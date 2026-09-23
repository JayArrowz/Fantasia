using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.UI;

namespace Fantasia.Client;

public sealed class MenuOption
{
    public string Verb;       // "Attack"
    public string Target;     // "Goblin"
    public Color TargetColor = new(1f, 1f, 0.3f);
    public string Suffix;     // " (lvl 5)"
    public Action Run;
    public bool Red;          // red click marker (interaction)
    public Vector3 MarkerPos;
}

/// Client-side game scene: renders server snapshots, handles picking and input, owns the HUD.
public partial class GameWorld : Node3D
{
    public static GameWorld I { get; private set; }

    public MapData Map { get; private set; }
    public readonly Dictionary<int, EntityView> Views = new();
    public readonly Dictionary<int, GroundItemView> Items = new();
    public CameraRig Rig { get; private set; }
    public Hud Hud { get; private set; }
    public int MyId;
    public string MyName;
    public EntityView Me => Views.TryGetValue(MyId, out var v) ? v : null;
    public PrivateState State { get; private set; }
    public bool Pvp { get; private set; }

    MapView mapView;
    public MapView MapViewNode => mapView;
    Node3D entityRoot, itemRoot, fxRoot;
    int[] lastXp;
    Vector2 lastMouse;
    float hoverTimer;

    public override void _EnterTree() => I = this;
    public override void _ExitTree()
    {
        if (I == this) I = null;
        Net.I.SnapshotReceived -= OnSnapshot;
        Net.I.StateReceived -= OnState;
        Net.I.MessageReceived -= OnMessage;
        EquipTuning.Changed -= RefitAll;
    }

    /// equipment.json was edited (debug builds): refit everyone's gear with the new values.
    void RefitAll()
    {
        ArmourFit.ClearCaches();
        foreach (var v in Views.Values) v.Body?.Refit();
    }

    public override void _Ready()
    {
        EquipTuning.Changed += RefitAll;
        Rig = new CameraRig { Name = "CameraRig" };
        AddChild(Rig);
        entityRoot = new Node3D { Name = "Entities" };
        itemRoot = new Node3D { Name = "GroundItems" };
        fxRoot = new Node3D { Name = "Fx" };
        AddChild(entityRoot); AddChild(itemRoot); AddChild(fxRoot);
        Hud = Hud.Create();
        AddChild(Hud);
        Net.I.SnapshotReceived += OnSnapshot;
        Net.I.StateReceived += OnState;
        Net.I.MessageReceived += OnMessage;
        Music.I?.Play("overworld");
    }

    // ================= network =================

    void LoadMap(string id)
    {
        mapView?.QueueFree();
        foreach (var v in Views.Values) v.QueueFree();
        Views.Clear();
        foreach (var g in Items.Values) g.QueueFree();
        Items.Clear();
        Map = MapGenerator.Get(id);
        mapView = new MapView { Name = "Map" };
        AddChild(mapView);
        mapView.Build(Map);
        Hud.OnMapChanged(Map);
        Music.I?.Play(Map.Music);
    }

    void OnSnapshot(Snapshot s)
    {
        if (Map == null || Map.Id != s.Map) LoadMap(s.Map);
        MyId = s.You;
        if (Pvp != s.Pvp) { Pvp = s.Pvp; Hud.SetPvp(Pvp); }

        var seen = new HashSet<int>();
        foreach (var e in s.E)
        {
            seen.Add(e.Id);
            if (!Views.TryGetValue(e.Id, out var v))
            {
                v = new EntityView { Name = $"E{e.Id}" };
                entityRoot.AddChild(v);
                v.Init(e, Map, e.Id == MyId);
                Views[e.Id] = v;
                if (e.Id == MyId) { Rig.Follow = v; Rig.SnapToFollow(); }
            }
            else v.Apply(e);
        }
        foreach (var id in Views.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Views[id].QueueFree();
            Views.Remove(id);
        }

        var seenItems = new HashSet<int>();
        foreach (var g in s.G)
        {
            seenItems.Add(g.U);
            if (Items.TryGetValue(g.U, out var gv)) { gv.Count = g.N; continue; }
            var def = ItemDb.Get(g.I);
            if (def == null) continue;
            gv = new GroundItemView { Uid = g.U, ItemId = g.I, Count = g.N, Tile = new Tile(g.X, g.Z) };
            var mdl = ItemVisuals.Ground(def);
            gv.AddChild(mdl);
            float jitter = (Hash.Unit(g.U, 1) - 0.5f) * 0.4f;
            var pos = Map.TileCenter(gv.Tile) + new Vector3(jitter, 0.02f, (Hash.Unit(g.U, 2) - 0.5f) * 0.4f);
            if (Map.Ground[g.X, g.Z] == Ground.Water) pos.Y = 0.45f;
            gv.Position = pos;
            gv.RotationDegrees = new Vector3(0, Hash.Unit(g.U, 3) * 360f, 0);
            itemRoot.AddChild(gv);
            Items[g.U] = gv;
        }
        foreach (var id in Items.Keys.Where(k => !seenItems.Contains(k)).ToList())
        {
            Items[id].QueueFree();
            Items.Remove(id);
        }

        mapView.SetResourceState(s.Dep, s.Frz);
        foreach (var f in s.Fx) HandleFx(f);
        Hud.OnSnapshot(s);
    }

