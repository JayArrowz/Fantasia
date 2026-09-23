using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Server;

public enum Interaction { None, Attack, Talk, Trade, Bank, Take, Object, Follow, Gather, Craft, Harvest }

public abstract class Entity
{
    public int Id;
    public MapData Map;
    public Tile Pos;
    public Tile LastPos;
    public readonly Queue<Tile> Path = new();
    public int Hp;
    public Entity Target;          // combat target
    public int AttackTimer;
    public int CombatTicks;        // ticks since last combat activity
    public Entity LastAttacker;
    public bool Dead;
    public int DeathTimer;

    public abstract int MaxHp { get; }
    public abstract int CombatLevel { get; }
    public abstract string DisplayName { get; }
    public bool Alive => !Dead;

    public void SetPath(List<Tile> tiles)
    {
        Path.Clear();
        foreach (var t in tiles) Path.Enqueue(t);
    }
}

public sealed class NpcEntity : Entity
{
    public NpcDef Def;
    public Tile Home;
    public int RespawnTimer;
    public int WanderTimer;
    public int MagicCounter;

    public override int MaxHp => Def.Hitpoints;
    public override int CombatLevel => Def.CombatLevel;
    public override string DisplayName => Def.Name;
}

public sealed class PlayerEntity : Entity
{
    public long PeerId;
    public Account Acc;
    public Interaction Action;
    public int ActionTargetId = -1;     // entity id, ground uid or object id
    public string OpenShop;
    public int ShopNpcId = -1;
    public bool BankOpen;
    public Tile BankTile;
    public int EatTimer;
    public bool PrivateDirty = true;
    public bool BankDirty;
    public int MsgBudget;
    public int ChatCooldown;
    public int Spam;
    public int SaveTimer;
    public int SkullTicks;
    public int SpellReadyTick;

    // Skilling
    public string ObjVerb, ObjArg;      // option picked on the object being walked to
    public int SkillTimer;
    public string CraftStation;         // station of the open crafting window
    public int CraftObj = -1;           // station object (-1 for tool crafting such as sewing)
    public string CraftRecipe;
    public int CraftLeft, CraftMade;
    public string Activity;
    public string OfferQuest;
    public int OfferNpc = -1;
    public readonly System.Collections.Generic.Dictionary<int, PatchPhase> PatchSeen = new();

    public string Name => Acc.Name;
    public int[] Xp => Acc.Xp;
    public ItemStack[] Inv => Acc.Inv;
    public ItemStack[] Equip => Acc.Equip;

    public int Level(Skill s) => Xp_.LevelForXp(Acc.Xp[(int)s]);
    public override int MaxHp => Level(Skill.Vitality);
    public override int CombatLevel => Fantasia.Xp.CombatLevel(Level(Skill.Prowess), Level(Skill.Might), Level(Skill.Fortitude), Level(Skill.Vitality), Level(Skill.Archery), Level(Skill.Sorcery));
    public override string DisplayName => Acc.Name;

    public ItemDef Weapon => Equip[(int)EquipSlot.Weapon]?.Def;

    public int[] Bonuses()
    {
        var b = new int[9];
        foreach (var st in Equip)
        {
            var d = st?.Def;
            if (d == null) continue;
            b[0] += d.AtkMelee; b[1] += d.AtkRanged; b[2] += d.AtkMagic;
            b[3] += d.DefMelee; b[4] += d.DefRanged; b[5] += d.DefMagic;
            b[6] += d.StrMelee; b[7] += d.StrRanged; b[8] += d.MagicDmg;
        }
        return b;
    }

    // ---------- inventory helpers (server-side only) ----------
    public int FreeSlots() => Inv.Count(s => s == null);

    public int CountOf(string id)
    {
        int n = 0;
        foreach (var s in Inv) if (s != null && s.Id == id) n += s.Count;
        return n;
    }

    public bool CanAdd(string id, int count)
    {
        var d = ItemDb.Get(id);
        if (d == null) return false;
        if (d.Stackable) return Inv.Any(s => s != null && s.Id == id) || FreeSlots() > 0;
        return FreeSlots() >= count;
    }

    /// Adds as much as fits; returns amount not added.
    public int Add(string id, int count)
    {
        var d = ItemDb.Get(id);
        if (d == null || count <= 0) return count;
        PrivateDirty = true;
        if (d.Stackable)
        {
            var ex = Inv.FirstOrDefault(s => s != null && s.Id == id);
            if (ex != null) { long total = (long)ex.Count + count; ex.Count = (int)Math.Min(total, int.MaxValue); return (int)(total - ex.Count); }
            int free = Array.IndexOf(Inv, null);
            if (free < 0) return count;
            Inv[free] = new ItemStack(id, count);
            return 0;
        }
        while (count > 0)
        {
            int free = Array.IndexOf(Inv, null);
            if (free < 0) break;
            Inv[free] = new ItemStack(id, 1);
            count--;
        }
        return count;
    }

    public bool Remove(string id, int count)
    {
        if (CountOf(id) < count) return false;
        PrivateDirty = true;
        for (int i = 0; i < Inv.Length && count > 0; i++)
        {
            var s = Inv[i];
            if (s == null || s.Id != id) continue;
            int take = Math.Min(count, s.Count);
            s.Count -= take; count -= take;
            if (s.Count <= 0) Inv[i] = null;
        }
        return true;
    }
}

public sealed class GroundItem
{
    public int Uid;
    public MapData Map;
    public Tile Pos;
    public ItemStack Stack;
    public int SpawnTick;
    public int OwnerId = -1;
}

// Alias to avoid clashing with the PlayerEntity.Xp property.
static class Xp_
{
    public static int LevelForXp(int xp) => Fantasia.Xp.LevelForXp(xp);
}
