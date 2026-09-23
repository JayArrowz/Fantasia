using System;
using System.Linq;

namespace Fantasia.Server;

sealed class PendingHit
{
    public Entity Attacker, Target;
    public int Damage;
    public int Ticks;
    public CombatType Type;
    public int Style;
    public SpellDef Spell;
    public int CreatedTick;
}

public sealed partial class ServerWorld
{
    public struct AttackStats
    {
        public CombatType Type;
        public int Range;
        public int Speed;
        public SpellDef Spell;
    }

    AttackStats AttackInfo(PlayerEntity p)
    {
        var w = p.Weapon;
        var spell = p.Acc.Spell != null ? SpellDb.Get(p.Acc.Spell) : null;
        if (spell != null) return new AttackStats { Type = CombatType.Magic, Range = 10, Speed = 5, Spell = spell };
        if (w != null && w.AttackType == CombatType.Ranged)
        {
            int range = Math.Min(10, w.Range + (p.Acc.Style == 2 ? 2 : 0));
            int speed = w.Speed - (p.Acc.Style == 1 ? 1 : 0);
            return new AttackStats { Type = CombatType.Ranged, Range = range, Speed = speed };
        }
        return new AttackStats { Type = CombatType.Melee, Range = 1, Speed = w?.Speed ?? 4 };
    }

    static double HitChance(double atk, double def) =>
        atk > def ? 1 - (def + 2) / (2 * (atk + 1)) : atk / (2 * (def + 1));

    double DefenceRoll(Entity target, CombatType type)
    {
        if (target is NpcEntity n)
        {
            var d = n.Def;
            return type switch
            {
                CombatType.Melee => (d.Defence + 9) * (d.DefMelee + 64.0),
                CombatType.Ranged => (d.Defence + 9) * (d.DefRanged + 64.0),
                _ => (d.Magic + 9) * (d.DefMagic + 64.0),
            };
        }
        var p = (PlayerEntity)target;
        var b = p.Bonuses();
        bool defensiveStyle = p.Acc.Style == 2 && AttackInfo(p).Type != CombatType.Magic;
        int effDef = p.Level(Skill.Fortitude) + (defensiveStyle ? 3 : 0) + 8;
        return type switch
        {
            CombatType.Melee => effDef * (b[3] + 64.0),
            CombatType.Ranged => effDef * (b[4] + 64.0),
            _ => (Math.Floor(0.7 * p.Level(Skill.Sorcery) + 0.3 * effDef) + 8) * (b[5] + 64.0),
        };
    }

    // ================= player attacking =================

    void PlayerCombat(PlayerEntity p)
    {
        if (p.AttackTimer > 0) p.AttackTimer--;
        if (p.Action != Interaction.Attack || p.Target == null || p.Target.Dead) return;
        var t = p.Target;
        var info = AttackInfo(p);
        if (!CanAttackFrom(p.Map, p.Pos, t.Pos, info.Range, info.Type == CombatType.Melee)) return;
        if (p.AttackTimer > 0) return;
        if (t is NpcEntity tn && !tn.Def.Attackable) { ClearAction(p); return; }

        var b = p.Bonuses();
        int style = p.Acc.Style;
        double atkRoll; int maxHit; string anim; int delay; string proj = null;

        switch (info.Type)
        {
            case CombatType.Ranged:
            {
                var ammo = p.Equip[(int)EquipSlot.Ammo];
                if (ammo == null || ammo.Def.Slot != EquipSlot.Ammo || ammo.Count <= 0)
                {
                    GameMsg(p, "You have run out of arrows.");
                    ClearAction(p);
                    return;
                }
                ammo.Count--;
                if (ammo.Count <= 0) p.Equip[(int)EquipSlot.Ammo] = null;
                p.PrivateDirty = true;
                if (rng.NextDouble() < 0.5) DropItem(t.Map, t.Pos, ammo.Id, 1, p.Id);
                int eff = p.Level(Skill.Archery) + (style == 0 ? 3 : 0) + 8;
                atkRoll = eff * (b[1] + 64.0);
                maxHit = (int)Math.Floor(0.5 + eff * (b[7] + 64.0) / 640.0);
                anim = "shoot";
                delay = 1 + (p.Pos.Chebyshev(t.Pos) + 3) / 6;
                proj = "arrow:" + ammo.Def.Tint.ToHtml(false);
                break;
            }
            case CombatType.Magic:
            {
                var sp = info.Spell;
                if (p.Level(Skill.Sorcery) < sp.Level) { GameMsg(p, $"You need a Magic level of {sp.Level} to cast {sp.Name}."); ClearAction(p); return; }
                string staffRune = p.Weapon?.ProvidesRune;
                foreach (var (rune, count) in sp.Runes)
                    if (rune != staffRune && p.CountOf(rune) < count)
                    {
                        GameMsg(p, $"You lack the sigils to cast {sp.Name}.");
                        ClearAction(p);
                        return;
                    }
                foreach (var (rune, count) in sp.Runes)
                    if (rune != staffRune) p.Remove(rune, count);
                int eff = p.Level(Skill.Sorcery) + 8;
                atkRoll = eff * (b[2] + 64.0);
                maxHit = (int)Math.Floor(sp.MaxHit * (100 + b[8]) / 100.0);
                anim = "cast";
                delay = 1 + (p.Pos.Chebyshev(t.Pos) + 1) / 3;
                proj = "magic:" + sp.Color.TrimStart('#');
                break;
            }
            default:
            {
                int effAtk = p.Level(Skill.Prowess) + (style == 0 ? 3 : 0) + 8;
                int effStr = p.Level(Skill.Might) + (style == 1 ? 3 : 0) + 8;
                atkRoll = effAtk * (b[0] + 64.0);
                maxHit = (int)Math.Floor(0.5 + effStr * (b[6] + 64.0) / 640.0);
                var wk = p.Weapon?.Weapon ?? WeaponKind.Unarmed;
                anim = wk switch
                {
                    WeaponKind.Warhammer or WeaponKind.Greatsword => "heavy",
                    WeaponKind.Dagger or WeaponKind.Spear or WeaponKind.Unarmed => "stab",
                    _ => "slash",
                };
                delay = 1;
                break;
            }
        }

        p.AttackTimer = info.Speed;
        p.CombatTicks = 0;
        Emit(p.Map, new Fx { T = "anim", A = p.Id, B = t.Id, S = anim });
        if (proj != null) Emit(p.Map, new Fx { T = "proj", A = p.Id, B = t.Id, C = delay, S = proj });

        bool hit = rng.NextDouble() < HitChance(atkRoll, DefenceRoll(t, info.Type));
        int dmg = hit ? rng.Next(0, maxHit + 1) : 0;
        pending.Add(new PendingHit { Attacker = p, Target = t, Damage = dmg, Ticks = delay, Type = info.Type, Style = style, Spell = info.Spell, CreatedTick = tick });

        // Provoke NPCs immediately so they turn to fight.
        if (t is NpcEntity npc && npc.Target == null) { npc.Target = p; npc.CombatTicks = 0; }
    }

