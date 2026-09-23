using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

public partial class BankWindow : GameWindow
{
    Hud hud;
    GridContainer grid;
    Label info;

    public void Init(Hud h) => hud = h;

    public override void _Ready()
    {
        base._Ready();
        grid = GetNode<GridContainer>("%Grid");
        info = GetNode<Label>("%Info");
        GetNode<Button>("%Deposit").Pressed += () => Net.I.Send(new ClientMsg { T = C2S.DepositAll });
    }

    public void Open() { Visible = true; }

    public void Refresh(PrivateState st)
    {
        if (st?.Bank == null) return;
        foreach (var c in grid.GetChildren()) c.QueueFree();
        for (int i = 0; i < st.Bank.Count; i++)
        {
            int idx = i;
            var stack = st.Bank[i];
            var d = ItemDb.Get(stack.Id);
            if (d == null) continue;
            var s = new ItemSlot { Stack = stack, Index = i, CustomMinimumSize = new Vector2(48, 44), TooltipText = " " };
            s.LeftClick = sl => Net.I.Send(new ClientMsg { T = C2S.Withdraw, A = idx, B = 1 });
            s.RightClick = (sl, pos) =>
            {
                var col = new Color(1f, 0.6f, 0.25f);
                var opts = new List<MenuOption>();
                foreach (var (label, n) in new[] { ("Withdraw-1", 1), ("Withdraw-5", 5), ("Withdraw-10", 10), ("Withdraw-All", int.MaxValue) })
                    opts.Add(new MenuOption { Verb = label, Target = d.Name, TargetColor = col, Run = () => Net.I.Send(new ClientMsg { T = C2S.Withdraw, A = idx, B = n }) });
                opts.Add(new MenuOption { Verb = "Inspect", Target = d.Name, TargetColor = col, Run = () => hud.GameMessage($"{d.Examine} ({stack.Count:N0})") });
                hud.ShowMenu(pos, opts);
            };
            grid.AddChild(s);
        }
        info.Text = $"{st.Bank.Count} / 400 item types stored";
    }
}
