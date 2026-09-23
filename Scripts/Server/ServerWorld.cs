using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia.Server;

/// Authoritative simulation. Runs on the host (or a dedicated headless server) at one tick per 0.6s.
/// Clients only ever send intents (ClientMsg); every outcome is decided here.
public sealed partial class ServerWorld : Node
{
    public Action<long, string> SendSnapshot, SendState, SendMsg;
    public Action<long> Kick;

    int tick;
    double acc;
    int nextId = 1, nextUid = 1;
    readonly Random rng = new();

    readonly Dictionary<long, PlayerEntity> byPeer = new();
    readonly Dictionary<int, Entity> entities = new();
    readonly List<NpcEntity> npcs = new();
    readonly List<GroundItem> ground = new();
    readonly Dictionary<string, List<Fx>> fx = new();
    readonly List<PendingHit> pending = new();

    public int Tick => tick;
    public IEnumerable<PlayerEntity> Players => byPeer.Values;

    public override void _Ready()
    {
        foreach (var id in MapGenerator.MapIds)
        {
            var map = MapGenerator.Get(id);
            fx[id] = new List<Fx>();
            foreach (var s in map.Spawns)
            {
                var def = NpcDb.Get(s.NpcId);
                if (def == null) { GD.PrintErr($"Unknown npc {s.NpcId}"); continue; }
                var n = new NpcEntity { Id = nextId++, Def = def, Map = map, Home = new Tile(s.X, s.Z), Pos = new Tile(s.X, s.Z), Hp = def.Hitpoints };
                n.LastPos = n.Pos;
                n.WanderTimer = rng.Next(0, 10);
                npcs.Add(n);
                entities[n.Id] = n;
            }
        }
        InitResources();
        GD.Print($"[Server] World ready: {npcs.Count} NPCs across {MapGenerator.MapIds.Length} maps.");
    }

    public override void _Process(double delta)
    {
        acc += delta;
        // Catch up at most a few ticks if we hitch.
        int guard = 0;
        while (acc >= GameConst.TickSeconds && guard++ < 3)
        {
            acc -= GameConst.TickSeconds;
            try { RunTick(); }
            catch (Exception e) { GD.PrintErr($"[Server] Tick error: {e}"); }
        }
        if (acc > GameConst.TickSeconds * 3) acc = 0;
    }

    void Emit(MapData map, Fx f) => fx[map.Id].Add(f);

    // ================= connection lifecycle =================

    public void OnPeerDisconnected(long peer)
    {
        if (!byPeer.TryGetValue(peer, out var p)) return;
        SaveAccount(p);
        byPeer.Remove(peer);
        entities.Remove(p.Id);
        foreach (var e in entities.Values) if (e.Target == p) e.Target = null;
        GD.Print($"[Server] {p.Name} logged out.");
    }

    public void SaveAll()
    {
        foreach (var p in byPeer.Values) SaveAccount(p);
    }

    void SaveAccount(PlayerEntity p)
    {
        var a = p.Acc;
        if (p.Dead) { a.Map = GameConst.OverworldId; a.X = -1; a.Z = -1; a.Hp = p.MaxHp; }
        else { a.Map = p.Map.Id; a.X = p.Pos.X; a.Z = p.Pos.Z; a.Hp = p.Hp; }
        Accounts.Save(a);
    }

    void HandleLogin(long peer, ClientMsg m)
    {
        if (byPeer.ContainsKey(peer)) return;
        string name = (m.S ?? "").Trim();
        if (byPeer.Values.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SendMsg?.Invoke(peer, Json.Write(new ServerMsg { T = "login_fail", S = "That account is already logged in." }));
            return;
        }
        var acc = Accounts.LoadOrCreate(name, m.S2, out var err);
        if (acc == null)
        {
            SendMsg?.Invoke(peer, Json.Write(new ServerMsg { T = "login_fail", S = err }));
            return;
        }
        var map = MapGenerator.Get(acc.Map) ?? MapGenerator.Get(GameConst.OverworldId);
        var pos = new Tile(acc.X, acc.Z);
        if (!map.Walkable(pos)) { map = MapGenerator.Get(GameConst.OverworldId); pos = map.Spawn; }
        var p = new PlayerEntity { Id = nextId++, PeerId = peer, Acc = acc, Map = map, Pos = pos, LastPos = pos };
        p.Hp = Math.Clamp(acc.Hp, 1, p.MaxHp);
        byPeer[peer] = p;
        entities[p.Id] = p;
        GD.Print($"[Server] {p.Name} logged in (peer {peer}).");
        SendMsg?.Invoke(peer, Json.Write(new ServerMsg { T = "login_ok", A = p.Id, S = p.Name }));
        GameMsg(p, "Welcome to Fantasia.");
        if (acc.Xp.Sum() == Xp.XpForLevel(10)) GameMsg(p, "Talk to Old Tobin by the fountain for some tips!");
        p.PrivateDirty = true;
    }

