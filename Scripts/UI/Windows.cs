using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

public abstract partial class GameWindow : PanelContainer
{
    protected VBoxContainer Body;
    protected Label TitleLabel;

    protected GameWindow(string title, Vector2 size)
    {
        Visible = false;
        CustomMinimumSize = size;
        UiTheme.Place(this, LayoutPreset.Center, -size / 2 + new Vector2(-120, -60), size);
        MouseFilter = MouseFilterEnum.Stop;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        AddChild(v);
        var top = new HBoxContainer();
        v.AddChild(top);
        TitleLabel = UiTheme.Title(title, 20);
        TitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(TitleLabel);
        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(30, 30), FocusMode = FocusModeEnum.None };
        close.Pressed += OnClose;
        top.AddChild(close);
        Body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 6);
        v.AddChild(Body);
    }

    protected virtual void OnClose() => GameWorld.I?.Hud.CloseWindows(true);
}

public partial class ShopWindow : GameWindow
{
    readonly Hud hud;
    readonly GridContainer grid;
    readonly Label coins;
    string shopId;

    public ShopWindow(Hud h) : base("Shop", new Vector2(460, 420))
    {
        hud = h;
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        grid = new GridContainer { Columns = 8 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(grid);
        Body.AddChild(scroll);
        coins = UiTheme.Lbl("", 14, UiTheme.Gold);
        Body.AddChild(coins);
        Body.AddChild(UiTheme.Lbl("Left-click to buy 1, right-click for more. Click items in your pack to sell.", 12, UiTheme.Dim));
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

public partial class BankWindow : GameWindow
{
    readonly Hud hud;
    readonly GridContainer grid;
    readonly Label info;

    public BankWindow(Hud h) : base("Aldmoor Treasury", new Vector2(480, 460))
    {
        hud = h;
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        grid = new GridContainer { Columns = 8 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);
        Body.AddChild(scroll);
        var row = new HBoxContainer();
        Body.AddChild(row);
        info = UiTheme.Lbl("", 13, UiTheme.Dim);
        info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(info);
        var dep = new Button { Text = "Deposit inventory", FocusMode = FocusModeEnum.None };
        dep.Pressed += () => Net.I.Send(new ClientMsg { T = C2S.DepositAll });
        row.AddChild(dep);
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

public partial class DialogWindow : PanelContainer
{
    readonly Label name;
    readonly Label text;
    readonly Button next;
    readonly HBoxContainer choices;
    string[] lines = Array.Empty<string>();
    string[] opts;
    string questId;
    int index;

    public DialogWindow()
    {
        Visible = false;
        AddThemeStyleboxOverride("panel", UiTheme.ParchmentStyle());
        UiTheme.Place(this, LayoutPreset.BottomLeft, new Vector2(8, -330), new Vector2(520, 124));
        CustomMinimumSize = new Vector2(520, 124);
        var v = new VBoxContainer();
        AddChild(v);
        name = UiTheme.Title("", 18);
        name.AddThemeColorOverride("font_color", new Color(0.45f, 0.1f, 0.05f));
        name.AddThemeConstantOverride("outline_size", 0);
        v.AddChild(name);
        text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        text.AddThemeColorOverride("font_color", new Color(0.12f, 0.08f, 0.04f));
        text.AddThemeConstantOverride("outline_size", 0);
        text.AddThemeFontSizeOverride("font_size", 16);
        v.AddChild(text);
        next = new Button { Text = "Continue ▸", Flat = true, FocusMode = FocusModeEnum.None };
        next.AddThemeColorOverride("font_color", new Color(0.1f, 0.2f, 0.6f));
        next.AddThemeConstantOverride("outline_size", 0);
        next.Pressed += Next;
        v.AddChild(next);
        choices = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
        choices.AddThemeConstantOverride("separation", 16);
        v.AddChild(choices);
    }

    public void Open(string who, string[] l, string[] options = null, string quest = null)
    {
        lines = l ?? Array.Empty<string>();
        opts = options;
        questId = quest;
        index = 0;
        name.Text = who;
        Visible = lines.Length > 0;
        Show();
        if (lines.Length > 0) text.Text = lines[0];
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool last = index >= lines.Length - 1;
        bool choose = last && opts is { Length: > 0 };
        next.Visible = !choose;
        choices.Visible = choose;
        foreach (var c in choices.GetChildren()) c.QueueFree();
        if (!choose) return;
        for (int i = 0; i < opts.Length; i++)
        {
            int idx = i;
            var b = new Button { Text = opts[i], FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 30) };
            b.AddThemeColorOverride("font_color", i == 0 ? new Color(0.1f, 0.4f, 0.1f) : new Color(0.4f, 0.1f, 0.05f));
            b.Pressed += () =>
            {
                Visible = false;
                if (questId != null) Net.I.Send(new ClientMsg { T = C2S.Quest, S = questId, A = idx });
            };
            choices.AddChild(b);
        }
    }

    void Next()
    {
        index++;
        if (index >= lines.Length) { Visible = false; return; }
        text.Text = lines[index];
        UpdateButtons();
    }
}

public partial class WorldMapWindow : PanelContainer
{
    MapData map;
    ImageTexture tex;
    readonly MapCanvas canvas;

    public WorldMapWindow()
    {
        Visible = false;
        UiTheme.Place(this, LayoutPreset.Center, new Vector2(-380, -380), new Vector2(760, 760));
        MouseFilter = MouseFilterEnum.Stop;
        var v = new VBoxContainer();
        AddChild(v);
        var top = new HBoxContainer();
        v.AddChild(top);
        var title = UiTheme.Title("World Map", 22);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(title);
        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(30, 30), FocusMode = FocusModeEnum.None };
        close.Pressed += () => Visible = false;
        top.AddChild(close);
        canvas = new MapCanvas { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddChild(canvas);
    }

    public void SetMap(MapData m)
    {
        map = m;
        tex = MapImage.Build(m);
        canvas.Map = m;
        canvas.Tex = tex;
    }

    partial class MapCanvas : Control
    {
        public MapData Map;
        public ImageTexture Tex;
        public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
        public override void _Draw()
        {
            if (Map == null || Tex == null) return;
            float s = Mathf.Min(Size.X / Map.W, Size.Y / Map.H);
            var off = (Size - new Vector2(Map.W, Map.H) * s) / 2;
            var parch = Assets.Tex("parchment");
            if (parch != null) DrawTextureRect(parch, new Rect2(Vector2.Zero, Size), false, new Color(1, 1, 1, 0.9f));
            DrawTextureRect(Tex, new Rect2(off, new Vector2(Map.W, Map.H) * s), false, new Color(1, 1, 1, 0.92f));
            if (Map.PvpZone.Size != Vector2I.Zero)
            {
                var z = Map.PvpZone;
                DrawRect(new Rect2(off + new Vector2(z.Position.X, z.Position.Y) * s, new Vector2(z.Size.X, z.Size.Y) * s), new Color(0.8f, 0.05f, 0.05f, 0.18f));
            }
            var font = UiTheme.Heading;
            foreach (var l in Map.Labels)
            {
                var p = off + new Vector2(l.X, l.Z) * s;
                var w = font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, 16).X;
                DrawString(font, p + new Vector2(-w / 2 + 1, 1), l.Text, HorizontalAlignment.Left, -1, 16, Colors.Black);
                DrawString(font, p + new Vector2(-w / 2, 0), l.Text, HorizontalAlignment.Left, -1, 16, l.Text.StartsWith("THE BLOOD") ? new Color(1f, 0.35f, 0.3f) : UiTheme.Gold);
            }
            foreach (var o in Map.Objects)
            {
                if (o.Action is "Enter" or "Climb-up" or "Bank")
                {
                    var p = off + new Vector2(o.X + o.W / 2f, o.Z + o.D / 2f) * s;
                    DrawCircle(p, 5, Colors.Black);
                    DrawCircle(p, 4, o.Action == "Bank" ? new Color(1f, 0.85f, 0.2f) : new Color(0.6f, 0.2f, 0.9f));
                }
            }
            var me = GameWorld.I?.Me;
            if (me != null)
            {
                var p = off + new Vector2(me.GlobalPosition.X, me.GlobalPosition.Z) * s;
                float pulse = 5 + Mathf.Sin(Time.GetTicksMsec() / 150f) * 1.5f;
                DrawCircle(p, pulse + 2, Colors.Black);
                DrawCircle(p, pulse, Colors.White);
            }
            DrawString(ThemeDB.FallbackFont, new Vector2(8, Size.Y - 8), "Purple: dungeon entrances    Gold: treasury    Red: the Bloodmarch (PvP)", HorizontalAlignment.Left, -1, 13, new Color(0.2f, 0.12f, 0.05f));
        }
    }
}
