using System;
using System.Linq;

namespace Fantasia.Server;

public sealed partial class ServerWorld
{
    bool ValidSlot(PlayerEntity p, int slot) => slot >= 0 && slot < p.Inv.Length && p.Inv[slot] != null;

    void OnInvOption(PlayerEntity p, int slot, string opt)
    {
        if (!ValidSlot(p, slot)) return;
        var st = p.Inv[slot];
        var d = st.Def;
        switch (opt)
        {
            case "Equip":
                if (d.Equippable) Equip(p, slot);
                break;
            case "Eat":
                if (d.IsFood) Eat(p, slot);
                break;
            case "Read":
                if (d.Teaches != null) ReadScroll(p, slot);
                break;
            case "Open":
                if (d.Use == "Open") OpenContainer(p, slot);
                break;
            case "Sew":
                if (d.Tool == ToolKind.Needle) { CloseUi(p); OpenStation(p, null, "sew"); }
                break;
            case "Drop":
                p.Inv[slot] = null;
                p.PrivateDirty = true;
                DropItem(p.Map, p.Pos, st.Id, st.Count, p.Id);
                break;
        }
    }

    void Eat(PlayerEntity p, int slot)
    {
        if (p.EatTimer > 0) return;
        var d = p.Inv[slot].Def;
        p.Inv[slot] = null;
        int before = p.Hp;
        p.Hp = Math.Min(p.MaxHp, p.Hp + d.Heal);
        p.EatTimer = 3;
        if (p.Target != null) p.AttackTimer = Math.Max(p.AttackTimer, 2);
        p.PrivateDirty = true;
        GameMsg(p, p.Hp > before ? $"You eat the {d.Name.ToLowerInvariant()}. It heals some health." : $"You eat the {d.Name.ToLowerInvariant()}.");
        Emit(p.Map, new Fx { T = "anim", A = p.Id, S = "eat" });
    }

    void Equip(PlayerEntity p, int slot)
    {
        var st = p.Inv[slot];
        var d = st.Def;
        if (p.Level(d.ReqSkill) < d.ReqLevel)
        {
            GameMsg(p, $"You need level {d.ReqLevel} {d.ReqSkill} to equip this.");
            return;
        }
        int es = (int)d.Slot;
        if (d.Stackable && p.Equip[es]?.Id == d.Id)
        {
            p.Equip[es].Count += st.Count;
            p.Inv[slot] = null;
            p.PrivateDirty = true;
            return;
        }
        var clear = new System.Collections.Generic.List<int> { es };
        if (d.TwoHanded) clear.Add((int)EquipSlot.Shield);
        if (d.Slot == EquipSlot.Shield && p.Weapon is { TwoHanded: true }) clear.Add((int)EquipSlot.Weapon);
        var removing = clear.Where(s => p.Equip[s] != null).ToList();
        if (removing.Count > p.FreeSlots() + 1)
        {
            GameMsg(p, "You don't have enough free inventory space to do that.");
            return;
        }
        p.Inv[slot] = null;
        foreach (var s in removing)
        {
            var old = p.Equip[s];
            p.Equip[s] = null;
            int target = p.Inv[slot] == null ? slot : Array.IndexOf(p.Inv, null);
            p.Inv[target] = old;
        }
        p.Equip[es] = st;
        p.PrivateDirty = true;
        // Changing weapon type resets an autocast spell only when switching away from a staff.
        if (d.Slot == EquipSlot.Weapon && d.Weapon != WeaponKind.Staff && p.Acc.Spell != null) p.Acc.Spell = null;
    }

    void Unequip(PlayerEntity p, int es)
    {
        if (es < 0 || es >= p.Equip.Length || p.Equip[es] == null) return;
        var st = p.Equip[es];
        if (!p.CanAdd(st.Id, st.Count)) { GameMsg(p, "You don't have enough free inventory space to do that."); return; }
        p.Equip[es] = null;
        p.Add(st.Id, st.Count);
        p.PrivateDirty = true;
    }

    // ================= shops =================

    bool ShopValid(PlayerEntity p)
    {
        if (p.OpenShop == null) return false;
        if (!entities.TryGetValue(p.ShopNpcId, out var n) || n.Map != p.Map || n.Pos.Chebyshev(p.Pos) > 4)
        {
            CloseUi(p);
            return false;
        }
        return true;
    }