    // ================= messages =================

    public void Handle(long peer, ClientMsg m)
    {
        if (m == null || string.IsNullOrEmpty(m.T)) return;
        if (!byPeer.TryGetValue(peer, out var p))
        {
            if (m.T == C2S.Login) HandleLogin(peer, m);
            return;
        }
        if (--p.MsgBudget < 0)
        {
            if (++p.Spam > 200) { GD.Print($"[Server] Kicking {p.Name} for flooding."); Kick?.Invoke(peer); }
            return;
        }
        if (p.Dead) return;

        switch (m.T)
        {
            case C2S.Walk:
                CloseUi(p);
                ClearAction(p);
                if (!p.Map.InBounds(m.A, m.B)) return;
                p.SetPath(Pathfinder.Find(p.Map, p.Pos, new Tile(m.A, m.B)));
                break;
            case C2S.Npc: OnNpcOption(p, m.A, m.S); break;
            case C2S.Player: OnPlayerOption(p, m.A, m.S); break;
            case C2S.Ground:
                if (m.S != "Take") return;
                if (ground.All(g => g.Uid != m.A)) return;
                CloseUi(p);
                StartAction(p, Interaction.Take, m.A);
                break;
            case C2S.Object:
            {
                var o = p.Map.GetObject(m.A);
                if (o == null || o.Action == null) return;
                bool patchVerb = o.Kind == ObjKind.FarmPatch && m.S is "Plant" or "Water" or "Harvest" or "Cure" or "Clear" or "Inspect";
                if (o.Action != m.S && !patchVerb) return;
                CloseUi(p);
                StartAction(p, Interaction.Object, o.Id);
                p.ObjVerb = m.S;
                p.ObjArg = m.S2;
                break;
            }
            case C2S.Inv: OnInvOption(p, m.A, m.S); break;
            case C2S.Unequip: Unequip(p, m.A); break;
            case C2S.Swap:
                if (m.A < 0 || m.B < 0 || m.A >= p.Inv.Length || m.B >= p.Inv.Length) return;
                (p.Inv[m.A], p.Inv[m.B]) = (p.Inv[m.B], p.Inv[m.A]);
                p.PrivateDirty = true;
                break;
            case C2S.Style:
                p.Acc.Style = Math.Clamp(m.A, 0, 2); p.PrivateDirty = true; break;
            case C2S.Spell:
                if (string.IsNullOrEmpty(m.S)) { p.Acc.Spell = null; p.PrivateDirty = true; break; }
                var sp = SpellDb.Get(m.S);
                if (sp == null) return;
                if (sp.Kind != SpellKind.Combat) { CastUtility(p, sp); break; }
                if (p.Level(Skill.Sorcery) < sp.Level) { GameMsg(p, $"You need a Magic level of {sp.Level} to cast {sp.Name}."); return; }
                p.Acc.Spell = sp.Id; p.PrivateDirty = true;
                GameMsg(p, $"Autocasting {sp.Name}.");
                break;
            case C2S.Run: p.Acc.Run = m.A != 0; p.PrivateDirty = true; break;
            case C2S.Retaliate: p.Acc.Retal = m.A != 0; p.PrivateDirty = true; break;
            case C2S.Chat: OnChat(p, m.S); break;
            case C2S.Buy: Buy(p, m.S, m.A); break;
            case C2S.Sell: Sell(p, m.A, m.B); break;
            case C2S.Deposit: Deposit(p, m.A, m.B); break;
            case C2S.DepositAll: DepositAll(p); break;
            case C2S.Withdraw: Withdraw(p, m.A, m.B); break;
            case C2S.CloseUi: CloseUi(p, false); p.CraftStation = null; break;
            case C2S.Craft: StartCraft(p, m.S, m.A); break;
            case C2S.Quest: AnswerQuest(p, m.S, m.A); break;
        }
    }

    void StartAction(PlayerEntity p, Interaction a, int targetId)
    {
        p.Action = a;
        p.ActionTargetId = targetId;
        p.Target = null;
        p.Path.Clear();
    }

