using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fantasia;

/// Client -> server intent. Clients never send positions or results, only requests
/// the server validates (walk here, attack that, equip slot N...).
public sealed class ClientMsg
{
    public string T;
    public int A, B, C;
    public string S, S2;
}

public static class C2S
{
    public const string Login = "login";        // S=name S2=password
    public const string Walk = "walk";          // A=x B=z
    public const string Npc = "npc";            // A=entity id S=option
    public const string Player = "player";      // A=entity id S=option
    public const string Ground = "ground";      // A=ground uid S=option
    public const string Object = "obj";         // A=object id S=option S2=argument (seed id)
    public const string Inv = "inv";            // A=slot S=option
    public const string Unequip = "unequip";    // A=equip slot
    public const string Swap = "swap";          // A=from B=to
    public const string Style = "style";        // A=index
    public const string Spell = "spell";        // S=spell id or ""
    public const string Run = "run";            // A=0/1
    public const string Retaliate = "retal";    // A=0/1
    public const string Chat = "chat";          // S=text
    public const string Buy = "buy";            // S=item A=qty
    public const string Sell = "sell";          // A=slot B=qty
    public const string Deposit = "dep";        // A=slot B=qty
    public const string DepositAll = "depall";
    public const string Withdraw = "wd";        // A=bank index B=qty
    public const string CloseUi = "close";
    public const string Examine = "examine";    // S=kind A=id
    public const string Craft = "craft";        // S=recipe id A=count
    public const string Quest = "quest";        // S=quest id A=0 accept / 1 decline
}

public sealed class ItemStack
{
    public string Id;
    public int Count;
    public ItemStack() { }
    public ItemStack(string id, int count) { Id = id; Count = count; }
    [JsonIgnore] public ItemDef Def => ItemDb.Get(Id);
    public ItemStack Clone() => new(Id, Count);
}

public sealed class EntitySnap
{
    public int Id;
    public byte K;          // 0 player, 1 npc
    public string D;        // npc def id or player name
    public int X, Z;
    public int Hp, Max;
    public int Tgt = -1;
    public int Cb;
    public bool Dead;
    public string[] Eq;     // player appearance: item id per EquipSlot (null if empty)
}

public sealed class GroundSnap
{
    public int U;
    public string I;
    public int N;
    public int X, Z;
}

public sealed class Fx
{
    public string T;        // hit, proj, anim, death, lvl, say, tele
    public int A, B, C;
    public string S;
}

public sealed class Snapshot
{
    public int Tick;
    public string Map;
    public int You;
    public bool Pvp;
    public List<EntitySnap> E = new();
    public List<GroundSnap> G = new();
    public List<Fx> Fx = new();
    public int[] Dep;       // depleted / inactive resource object ids in view
    public int[] Frz;       // frenzied fishing spots in view
}

public sealed class PrivateState
{
    public int[] Xp;
    public int Hp;
    public ItemStack[] Inv;
    public ItemStack[] Eq;
    public int Style;
    public string Spell;
    public bool Run;
    public bool Retal;
    public int[] Bonus;   // atkMelee, atkRanged, atkMagic, defMelee, defRanged, defMagic, strMelee, strRanged, magicDmg
    public List<ItemStack> Bank;   // only sent while bank is open
    public string[] Unlocks;       // learnt recipe unlock keys
    public Dictionary<string, int> Quests;
    public Dictionary<string, int> QVars;
    public List<PatchState> Patches;
    public long Now;               // server unix time, for crop growth display
    public string Activity;        // what the player is busy doing, e.g. "Chopping an ash tree"
}

public sealed class ServerMsg
{
    public string T;      // game, chat, shop, bank, close, dialog, login_ok, login_fail
    public string S, S2;
    public int A, B;
    public string[] Lines;
    public List<ItemStack> Items;
    public string[] Opts;  // dialog choices
}