    public static int BuyPrice(ItemDef d) => Math.Max(1, d.Value);
    public static int SellPrice(ItemDef d) => Math.Max(0, d.Value * 4 / 10);

    void Buy(PlayerEntity p, string itemId, int qty)
    {
        if (!ShopValid(p)) return;
        var shop = ShopDb.All[p.OpenShop];
        if (!shop.Stock.Contains(itemId)) return;
        var d = ItemDb.Get(itemId);
        qty = Math.Clamp(qty, 1, 1000);
        int price = BuyPrice(d);
        int coins = p.CountOf("coins");
        qty = Math.Min(qty, coins / price);
        if (qty <= 0) { GameMsg(p, "You don't have enough crowns."); return; }
        if (!d.Stackable) qty = Math.Min(qty, p.FreeSlots());
        else if (!p.CanAdd(itemId, qty)) qty = 0;
        if (qty <= 0) { GameMsg(p, "You don't have enough inventory space."); return; }
        p.Remove("coins", qty * price);
        p.Add(itemId, qty);
    }

    void Sell(PlayerEntity p, int slot, int qty)
    {
        if (!ShopValid(p) || !ValidSlot(p, slot)) return;
        var st = p.Inv[slot];
        if (st.Id == "coins") return;
        var d = st.Def;
        qty = Math.Clamp(qty, 1, int.MaxValue);
        qty = Math.Min(qty, p.CountOf(st.Id));
        long total = (long)SellPrice(d) * qty;
        if (total > 0 && !p.CanAdd("coins", 1) && p.CountOf(st.Id) > qty) { GameMsg(p, "You don't have enough inventory space."); return; }
        p.Remove(st.Id, qty);
        if (total > 0) p.Add("coins", (int)Math.Min(total, int.MaxValue));
        GameMsg(p, $"You sell {qty} x {d.Name} for {total} crowns.");
    }

    // ================= bank =================

    bool BankValid(PlayerEntity p)
    {
        if (!p.BankOpen) return false;
        if (p.Pos.Chebyshev(p.BankTile) > 2) { CloseUi(p); return false; }
        return true;
    }

    void BankAdd(PlayerEntity p, string id, int count)
    {
        var ex = p.Acc.Bank.FirstOrDefault(s => s.Id == id);
        if (ex != null) ex.Count = (int)Math.Min((long)ex.Count + count, int.MaxValue);
        else p.Acc.Bank.Add(new ItemStack(id, count));
    }

    void Deposit(PlayerEntity p, int slot, int qty)
    {
        if (!BankValid(p) || !ValidSlot(p, slot)) return;
        var id = p.Inv[slot].Id;
        if (p.Acc.Bank.Count >= 400 && p.Acc.Bank.All(s => s.Id != id)) { GameMsg(p, "Your bank is full."); return; }
        qty = Math.Min(Math.Max(qty, 1), p.CountOf(id));
        p.Remove(id, qty);
        BankAdd(p, id, qty);
        p.PrivateDirty = true;
    }

    void DepositAll(PlayerEntity p)
    {
        if (!BankValid(p)) return;
        for (int i = 0; i < p.Inv.Length; i++)
        {
            var st = p.Inv[i];
            if (st == null) continue;
            if (p.Acc.Bank.Count >= 400 && p.Acc.Bank.All(s => s.Id != st.Id)) continue;
            BankAdd(p, st.Id, st.Count);
            p.Inv[i] = null;
        }
        p.PrivateDirty = true;
    }

    void Withdraw(PlayerEntity p, int index, int qty)
    {
        if (!BankValid(p) || index < 0 || index >= p.Acc.Bank.Count) return;
        var st = p.Acc.Bank[index];
        var d = st.Def;
        qty = Math.Min(Math.Max(qty, 1), st.Count);
        if (!d.Stackable) qty = Math.Min(qty, p.FreeSlots());
        else if (!p.CanAdd(st.Id, qty)) qty = 0;
        if (qty <= 0) { GameMsg(p, "You don't have enough inventory space."); return; }
        p.Add(st.Id, qty);
        st.Count -= qty;
        if (st.Count <= 0) p.Acc.Bank.RemoveAt(index);
        p.PrivateDirty = true;
    }
}