    void ClearAction(PlayerEntity p)
    {
        p.Action = Interaction.None;
        p.ActionTargetId = -1;
        p.Target = null;
        if (p.Activity != null) { p.Activity = null; p.PrivateDirty = true; }
    }

    void OnNpcOption(PlayerEntity p, int id, string opt)
    {
        if (!entities.TryGetValue(id, out var e) || e is not NpcEntity n || n.Map != p.Map || n.Dead) return;
        if (!n.Def.Options.Contains(opt)) return;
        CloseUi(p);
        switch (opt)
        {
            case "Attack":
                if (!n.Def.Attackable) return;
                StartAction(p, Interaction.Attack, id);
                p.Target = n;
                break;
            case "Talk": StartAction(p, Interaction.Talk, id); break;
            case "Trade": StartAction(p, Interaction.Trade, id); break;
            case "Bank": StartAction(p, Interaction.Bank, id); break;
        }
    }

    void OnPlayerOption(PlayerEntity p, int id, string opt)
    {
        if (!entities.TryGetValue(id, out var e) || e is not PlayerEntity other || other == p || other.Map != p.Map) return;
        CloseUi(p);
        if (opt == "Follow") { StartAction(p, Interaction.Follow, id); return; }
        if (opt == "Attack")
        {
            if (!p.Map.IsPvp(p.Pos) || !p.Map.IsPvp(other.Pos)) { GameMsg(p, "You can only attack other adventurers in the Bloodmarch."); return; }
            StartAction(p, Interaction.Attack, id);
            p.Target = other;
        }
    }

    /// Dev commands are only honoured for the host's own player when the server runs with --dev.
    public bool DevMode;

    bool DevCommand(PlayerEntity p, string text)
    {
        if (!text.StartsWith("::")) return false;
        if (!DevMode || p.PeerId != 1) { GameMsg(p, "Unknown command."); return true; }
        var a = text[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) return true;
        try
        {
            switch (a[0])
            {
                case "tele":
                {
                    var map = p.Map;
                    int i = 1;
                    if (a.Length > 1 && !int.TryParse(a[1], out _)) { map = MapGenerator.Get(a[1]) ?? map; i = 2; }
                    var dest = a.Length > i + 1 ? new Tile(int.Parse(a[i]), int.Parse(a[i + 1])) : map.Spawn;
                    Teleport(p, map, NearestWalkable(map, dest));
                    break;
                }
                case "item":
                    if (ItemDb.Get(a[1]) != null) p.Add(a[1], a.Length > 2 ? int.Parse(a[2]) : 1);
                    break;
                case "lvl":
                    if (Enum.TryParse<Skill>(a[1], true, out var sk))
                    {
                        p.Xp[(int)sk] = Xp.XpForLevel(Math.Clamp(int.Parse(a[2]), 1, 99));
                        if (sk == Skill.Vitality) p.Hp = p.MaxHp;
                        p.PrivateDirty = true;
                    }
                    break;
                case "boss":
                {
                    var boss = npcs.FirstOrDefault(n => n.Map == p.Map && n.Def.Boss);
                    if (boss != null)
                    {
                        var spot = Pathfinder.Search(p.Map, boss.Home, t => t.Chebyshev(boss.Home) >= 4, boss.Home);
                        Teleport(p, p.Map, spot.Count > 0 ? spot[^1] : p.Map.Spawn);
                    }
                    break;
                }
                case "heal": p.Hp = p.MaxHp; p.PrivateDirty = true; break;
                case "unlock":
                    foreach (var u in RecipeDb.Unlocks) if (a.Length < 2 || a[1] == "all" || a[1] == u.Key) Learn(p, u.Key);
                    break;
                case "grow":
                    foreach (var pt in p.Acc.Patches) { var c = FarmDb.Get(pt.Crop); if (c != null) { pt.Planted = FarmDb.Now - c.GrowSeconds - 1; pt.DiseaseAt = -1; } }
                    p.PrivateDirty = true;
                    break;
                case "frenzy":
                {
                    var spot = p.Map.Objects.Where(o => o.Kind == ObjKind.FishingSpot && !IsDepleted(p.Map, o)).OrderBy(o => o.DistanceTo(p.Pos)).FirstOrDefault();
                    if (spot != null) StartFrenzy(spot, p.Map);
                    break;
                }
                case "quest":
                    if (QuestDb.Get(a[1]) != null) { p.Acc.Quests[a[1]] = int.Parse(a[2]); p.PrivateDirty = true; }
                    break;
                case "skills":
                    foreach (var s in Skills.All) if (!Skills.IsCombat(s)) p.Xp[(int)s] = Xp.XpForLevel(Math.Clamp(int.Parse(a[1]), 1, 99));
                    p.PrivateDirty = true;
                    break;
            }
        }
        catch (Exception e) { GameMsg(p, "Bad command: " + e.Message); }
        return true;
    }