    // ================= npc attacking =================

    void NpcCombat(NpcEntity n)
    {
        if (n.Target == null || n.Target.Dead || n.AttackTimer > 0) return;
        var t = n.Target;
        var (range, melee) = NpcRange(n);
        if (!CanAttackFrom(n.Map, n.Pos, t.Pos, range, melee)) return;

        var d = n.Def;
        CombatType type = d.Style;
        // Mixed bosses melee up close and cast at range.
        if (d.Boss && d.Magic > 1 && d.Style == CombatType.Melee)
            type = n.Pos.Chebyshev(t.Pos) <= 1 && (n.Pos.X == t.Pos.X || n.Pos.Z == t.Pos.Z) ? CombatType.Melee : CombatType.Magic;

        double atkRoll = type switch
        {
            CombatType.Ranged => (d.Ranged + 9) * 64.0,
            CombatType.Magic => (d.Magic + 9) * 64.0,
            _ => (d.Attack + 9) * 64.0,
        };
        int maxHit = d.MaxHit;
        int delay = 1;
        string anim = "slash";
        if (type == CombatType.Ranged)
        {
            anim = "shoot";
            delay = 1 + (n.Pos.Chebyshev(t.Pos) + 3) / 6;
            Emit(n.Map, new Fx { T = "proj", A = n.Id, B = t.Id, C = delay, S = "arrow:a0a0a0" });
        }
        else if (type == CombatType.Magic)
        {
            anim = "cast";
            delay = 1 + (n.Pos.Chebyshev(t.Pos) + 1) / 3;
            Emit(n.Map, new Fx { T = "proj", A = n.Id, B = t.Id, C = delay, S = "magic:" + d.ProjectileColor.TrimStart('#') });
        }
        else if (d.Body != NpcBody.Humanoid) anim = "bite";

        n.AttackTimer = d.AttackSpeed;
        n.CombatTicks = 0;
        Emit(n.Map, new Fx { T = "anim", A = n.Id, B = t.Id, S = anim });
        bool hit = rng.NextDouble() < HitChance(atkRoll, DefenceRoll(t, type));
        int dmg = hit ? rng.Next(0, maxHit + 1) : 0;
        pending.Add(new PendingHit { Attacker = n, Target = t, Damage = dmg, Ticks = delay, Type = type, CreatedTick = tick });
    }

    // ================= hit resolution =================

    void ProcessHits()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var h = pending[i];
            // Count down from the tick after the attack so a delay of N lands exactly N ticks later,
            // which is when the client's projectile (or swing) visually connects.
            if (h.CreatedTick == tick || --h.Ticks > 0) continue;
            pending.RemoveAt(i);
            var t = h.Target;
            if (t.Dead || !entities.ContainsKey(t.Id) || t.Map != h.Attacker.Map) continue;
            int dmg = Math.Min(h.Damage, t.Hp);
            t.Hp -= dmg;
            t.LastAttacker = h.Attacker;
            t.CombatTicks = 0;
            Emit(t.Map, new Fx { T = "hit", A = t.Id, B = dmg, C = t.Hp, S = h.Type.ToString() });
            if (t is PlayerEntity tp) tp.PrivateDirty = true;

