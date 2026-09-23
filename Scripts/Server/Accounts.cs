using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Godot;

namespace Fantasia.Server;

public sealed class Account
{
    public string Name;
    public string Salt;
    public string Hash;
    public string Map = GameConst.OverworldId;
    public int X = -1, Z = -1;
    public int[] Xp = new int[Skills.Count];
    public ItemStack[] Inv = new ItemStack[GameConst.InventorySize];
    public ItemStack[] Equip = new ItemStack[11];
    public List<ItemStack> Bank = new();
    public int Style;
    public string Spell;
    public bool Run = true;
    public bool Retal = true;
    public int Hp = 10;
    public List<string> Unlocks = new();
    public Dictionary<string, int> Quests = new();
    public Dictionary<string, int> QVars = new();
    public List<PatchState> Patches = new();
}

/// Server-side account storage. Passwords are salted + PBKDF2 hashed; files live in user://saves.
public static class Accounts
{
    static string Dir => ProjectSettings.GlobalizePath("user://saves");
    static readonly Regex NameRx = new("^[A-Za-z0-9 _]{3,12}$");

    public static bool ValidName(string name) => name != null && NameRx.IsMatch(name) && name.Trim() == name;

    static string PathFor(string name) => System.IO.Path.Combine(Dir, name.ToLowerInvariant().Replace(' ', '_') + ".json");

    public static Account LoadOrCreate(string name, string password, out string error)
    {
        error = null;
        if (!ValidName(name)) { error = "Names must be 3-12 letters, numbers or spaces."; return null; }
        if (string.IsNullOrEmpty(password) || password.Length < 3 || password.Length > 64) { error = "Password must be 3-64 characters."; return null; }
        Directory.CreateDirectory(Dir);
        var path = PathFor(name);
        if (File.Exists(path))
        {
            var acc = Json.Read<Account>(File.ReadAllText(path));
            if (acc == null) { error = "Account file is corrupt."; return null; }
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(acc.Hash), HashPw(password, Convert.FromBase64String(acc.Salt))))
            { error = "Invalid username or password."; return null; }
            Sanitize(acc);
            return acc;
        }
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var a = new Account { Name = name, Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(HashPw(password, salt)) };
        a.Xp[(int)Skill.Vitality] = Xp.XpForLevel(10);
        a.Hp = 10;
        StarterKit(a);
        Save(a);
        return a;
    }

    static byte[] HashPw(string pw, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pw), salt, 100_000, HashAlgorithmName.SHA256, 32);

    static void StarterKit(Account a)
    {
        a.Equip[(int)EquipSlot.Weapon] = new ItemStack("bronze_sword", 1);
        a.Equip[(int)EquipSlot.Shield] = new ItemStack("wooden_shield", 1);
        a.Equip[(int)EquipSlot.Body] = new ItemStack("leather_jerkin", 1);
        a.Equip[(int)EquipSlot.Feet] = new ItemStack("leather_boots", 1);
        var inv = new (string, int)[]
        {
            ("coins", 250), ("shortbow", 1), ("bronze_arrow", 150), ("gale_staff", 1), ("focus_sigil", 200),
            ("tide_sigil", 60), ("stone_sigil", 60), ("bread", 1), ("bread", 1), ("bread", 1), ("roast_perch", 1), ("roast_perch", 1),
        };
        int i = 0;
        foreach (var (id, n) in inv) a.Inv[i++] = new ItemStack(id, n);
    }

    static void Sanitize(Account a)
    {
        if (a.Xp == null) a.Xp = new int[Skills.Count];
        else if (a.Xp.Length < Skills.Count) Array.Resize(ref a.Xp, Skills.Count);
        a.Unlocks ??= new(); a.Quests ??= new(); a.QVars ??= new(); a.Patches ??= new();
        a.Unlocks.RemoveAll(k => RecipeDb.GetUnlock(k) == null);
        a.Patches.RemoveAll(pt => pt == null);
        if (a.Inv == null || a.Inv.Length != GameConst.InventorySize) Array.Resize(ref a.Inv, GameConst.InventorySize);
        if (a.Equip == null || a.Equip.Length != 11) Array.Resize(ref a.Equip, 11);
        a.Bank ??= new();
        foreach (var st in a.Inv) if (st != null) st.Id = IdMigration.Item(st.Id);
        foreach (var st in a.Equip) if (st != null) st.Id = IdMigration.Item(st.Id);
        foreach (var st in a.Bank) if (st != null) st.Id = IdMigration.Item(st.Id);
        if (a.Spell != null && IdMigration.Spells.TryGetValue(a.Spell, out var ns)) a.Spell = ns;
        if (SpellDb.Get(a.Spell) is not { Kind: SpellKind.Combat }) a.Spell = null;
        a.Bank.RemoveAll(s => s == null || ItemDb.Get(s.Id) == null || s.Count <= 0);
        for (int i = 0; i < a.Inv.Length; i++) if (a.Inv[i] != null && (ItemDb.Get(a.Inv[i].Id) == null || a.Inv[i].Count <= 0)) a.Inv[i] = null;
        for (int i = 0; i < a.Equip.Length; i++) if (a.Equip[i] != null && ItemDb.Get(a.Equip[i].Id) == null) a.Equip[i] = null;
        if (a.Xp[(int)Skill.Vitality] < Xp.XpForLevel(10)) a.Xp[(int)Skill.Vitality] = Xp.XpForLevel(10);
    }

    public static void Save(Account a)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = PathFor(a.Name) + ".tmp";
            File.WriteAllText(tmp, Json.Write(a));
            File.Move(tmp, PathFor(a.Name), true);
        }
        catch (Exception e) { GD.PrintErr($"Failed to save {a.Name}: {e.Message}"); }
    }
}