    static Tile NearestWalkable(MapData map, Tile t)
    {
        for (int r = 0; r < 12; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r && map.Walkable(t.X + dx, t.Z + dz))
                        return new Tile(t.X + dx, t.Z + dz);
        return map.Spawn;
    }

    void OnChat(PlayerEntity p, string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && DevCommand(p, text.Trim())) return;
        if (string.IsNullOrWhiteSpace(text) || p.ChatCooldown > 0) return;
        p.ChatCooldown = 1;
        text = new string(text.Where(c => !char.IsControl(c) && c != '[' && c != ']').ToArray()).Trim();
        if (text.Length > 80) text = text[..80];
        if (text.Length == 0) return;
        Emit(p.Map, new Fx { T = "say", A = p.Id, S = text });
        var msg = Json.Write(new ServerMsg { T = "chat", S = p.Name, S2 = text });
        foreach (var o in byPeer.Values)
            if (o.Map == p.Map) SendMsg?.Invoke(o.PeerId, msg);
    }

    public void GameMsg(PlayerEntity p, string text) =>
        SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "game", S = text }));

    void CloseUi(PlayerEntity p, bool notify = true)
    {
        bool had = p.OpenShop != null || p.BankOpen;
        p.OpenShop = null; p.ShopNpcId = -1; p.BankOpen = false;
        if (had && notify) SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "close" }));
    }

    // ================= tick =================

    void RunTick()
    {
        tick++;
        foreach (var p in byPeer.Values)
        {
            p.MsgBudget = 40;
            p.Spam = Math.Max(0, p.Spam - 5);
            if (p.ChatCooldown > 0) p.ChatCooldown--;
            if (p.EatTimer > 0) p.EatTimer--;
            p.LastPos = p.Pos;
        }
        foreach (var n in npcs) n.LastPos = n.Pos;

        foreach (var p in byPeer.Values.ToList()) if (!p.Dead) ProcessPlayerAction(p);
        foreach (var n in npcs) ProcessNpc(n);

        foreach (var p in byPeer.Values) if (!p.Dead) Move(p, p.Acc.Run ? 2 : 1);
        foreach (var n in npcs) if (!n.Dead) Move(n, 1);

        foreach (var p in byPeer.Values.ToList()) if (!p.Dead) PlayerCombat(p);
        foreach (var n in npcs) if (!n.Dead) NpcCombat(n);

        ProcessHits();
        ProcessDeaths();
        Regenerate();
        TickResources();

        ground.RemoveAll(g => tick - g.SpawnTick > GameConst.GroundItemTicks);

        foreach (var p in byPeer.Values)
        {
            if (++p.SaveTimer >= 100) { p.SaveTimer = 0; SaveAccount(p); }
        }
        BroadcastSnapshots();
        foreach (var l in fx.Values) l.Clear();
    }

    void Move(Entity e, int steps)
    {
        for (int i = 0; i < steps && e.Path.Count > 0; i++)
        {
            var next = e.Path.Peek();
            int dx = next.X - e.Pos.X, dz = next.Z - e.Pos.Z;
            if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1 || !Pathfinder.CanStep(e.Map, e.Pos, dx, dz)) { e.Path.Clear(); break; }
            e.Path.Dequeue();
            e.Pos = next;
        }
    }

    void Regenerate()
    {
        if (tick % 100 != 0) return;
        foreach (var p in byPeer.Values)
            if (!p.Dead && p.Hp < p.MaxHp) { p.Hp++; p.PrivateDirty = true; }
        foreach (var n in npcs)
            if (!n.Dead && n.Target == null && n.Hp < n.MaxHp) n.Hp = Math.Min(n.MaxHp, n.Hp + 2);
    }

    // ================= player actions =================

    void ProcessPlayerAction(PlayerEntity p)
    {
        switch (p.Action)
        {
            case Interaction.None: return;
            case Interaction.Gather: GatherTick(p); return;
            case Interaction.Craft: CraftTick(p); return;
            case Interaction.Harvest: HarvestTick(p); return;
            case Interaction.Attack:
            {
                if (p.Target == null || p.Target.Dead || p.Target.Map != p.Map || !entities.ContainsKey(p.Target.Id)) { ClearAction(p); return; }
                if (p.Target is PlayerEntity tp && (!p.Map.IsPvp(p.Pos) || !p.Map.IsPvp(tp.Pos))) { GameMsg(p, "Your target has fled the Bloodmarch."); ClearAction(p); return; }
                var info = AttackInfo(p);
                PathIntoRange(p, p.Target, info.Range, info.Type == CombatType.Melee);
                return;
            }
            case Interaction.Follow:
            {
                if (!entities.TryGetValue(p.ActionTargetId, out var t) || t.Map != p.Map || t.Dead) { ClearAction(p); return; }
                if (p.Pos.Chebyshev(t.Pos) > 1)
                    p.SetPath(Pathfinder.Search(p.Map, p.Pos, x => x.Chebyshev(t.LastPos) <= 1 && x != t.Pos, t.Pos));
                return;
            }
            case Interaction.Talk:
            case Interaction.Trade:
            case Interaction.Bank:
            {
                if (!entities.TryGetValue(p.ActionTargetId, out var t) || t is not NpcEntity n || n.Map != p.Map || n.Dead) { ClearAction(p); return; }
                int reach = p.Action == Interaction.Talk ? 1 : 2;
                if (p.Pos.Chebyshev(n.Pos) <= reach && p.Map.HasLineOfSight(p.Pos, n.Pos) && p.Pos != n.Pos)
                {
                    p.Path.Clear();
                    var act = p.Action;
                    ClearAction(p);
                    if (act == Interaction.Talk && !TalkQuest(p, n))
                        SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "dialog", S = n.Def.Name, A = n.Id, Lines = n.Def.Dialogue ?? new[] { "..." } }));
                    else if (act == Interaction.Trade && n.Def.ShopId != null)
                    {
                        p.OpenShop = n.Def.ShopId; p.ShopNpcId = n.Id;
                        SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "shop", S = n.Def.ShopId }));
                    }
                    else if (act == Interaction.Bank) OpenBank(p);
                    return;
                }
                if (p.Path.Count == 0 || tick % 2 == 0)
                    p.SetPath(Pathfinder.Search(p.Map, p.Pos, x => x.Chebyshev(n.Pos) <= reach && x != n.Pos && p.Map.HasLineOfSight(x, n.Pos), n.Pos));
                if (p.Path.Count == 0 && p.Pos.Chebyshev(n.Pos) > reach) { GameMsg(p, "You can't reach that."); ClearAction(p); }
                return;
            }
            case Interaction.Take:
            {
                var g = ground.FirstOrDefault(x => x.Uid == p.ActionTargetId);
                if (g == null || g.Map != p.Map) { ClearAction(p); return; }
                bool reachable = p.Pos == g.Pos || (!p.Map.Walkable(g.Pos) && p.Pos.Chebyshev(g.Pos) <= 1);
                if (reachable)
                {
                    ClearAction(p);
                    if (!p.CanAdd(g.Stack.Id, g.Stack.Count)) { GameMsg(p, "You don't have enough inventory space."); return; }
                    int left = p.Add(g.Stack.Id, g.Stack.Count);
                    if (left <= 0) ground.Remove(g); else g.Stack.Count = left;
                    Emit(p.Map, new Fx { T = "anim", A = p.Id, S = "pickup" });
                    return;
                }
                if (p.Path.Count == 0)
                {
                    p.SetPath(Pathfinder.Search(p.Map, p.Pos, x => x == g.Pos || (!p.Map.Walkable(g.Pos) && x.Chebyshev(g.Pos) <= 1), g.Pos));
                    if (p.Path.Count == 0) { GameMsg(p, "You can't reach that."); ClearAction(p); }
                }
                return;
            }
            case Interaction.Object:
            {
                var o = p.Map.GetObject(p.ActionTargetId);
                if (o == null) { ClearAction(p); return; }
                if (o.DistanceTo(p.Pos) <= 1 && !o.Covers(p.Pos.X, p.Pos.Z))
                {
                    p.Path.Clear();
                    ClearAction(p);
                    UseObject(p, o, p.ObjVerb, p.ObjArg);
                    return;
                }
                if (p.Path.Count == 0)
                {
                    p.SetPath(Pathfinder.Search(p.Map, p.Pos, x => o.DistanceTo(x) <= 1 && !o.Covers(x.X, x.Z), o.Center));
                    if (p.Path.Count == 0) { GameMsg(p, "You can't reach that."); ClearAction(p); }
                }
                return;
            }
        }
    }

    void UseObject(PlayerEntity p, WorldObject o, string verb = null, string arg = null)
    {
        if (o.Kind == ObjKind.FarmPatch) { UsePatch(p, o, verb, arg); return; }
        if (o.Res != null) { StartGather(p, o); return; }
        if (o.Station != null) { OpenStation(p, o, o.Station); return; }
        switch (o.Action)
        {
            case "Bank": OpenBank(p); break;
            case "Read": SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "dialog", S = o.Name, A = -1, Lines = new[] { o.Text ?? "It's blank." } })); break;
            case "Enter":
            case "Climb-up":
            case "Climb-down":
            {
                var target = MapGenerator.Get(o.TargetMap);
                if (target == null) return;
                var dest = target.Id == GameConst.OverworldId ? new Tile(o.TargetX, o.TargetZ) : target.Spawn;
                Teleport(p, target, dest);
                GameMsg(p, target.Underground ? $"You enter the {target.Name}." : "You climb back up into the daylight.");
                break;
            }
        }
    }

    void Teleport(PlayerEntity p, MapData map, Tile dest)
    {
        if (!map.Walkable(dest)) dest = map.Spawn;
        Emit(p.Map, new Fx { T = "tele", A = p.Id });
        p.Map = map; p.Pos = dest; p.LastPos = dest;
        p.Path.Clear();
        ClearAction(p);
        CloseUi(p);
        foreach (var e in entities.Values) if (e.Target == p) e.Target = null;
    }

    void OpenBank(PlayerEntity p)
    {
        p.BankOpen = true;
        p.BankTile = p.Pos;
        p.BankDirty = true;
        p.PrivateDirty = true;
        SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "bank" }));
    }

    // ================= NPC AI =================

    void ProcessNpc(NpcEntity n)
    {
        if (n.Dead) return;
        if (n.AttackTimer > 0) n.AttackTimer--;
        n.CombatTicks++;

        if (n.Target != null)
        {
            var t = n.Target;
            bool lost = t.Dead || t.Map != n.Map || !entities.ContainsKey(t.Id)
                        || n.Pos.Chebyshev(n.Home) > 14 || n.Pos.Chebyshev(t.Pos) > 16
                        || n.CombatTicks > 40;
            if (lost)
            {
                n.Target = null;
                n.SetPath(Pathfinder.Find(n.Map, n.Pos, n.Home));
                return;
            }
            var (range, melee) = NpcRange(n);
            if (n.Pos == t.Pos)
            {
                StepOff(n);
                return;
            }
            if (!CanAttackFrom(n.Map, n.Pos, t.Pos, range, melee))
            {
                n.Path.Clear();
                var step = GreedyStep(n.Map, n.Pos, t.Pos, melee);
                if (step.HasValue) n.Path.Enqueue(step.Value);
            }
            else n.Path.Clear();
            return;
        }

        if (n.Def.Aggressive)
        {
            PlayerEntity best = null; int bd = int.MaxValue;
            foreach (var p in byPeer.Values)
            {
                if (p.Dead || p.Map != n.Map) continue;
                int d = p.Pos.Chebyshev(n.Pos);
                if (d > n.Def.AggroRange || !n.Map.HasLineOfSight(n.Pos, p.Pos)) continue;
                if (!n.Map.Underground && p.CombatLevel > n.Def.CombatLevel * 2) continue;
                if (d < bd) { bd = d; best = p; }
            }
            if (best != null) { n.Target = best; n.CombatTicks = 0; return; }
        }

        if (n.Path.Count == 0 && n.Def.WanderRadius > 0 && --n.WanderTimer <= 0)
        {
            n.WanderTimer = rng.Next(4, 14);
            int r = n.Def.WanderRadius;
            var dest = new Tile(n.Home.X + rng.Next(-r, r + 1), n.Home.Z + rng.Next(-r, r + 1));
            if (n.Map.Walkable(dest))
            {
                var path = Pathfinder.Find(n.Map, n.Pos, dest);
                if (path.Count <= r * 3) n.SetPath(path);
            }
        }
    }

    void StepOff(Entity e)
    {
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (Pathfinder.CanStep(e.Map, e.Pos, dx, dz)) { e.Path.Clear(); e.Path.Enqueue(new Tile(e.Pos.X + dx, e.Pos.Z + dz)); return; }
    }

    static Tile? GreedyStep(MapData map, Tile from, Tile to, bool melee)
    {
        int dx = Math.Sign(to.X - from.X), dz = Math.Sign(to.Z - from.Z);
        // Melee attackers shouldn't end on a diagonal, so prefer axis moves when one away.
        bool nearDiag = melee && Math.Abs(to.X - from.X) == 1 && Math.Abs(to.Z - from.Z) == 1;
        if (!nearDiag && dx != 0 && dz != 0 && Pathfinder.CanStep(map, from, dx, dz) && new Tile(from.X + dx, from.Z + dz) != to) return new Tile(from.X + dx, from.Z + dz);
        if (dx != 0 && Pathfinder.CanStep(map, from, dx, 0) && new Tile(from.X + dx, from.Z) != to) return new Tile(from.X + dx, from.Z);
        if (dz != 0 && Pathfinder.CanStep(map, from, 0, dz) && new Tile(from.X, from.Z + dz) != to) return new Tile(from.X, from.Z + dz);
        return null;
    }

    (int range, bool melee) NpcRange(NpcEntity n)
    {
        if (n.Def.Boss && n.Def.Magic > 1 && n.Def.Style == CombatType.Melee) return (8, false);
        return (n.Def.AttackRange, n.Def.Style == CombatType.Melee);
    }

    public static bool CanAttackFrom(MapData map, Tile from, Tile to, int range, bool melee)
    {
        int d = from.Chebyshev(to);
        if (d == 0) return false;
        if (melee)
        {
            if (d != 1 || (from.X != to.X && from.Z != to.Z)) return false;
            return Pathfinder.CanStep(map, from, to.X - from.X, to.Z - from.Z) || !map.Walkable(to);
        }
        return d <= range && map.HasLineOfSight(from, to);
    }

    void PathIntoRange(PlayerEntity p, Entity t, int range, bool melee)
    {
        if (p.Pos == t.Pos) { StepOff(p); return; }
        if (CanAttackFrom(p.Map, p.Pos, t.Pos, range, melee)) { p.Path.Clear(); return; }
        var path = Pathfinder.Search(p.Map, p.Pos, x => CanAttackFrom(p.Map, x, t.Pos, range, melee), t.Pos);
        p.SetPath(path);
    }

    // ================= deaths =================

    void ProcessDeaths()
    {
        foreach (var n in npcs)
        {
            if (!n.Dead) continue;
            if (n.DeathTimer > 0) { n.DeathTimer--; continue; }
            if (--n.RespawnTimer <= 0)
            {
                n.Dead = false;
                n.Hp = n.MaxHp;
                n.Pos = n.LastPos = n.Home;
                n.Path.Clear();
                n.Target = null;
                n.AttackTimer = 0;
            }
        }
        foreach (var p in byPeer.Values)
        {
            if (!p.Dead) continue;
            if (--p.DeathTimer > 0) continue;
            p.Dead = false;
            p.Hp = p.MaxHp;
            var ow = MapGenerator.Get(GameConst.OverworldId);
            Teleport(p, ow, ow.Spawn);
            p.PrivateDirty = true;
            GameMsg(p, "You wake up in Aldmoor, feeling rather sore.");
        }
    }

    void Kill(Entity victim, Entity killer)
    {
        victim.Dead = true;
        victim.Path.Clear();
        victim.Target = null;
        Emit(victim.Map, new Fx { T = "death", A = victim.Id });
        foreach (var e in entities.Values)
            if (e.Target == victim) { e.Target = null; if (e is PlayerEntity pe && pe.Action == Interaction.Attack) ClearAction(pe); }

        if (victim is NpcEntity n)
        {
            n.DeathTimer = 2;
            n.RespawnTimer = n.Def.RespawnTicks;
            foreach (var d in n.Def.Drops)
            {
                if (rng.NextDouble() > d.Chance) continue;
                int count = rng.Next(d.Min, d.Max + 1);
                var def = ItemDb.Get(d.Item);
                if (def == null) continue;
                if (def.Stackable) DropItem(n.Map, n.Pos, d.Item, count, killer?.Id ?? -1);
                else for (int i = 0; i < count; i++) DropItem(n.Map, n.Pos, d.Item, 1, killer?.Id ?? -1);
            }
            if (killer is PlayerEntity kp && n.Def.Boss)
                foreach (var o in byPeer.Values)
                    GameMsg(o, $"{kp.Name} has slain {n.Def.Name}!");
            if (killer is PlayerEntity qp) QuestKill(qp, n.Def);
        }
        else if (victim is PlayerEntity p)
        {
            p.DeathTimer = 5;
            GameMsg(p, "You have fallen in battle...");
            p.PrivateDirty = true;
            CloseUi(p);
            if (killer is PlayerEntity kp && p.Map.IsPvp(p.Pos))
            {
                PvpDrop(p, kp);
                GameMsg(kp, $"You have defeated {p.Name}!");
            }
        }
    }

    /// In the Bloodmarch you keep your 3 most valuable items; the rest drop for the killer.
    void PvpDrop(PlayerEntity p, PlayerEntity killer)
    {
        var all = new List<(ItemStack st, bool eq, int idx)>();
        for (int i = 0; i < p.Inv.Length; i++) if (p.Inv[i] != null) all.Add((p.Inv[i], false, i));
        for (int i = 0; i < p.Equip.Length; i++) if (p.Equip[i] != null) all.Add((p.Equip[i], true, i));
        var keep = all.Where(x => !x.st.Def.Stackable).OrderByDescending(x => x.st.Def.Value).Take(3).ToHashSet();
        foreach (var x in all)
        {
            if (keep.Contains(x)) continue;
            DropItem(p.Map, p.Pos, x.st.Id, x.st.Count, killer.Id);
            if (x.eq) p.Equip[x.idx] = null; else p.Inv[x.idx] = null;
        }
        p.PrivateDirty = true;
    }

    void DropItem(MapData map, Tile pos, string id, int count, int owner)
    {
        var def = ItemDb.Get(id);
        if (def == null || count <= 0) return;
        if (def.Stackable)
        {
            var ex = ground.FirstOrDefault(g => g.Map == map && g.Pos == pos && g.Stack.Id == id);
            if (ex != null) { ex.Stack.Count += count; ex.SpawnTick = tick; return; }
        }
        ground.Add(new GroundItem { Uid = nextUid++, Map = map, Pos = pos, Stack = new ItemStack(id, count), SpawnTick = tick, OwnerId = owner });
    }

    // ================= snapshots =================

    void BroadcastSnapshots()
    {
        foreach (var p in byPeer.Values)
        {
            var snap = new Snapshot { Tick = tick, Map = p.Map.Id, You = p.Id, Pvp = p.Map.IsPvp(p.Pos) };
            foreach (var e in entities.Values)
            {
                if (e.Map != p.Map || e.Pos.Chebyshev(p.Pos) > GameConst.ViewDistance) continue;
                if (e is NpcEntity dn && dn.Dead && dn.DeathTimer <= 0) continue;
                var s = new EntitySnap { Id = e.Id, X = e.Pos.X, Z = e.Pos.Z, Hp = e.Hp, Max = e.MaxHp, Tgt = e.Target?.Id ?? -1, Cb = e.CombatLevel, Dead = e.Dead };
                if (e is NpcEntity n) { s.K = 1; s.D = n.Def.Id; }
                else if (e is PlayerEntity op)
                {
                    s.K = 0; s.D = op.Name;
                    s.Eq = op.Equip.Select(x => x?.Id).ToArray();
                    if (op.Action == Interaction.Attack && op.Target == null) s.Tgt = -1;
                }
                snap.E.Add(s);
            }
            foreach (var g in ground)
                if (g.Map == p.Map && g.Pos.Chebyshev(p.Pos) <= GameConst.ViewDistance)
                    snap.G.Add(new GroundSnap { U = g.Uid, I = g.Stack.Id, N = g.Stack.Count, X = g.Pos.X, Z = g.Pos.Z });
            snap.Fx = fx[p.Map.Id];
            (snap.Dep, snap.Frz) = ResourceView(p);
            SendSnapshot?.Invoke(p.PeerId, Json.Write(snap));

            if (p.PrivateDirty)
            {
                p.PrivateDirty = false;
                var ps = new PrivateState
                {
                    Xp = p.Xp, Hp = p.Hp, Inv = p.Inv, Eq = p.Equip, Style = p.Acc.Style, Spell = p.Acc.Spell,
                    Run = p.Acc.Run, Retal = p.Acc.Retal, Bonus = p.Bonuses(),
                    Bank = p.BankOpen ? p.Acc.Bank : null,
                    Unlocks = p.Acc.Unlocks.ToArray(), Quests = p.Acc.Quests, QVars = p.Acc.QVars, Patches = p.Acc.Patches,
                    Now = FarmDb.Now, Activity = p.Activity,
                };
                SendState?.Invoke(p.PeerId, Json.Write(ps));
            }
        }
    }
}