            if (h.Attacker is PlayerEntity ap && entities.ContainsKey(ap.Id)) GrantCombatXp(ap, h, dmg);

            // Retaliation
            if (t is NpcEntity tn)
            {
                if (tn.Target == null || tn.Target.Dead) tn.Target = h.Attacker;
            }
            else if (t is PlayerEntity p && p.Acc.Retal && p.Action == Interaction.None && p.Path.Count == 0 && !h.Attacker.Dead)
            {
                if (h.Attacker is NpcEntity || (h.Attacker is PlayerEntity && p.Map.IsPvp(p.Pos)))
                {
                    p.Action = Interaction.Attack;
                    p.ActionTargetId = h.Attacker.Id;
                    p.Target = h.Attacker;
                }
            }

            if (t.Hp <= 0) Kill(t, h.Attacker);
        }
    }

    void GrantCombatXp(PlayerEntity p, PendingHit h, int dmg)
    {
        switch (h.Type)
        {
            case CombatType.Magic:
                GrantXp(p, Skill.Sorcery, (int)Math.Round((h.Spell?.BaseXp ?? 0) + dmg * 2));
                break;
            case CombatType.Ranged:
                if (h.Style == 2) { GrantXp(p, Skill.Archery, dmg * 2); GrantXp(p, Skill.Fortitude, dmg * 2); }
                else GrantXp(p, Skill.Archery, dmg * 4);
                break;
            default:
                var sk = h.Style switch { 0 => Skill.Prowess, 1 => Skill.Might, _ => Skill.Fortitude };
                GrantXp(p, sk, dmg * 4);
                break;
        }
        if (dmg > 0) GrantXp(p, Skill.Vitality, (int)Math.Round(dmg * 1.33));
    }

    /// Non-combat spells: teleports and heals. Validated and paid for on the server.
    void CastUtility(PlayerEntity p, SpellDef sp)
    {
        if (p.Level(Skill.Sorcery) < sp.Level) { GameMsg(p, $"You need level {sp.Level} Sorcery to cast {sp.Name}."); return; }
        if (tick < p.SpellReadyTick) { GameMsg(p, $"{sp.Name} is not ready yet ({(p.SpellReadyTick - tick) * GameConst.TickSeconds:0}s)."); return; }
        if (sp.Kind == SpellKind.Teleport && p.Map.IsPvp(p.Pos) && p.CombatTicks < 16) { GameMsg(p, "The Bloodmarch's wards block your recall while you are in combat."); return; }
        if (sp.Kind == SpellKind.Heal && p.Hp >= p.MaxHp) { GameMsg(p, "You are already at full vitality."); return; }
        string staffRune = p.Weapon?.ProvidesRune;
        foreach (var (rune, count) in sp.Runes)
            if (rune != staffRune && p.CountOf(rune) < count) { GameMsg(p, $"You lack the sigils to cast {sp.Name}."); return; }
        foreach (var (rune, count) in sp.Runes)
            if (rune != staffRune) p.Remove(rune, count);
        p.SpellReadyTick = tick + sp.CooldownTicks;
        Emit(p.Map, new Fx { T = "anim", A = p.Id, S = "cast" });
        GrantXp(p, Skill.Sorcery, (int)sp.BaseXp);
        if (sp.Kind == SpellKind.Teleport)
        {
            var map = MapGenerator.Get(sp.TeleportMap);
            Teleport(p, map, NearestWalkable(map, new Tile(sp.TeleportX, sp.TeleportZ)));
            GameMsg(p, $"The {sp.Name} gate carries you away.");
        }
        else
        {
            int heal = Math.Max(1, p.MaxHp * sp.HealPercent / 100);
            p.Hp = Math.Min(p.MaxHp, p.Hp + heal);
            p.PrivateDirty = true;
            Emit(p.Map, new Fx { T = "heal", A = p.Id, B = heal });
            GameMsg(p, "Warm light knits your wounds.");
        }
    }

    void GrantXp(PlayerEntity p, Skill s, int amount)
    {
        if (amount <= 0) return;
        int before = p.Level(s);
        long nx = (long)p.Xp[(int)s] + amount;
        p.Xp[(int)s] = (int)Math.Min(nx, 200_000_000);
        p.PrivateDirty = true;
        int after = p.Level(s);
        if (after > before)
        {
            if (s == Skill.Vitality) p.Hp += after - before;
            GameMsg(p, $"Your {s} has risen to level {after}!");
            Emit(p.Map, new Fx { T = "lvl", A = p.Id, B = after, S = s.ToString() });
        }
    }
}
