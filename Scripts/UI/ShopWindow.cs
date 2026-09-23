using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

public partial class ShopWindow : GameWindow
{
    Hud hud;
    GridContainer grid;
    Label coins;
    string shopId;

    public void Init(Hud h) => hud = h;

    public override void _Ready()
    {
        base._Ready();
        grid = GetNode<GridContainer>("%Grid");
        coins = GetNode<Label>("%Coins");
    }

    public void Open(string id)
    {
        shopId = id;
        var shop = ShopDb.All[id];
        TitleLabel.Text = shop.Name;
        foreach (var c in grid.GetChildren()) c.QueueFree();
        foreach (var itemId in shop.Stock)
        {
            var d = ItemDb.Get(itemId);
            if (d == null) continue;
            var s = new ItemSlot { Stack = new ItemStack(itemId, 1), Caption = Short(Server.ServerWorld.BuyPrice(d)), CustomMinimumSize = new Vector2(46, 50), TooltipText = " " };
            s.LeftClick = sl => Buy(itemId, 1);
            s.RightClick = (sl, pos) =>
            {
                var col = new Color(1f, 0.6f, 0.25f);
                var opts = new List<MenuOption> { new() { Verb = "Value", Target = d.Name, TargetColor = col, Run = () => hud.GameMessage($"{d.Name}: costs {Server.ServerWorld.BuyPrice(d):N0} crowns.") } };
                foreach (var n in new[] { 1, 5, 10, 50 })
                    opts.Add(new MenuOption { Verb = $"Buy {n}", Target = d.Name, TargetColor = col, Run = () => Buy(itemId, n) });
                if (d.Stackable) opts.Add(new MenuOption { Verb = "Buy 500", Target = d.Name, TargetColor = col, Run = () => Buy(itemId, 500) });
                hud.ShowMenu(pos, opts);
            };
            grid.AddChild(s);
        }
        Visible = true;
        Refresh();
    }

    static string Short(int v) => v >= 10000 ? $"{v / 1000}k" : v.ToString();

    void Buy(string id, int n) => Net.I.Send(new ClientMsg { T = C2S.Buy, S = id, A = n });

    public void Refresh()
    {
        var st = GameWorld.I?.State;
        int c = st?.Inv.Where(s => s?.Id == "coins").Sum(s => s.Count) ?? 0;
        coins.Text = $"Your crowns: {c:N0}";
    }
}