    void HandleFx(Fx f)
    {
        Views.TryGetValue(f.A, out var a);
        switch (f.T)
        {
            case "hit":
                a?.OnHit(f.B, f.C, f.S);
                if (a != null && f.B > 0) Sfx.I?.Play(a.IsMe ? "hurt" : "hit", a.GlobalPosition);
                else if (a != null) Sfx.I?.Play("block", a.GlobalPosition);
                break;
            case "anim":
            {
                if (a == null) break;
                if (f.B > 0 && Views.TryGetValue(f.B, out var tgt)) a.FaceToward(tgt.Position);
                if (f.S == "stop") { a.Body.StopAction(); break; }
                var bar = f.S.IndexOf('|');
                string anim = bar < 0 ? f.S : f.S[..bar];
                if (bar >= 0) a.Body.ShowTool(f.S[(bar + 1)..], 2.2f);
                if (f.C > 0 && Map.GetObject(f.C - 1) is { } faceObj)
                    a.FaceToward(new Vector3(faceObj.X + faceObj.W / 2f, a.Position.Y, faceObj.Z + faceObj.D / 2f));
                a.Body.Play(anim);
                if (f.S is "slash" or "stab" or "heavy") Sfx.I?.Play("swing", a.GlobalPosition);
                else if (f.S == "shoot") Sfx.I?.Play("bow", a.GlobalPosition);
                else if (f.S == "cast") Sfx.I?.Play("cast", a.GlobalPosition);
                else if (f.S == "eat") Sfx.I?.Play("eat", a.GlobalPosition);
                else if (f.S == "pickup") Sfx.I?.Play("pickup", a.GlobalPosition);
                break;
            }
            case "quest":
            case "learn":
                if (a != null)
                {
                    fxRoot.AddChild(Burst.Make(a.GlobalPosition + Vector3.Up * 2f, f.T == "quest" ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.9f, 0.5f), 90, 4f, -2f, 0.13f));
                    if (a.IsMe) Hud.Banner(f.S, f.T == "quest" ? new Color(0.5f, 0.9f, 1f) : new Color(1f, 0.85f, 0.45f));
                }
                break;
            case "proj":
            {
                if (a == null || !Views.TryGetValue(f.B, out var to)) break;
                var parts = (f.S ?? "arrow:ffffff").Split(':');
                // The server applies the hit exactly C ticks after this message, so launch at the
                // animation's release point and fly for the remaining time.
                float total = Math.Max(1, f.C) * GameConst.TickSeconds;
                float release = Math.Min(0.3f, total * 0.4f);
                var p = new Projectile { From = a, To = to, LaunchDelay = release, Duration = total - release, Magic = parts[0] == "magic", Color = Color.FromHtml(parts.Length > 1 ? parts[1] : "ffffff") };
                fxRoot.AddChild(p);
                break;
            }
            case "death":
                if (a != null)
                {
                    fxRoot.AddChild(Burst.Make(a.GlobalPosition + Vector3.Up * 0.5f, new Color(0.6f, 0.6f, 0.6f), 30, 1.5f, 0.5f, 0.25f));
                    Sfx.I?.Play("death", a.GlobalPosition);
                }
                break;
            case "lvl":
                if (a != null)
                {
                    fxRoot.AddChild(Burst.Make(a.GlobalPosition + Vector3.Up * 2f, new Color(1f, 0.85f, 0.2f), 120, 5f, -3f, 0.14f));
                    fxRoot.AddChild(Burst.Make(a.GlobalPosition + Vector3.Up * 2f, new Color(0.3f, 0.6f, 1f), 60, 4f, -3f, 0.12f));
                    if (a.IsMe) { Hud.LevelUp(f.S, f.B); Sfx.I?.Play("levelup", a.GlobalPosition); }
                }
                break;
            case "say":
                a?.Say(f.S);
                break;
            case "heal":
                if (a != null) fxRoot.AddChild(Burst.Make(a.GlobalPosition + Vector3.Up, new Color(0.4f, 1f, 0.5f), 70, 2.5f, 1.5f, 0.14f));
                break;
            case "tele":
                if (a != null) fxRoot.AddChild(Burst.Make(a.GlobalPosition + Vector3.Up, new Color(0.5f, 0.7f, 1f), 50, 2f, 1f, 0.15f));
                break;
        }
    }

    void OnState(PrivateState st)
    {
        if (lastXp != null && st.Xp != null)
            for (int i = 0; i < st.Xp.Length && i < lastXp.Length; i++)
                if (st.Xp[i] > lastXp[i]) Hud.XpDrop((Skill)i, st.Xp[i] - lastXp[i]);
        lastXp = (int[])st.Xp?.Clone();
        State = st;
        mapView?.UpdatePatches(st.Patches, st.Now);
        Hud.OnState(st);
    }

    void OnMessage(ServerMsg m)
    {
        switch (m.T)
        {
            case "game": Hud.GameMessage(m.S); break;
            case "chat": Hud.ChatMessage(m.S, m.S2); break;
            case "dialog": Hud.ShowDialog(m.S, m.Lines, m.A, m.Opts, m.S2); break;
            case "craft": Hud.OpenCraft(m.S, m.S2); break;
            case "shop": Hud.OpenShop(m.S); break;
            case "bank": Hud.OpenBank(); break;
            case "close": Hud.CloseWindows(false); break;
        }
    }

    public void Send(ClientMsg m) => Net.I.Send(m);

    // ================= picking =================

    static bool RayAabb(Vector3 o, Vector3 d, Aabb box, out float dist)
    {
        dist = 0;
        float tmin = 0f, tmax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float oi = o[i], di = d[i], mn = box.Position[i], mx = box.End[i];
            if (Mathf.Abs(di) < 1e-6f) { if (oi < mn || oi > mx) return false; continue; }
            float t1 = (mn - oi) / di, t2 = (mx - oi) / di;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        dist = tmin;
        return true;
    }

    bool RayTerrain(Vector3 o, Vector3 d, out Vector3 hit)
    {
        hit = Vector3.Zero;
        float prev = 0;
        for (float t = 0.5f; t < 250f; t += 0.4f)
        {
            var p = o + d * t;
            if (p.X < -5 || p.Z < -5 || p.X > Map.W + 5 || p.Z > Map.H + 5) { if (p.Y < -1) return false; prev = t; continue; }
            float h = Map.HeightAt(p.X, p.Z);
            if (p.Y <= h)
            {
                float lo = prev, hi = t;
                for (int i = 0; i < 10; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var q = o + d * mid;
                    if (q.Y <= Map.HeightAt(q.X, q.Z)) hi = mid; else lo = mid;
                }
                hit = o + d * hi;
                return true;
            }
            prev = t;
        }
        return false;
    }

    public List<MenuOption> OptionsAt(Vector2 screen)
    {
        var list = new List<MenuOption>();
        if (Map == null || Rig?.Cam == null) return list;
        var cam = Rig.Cam;
        var o = cam.ProjectRayOrigin(screen);
        var d = cam.ProjectRayNormal(screen);
        var hits = new List<(float dist, Action<List<MenuOption>> add, Action<List<MenuOption>> examine)>();

        foreach (var v in Views.Values)
        {
            if (v.Dead || v.IsMe) continue;
            float r = v.Radius;
            var box = new Aabb(v.GlobalPosition - new Vector3(r, 0, r), new Vector3(r * 2, v.VisualHeight, r * 2));
            if (!RayAabb(o, d, box, out float dist)) continue;
            var view = v;
            hits.Add((dist, l => AddEntityOptions(l, view), l => l.Add(new MenuOption { Verb = "Inspect", Target = view.DisplayName, TargetColor = view.IsPlayer ? Colors.White : new Color(1, 1, 0.3f), Run = () => Hud.GameMessage(view.IsPlayer ? $"It's {view.DisplayName}, a fellow adventurer." : view.Def?.Examine ?? "Hmm.") })));
        }
        var tileItems = new Dictionary<Tile, List<GroundItemView>>();
        foreach (var g in Items.Values)
        {
            var box = new Aabb(g.GlobalPosition - new Vector3(0.4f, 0.05f, 0.4f), new Vector3(0.8f, 0.5f, 0.8f));
            if (!RayAabb(o, d, box, out float dist)) continue;
            var gi = g;
            var def = ItemDb.Get(g.ItemId);
            hits.Add((dist - 0.3f, l => l.Add(new MenuOption
            {
                Verb = "Take", Target = def.Name + (g.Count > 1 ? $" ({g.Count})" : ""), TargetColor = new Color(1f, 0.6f, 0.25f), Red = true, MarkerPos = gi.GlobalPosition,
                Run = () => Send(new ClientMsg { T = C2S.Ground, A = gi.Uid, S = "Take" }),
            }), l => l.Add(new MenuOption { Verb = "Inspect", Target = def.Name, TargetColor = new Color(1f, 0.6f, 0.25f), Run = () => Hud.GameMessage(def.Examine) })));
        }
        foreach (var (obj, box) in mapView.Pickables)
        {
            bool gone = obj.Res != null && mapView.IsDepleted(obj.Id);
            if (gone && obj.Kind == ObjKind.FishingSpot) continue;
            var pick = gone && obj.Kind is ObjKind.TreeOak or ObjKind.TreePine or ObjKind.TreeWillow
                ? new Aabb(box.Position, new Vector3(box.Size.X, 0.6f, box.Size.Z)) : box;
            if (!RayAabb(o, d, pick, out float dist)) continue;
            var ob = obj;
            string name = gone && ob.Kind != ObjKind.OreRock ? "Tree stump" : ob.Name ?? Pretty(ob.Kind);
            var marker = box.GetCenter() with { Y = box.Position.Y };
            hits.Add((dist, l =>
            {
                if (ob.Kind == ObjKind.FarmPatch) { AddPatchOptions(l, ob, name, marker); return; }
                if (ob.Action != null && !gone)
                    l.Add(new MenuOption { Verb = ob.Action, Target = name, TargetColor = new Color(0.3f, 0.9f, 1f), Red = true, MarkerPos = marker,
                        Run = () => { Send(new ClientMsg { T = C2S.Object, A = ob.Id, S = ob.Action }); } });
            }, l => l.Add(new MenuOption { Verb = "Inspect", Target = name, TargetColor = new Color(0.3f, 0.9f, 1f), Run = () => Hud.GameMessage(gone ? "It'll grow back in time." : ExamineObject(ob)) })));
        }
        hits.Sort((a, b) => a.dist.CompareTo(b.dist));
        foreach (var h in hits) h.add(list);

        if (RayTerrain(o, d, out var ground))
        {
            var tile = new Tile((int)Mathf.Floor(ground.X), (int)Mathf.Floor(ground.Z));
            if (Map.InBounds(tile.X, tile.Z))
            {
                var mp = Map.TileCenter(tile);
                list.Add(new MenuOption { Verb = "Move here", Target = "", MarkerPos = mp, Run = () => Send(new ClientMsg { T = C2S.Walk, A = tile.X, B = tile.Z }) });
            }
        }
        foreach (var h in hits) h.examine(list);
        return list;
    }

    void AddEntityOptions(List<MenuOption> l, EntityView v)
    {
        if (v.IsPlayer)
        {
            string lvl = $" (lvl {v.CombatLevel})";
            var attack = new MenuOption { Verb = "Attack", Target = v.DisplayName, TargetColor = Colors.White, Suffix = lvl, Red = true, MarkerPos = v.GlobalPosition, Run = () => Send(new ClientMsg { T = C2S.Player, A = v.Id, S = "Attack" }) };
            var follow = new MenuOption { Verb = "Follow", Target = v.DisplayName, TargetColor = Colors.White, Suffix = lvl, Red = true, MarkerPos = v.GlobalPosition, Run = () => Send(new ClientMsg { T = C2S.Player, A = v.Id, S = "Follow" }) };
            if (Pvp) { l.Add(attack); l.Add(follow); } else l.Add(follow);
            return;
        }
        var def = v.Def;
        if (def == null) return;
        foreach (var opt in def.Options)
        {
            if (opt == "Attack" && !def.Attackable) continue;
            string suffix = def.Attackable ? $" (lvl {def.CombatLevel})" : "";
            var color = def.Attackable ? LevelColor(def.CombatLevel) : new Color(1, 1, 0.3f);
            l.Add(new MenuOption { Verb = opt, Target = def.Name, TargetColor = color, Suffix = suffix, Red = true, MarkerPos = v.GlobalPosition,
                Run = () => Send(new ClientMsg { T = C2S.Npc, A = v.Id, S = opt }) });
        }
    }

    /// RuneScape-style difficulty colouring of level text.
    public Color LevelColor(int npcLevel)
    {
        int mine = State != null ? MyCombatLevel() : 3;
        int diff = npcLevel - mine;
        if (diff >= 10) return new Color(1f, 0.1f, 0.1f);
        if (diff >= 4) return new Color(1f, 0.5f, 0.1f);
        if (diff >= 1) return new Color(1f, 0.9f, 0.2f);
        if (diff >= -3) return new Color(1f, 1f, 0.3f);
        if (diff >= -9) return new Color(0.6f, 1f, 0.2f);
        return new Color(0.2f, 1f, 0.2f);
    }

    public int MyCombatLevel()
    {
        if (State?.Xp == null) return 3;
        int L(Skill s) => Xp.LevelForXp(State.Xp[(int)s]);
        return Xp.CombatLevel(L(Skill.Prowess), L(Skill.Might), L(Skill.Fortitude), L(Skill.Vitality), L(Skill.Archery), L(Skill.Sorcery));
    }

    /// Allotment options depend on your own crop there.
    void AddPatchOptions(List<MenuOption> l, WorldObject ob, string name, Vector3 marker)
    {
        var pt = mapView.PatchFor(ob.Id);
        var (stage, phase, _) = FarmDb.Eval(pt, mapView.ServerNow);
        var col = new Color(0.3f, 0.9f, 1f);
        void Opt(string verb, string arg = null, string label = null) => l.Add(new MenuOption
        {
            Verb = label ?? verb, Target = name, TargetColor = col, Red = true, MarkerPos = marker,
            Run = () => Send(new ClientMsg { T = C2S.Object, A = ob.Id, S = verb, S2 = arg }),
        });
        switch (phase)
        {
            case PatchPhase.Empty:
            {
                int lvl = State != null ? Xp.LevelForXp(State.Xp[(int)Skill.Farming]) : 1;
                var seeds = FarmDb.Crops.Where(c => State?.Inv.Any(s => s?.Id == c.Seed) == true).OrderByDescending(c => c.Level).ToList();
                foreach (var c in seeds)
                    Opt("Plant", c.Seed, lvl >= c.Level ? $"Plant {c.Name.ToLowerInvariant()} in" : $"Plant {c.Name.ToLowerInvariant()} (lvl {c.Level}) in");
                if (seeds.Count == 0) Opt("Plant", null, "Plant seeds in");
                break;
            }
            case PatchPhase.Ready: Opt("Harvest"); break;
            case PatchPhase.Diseased: Opt("Cure"); break;
            case PatchPhase.Dead: Opt("Clear"); break;
            default:
                if (stage > pt.WaterStage && pt.Watered < 3) Opt("Water");
                break;
        }
        Opt("Inspect", null, "Check");
    }

    static string Pretty(ObjKind k) => k switch
    {
        ObjKind.TreeOak => "Oak tree", ObjKind.TreePine => "Pine tree", ObjKind.TreeDead => "Dead tree", ObjKind.TreeWillow => "Willow tree",
        ObjKind.RockLarge or ObjKind.RockSmall => "Rocks", ObjKind.CastleWall => "Wall", ObjKind.Tower => "Tower",
        _ => System.Text.RegularExpressions.Regex.Replace(k.ToString(), "(\\B[A-Z])", " $1"),
    };

    static string ExamineObject(WorldObject o) => o.Kind switch
    {
        ObjKind.TreeOak => "A sturdy oak.", ObjKind.TreePine => "A tall pine, smells of resin.", ObjKind.TreeDead => "This tree has seen better days.",
        ObjKind.TreeWillow => "A graceful willow.", ObjKind.Keep => "The seat of King Aldric.", ObjKind.Tower => "A watchtower of the town wall.",
        ObjKind.CastleWall => "Thick stone walls keep the town safe.", ObjKind.BankBooth => "Your money's safe here.",
        ObjKind.CaveEntrance => "A dark cave. Goblin voices echo from within.", ObjKind.Mausoleum => "An ancient crypt. A cold draft seeps from the doorway.",
        ObjKind.Ladder => "It leads back up to the surface.", ObjKind.Gravestone => "Rest in peace.", ObjKind.StandingStone => "Strange runes are carved into it.",
        ObjKind.Fountain => "Water glitters in the sunlight.", ObjKind.Statue => "A statue of a great hero of Aldmoor.", ObjKind.Throne => "Fit for an undead king.",
        ObjKind.Signpost => o.Text ?? "A signpost.", ObjKind.Stall => "A market stall.", ObjKind.Well => "A deep well.",
        _ when ResourceDb.Get(o.Res) is { } r => ResourceExamine(r),
        ObjKind.Furnace => "Smelt ore into bars here.", ObjKind.Anvil => "Hammer bars into gear here. Bring a hammer.",
        ObjKind.Campfire => "Cook raw food here.", ObjKind.Fireplace => "A hearth: food burns less here than on a campfire.",
        ObjKind.SpinningWheel => "Spin cocoons and silk into thread.", ObjKind.Loom => "Weave thread into cloth, and dye it.",
        ObjKind.EnchantingTable => "Carve sigils from essence, craft jewellery and imbue it with magic.",
        ObjKind.Workbench => "Carve logs into bows, staves and arrow shafts; fletch arrows.",
        ObjKind.Altar when o.Station != null => "A dark altar, thrumming with ley-power. Sigils carved here come out doubled.",
        _ => "Nothing interesting.",
    };

    static string ResourceExamine(ResourceDef r)
    {
        var y = r.Yields;
        string what = string.Join(", ", System.Linq.Enumerable.Reverse(y).Select(x => $"{ItemDb.Get(x.item)?.Name} (lvl {x.level})"));
        string tool = r.Tool switch { ToolKind.Hatchet => "a hatchet", ToolKind.Pickaxe => "a pickaxe", ToolKind.Net => "a small net", ToolKind.Rod => "a rod and bait", ToolKind.Harpoon => "a harpoon", _ => "just your hands" };
        return $"{r.Name}: {r.Skill} gives {what}. Needs {tool}.";
    }

    // ================= input =================

    public override void _UnhandledInput(InputEvent e)
    {
        if (Map == null) return;
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                var opts = OptionsAt(mb.Position);
                if (opts.Count > 0) Execute(opts[0]);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                var opts = OptionsAt(mb.Position);
                Hud.ShowContextMenu(mb.Position, opts);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void Execute(MenuOption o)
    {
        if (o.Run == null) return;
        o.Run();
        if (o.Verb != "Inspect" && o.MarkerPos != Vector3.Zero)
        {
            fxRoot.AddChild(ClickMarker.Make(o.MarkerPos, o.Red));
            SetDestination(new Vector2(o.MarkerPos.X, o.MarkerPos.Z));
        }
    }

    // Where the player was last sent (shown as a flag on the minimap until reached).
    public Vector2? Destination { get; private set; }
    float destIdle, destAge;
    Vector3 destLastPos;

    public void SetDestination(Vector2 p) { Destination = p; destIdle = 0; destAge = 0; }

    void TickDestination(float dt)
    {
        if (Destination is not { } d || Me == null) return;
        var pos = Me.GlobalPosition;
        float dist = new Vector2(pos.X, pos.Z).DistanceTo(d);
        destAge += dt;
        destIdle = pos.DistanceTo(destLastPos) < 0.01f ? destIdle + dt : 0;
        destLastPos = pos;
        // Arrived, or stopped next to the thing we were walking to (or gave up).
        if (dist < 0.75f || (destIdle > 0.9f && destAge > 0.9f && dist < 2.5f) || destIdle > 4f || destAge > 60f) Destination = null;
    }

    public override void _Process(double delta)
    {
        EquipTuning.Poll(Time.GetTicksMsec() / 1000.0);
        if (Map == null) return;
        TickDestination((float)delta);
        var mouse = GetViewport().GetMousePosition();
        hoverTimer -= (float)delta;
        if (mouse != lastMouse || hoverTimer <= 0)
        {
            lastMouse = mouse;
            hoverTimer = 0.2f;
            if (Hud.IsPointerOverUi(mouse)) Hud.SetHover(null, 0);
            else
            {
                var opts = OptionsAt(mouse);
                Hud.SetHover(opts.Count > 0 ? opts[0] : null, opts.Count - 1);
            }
        }
    }
}
