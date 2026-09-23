using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// The in-game interface: side panel tabs, chat, minimap, orbs, windows, hover text and menus.
public partial class Hud : CanvasLayer
{
    Control root;
    Overlay overlay;
    RichTextLabel hover;
    ChatBox chat;
    Minimap minimap;
    SidePanel side;
    ContextMenu menu;
    Label pvpLabel, areaLabel;
    ShopWindow shop;
    BankWindow bank;
    DialogWindow dialog;
    CraftWindow craft;
    Label banner;
    float bannerT;
    WorldMapWindow worldMap;
    Control xpDrops;
    Label levelUp, fps;
    float levelUpT;
    public PrivateState State { get; private set; }

    public override void _Ready()
    {
        Layer = 10;
        root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        overlay = new Overlay();
        root.AddChild(overlay);

        hover = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(10, 8), Size = new Vector2(700, 30) };
        hover.AddThemeFontSizeOverride("normal_font_size", 16);
        root.AddChild(hover);

        areaLabel = UiTheme.Title("", 26);
        UiTheme.Place(areaLabel, Control.LayoutPreset.CenterTop, new Vector2(-300, 60), new Vector2(600, 40));
        areaLabel.Modulate = new Color(1, 1, 1, 0);
        root.AddChild(areaLabel);

        minimap = new Minimap();
        UiTheme.Place(minimap, Control.LayoutPreset.TopRight, new Vector2(-222, 8), new Vector2(214, 214));
        root.AddChild(minimap);

        side = new SidePanel(this);
        UiTheme.Place(side, Control.LayoutPreset.BottomRight, new Vector2(-258, -398), new Vector2(250, 390));
        root.AddChild(side);

        pvpLabel = UiTheme.Lbl("☠ THE BLOODMARCH — PvP zone", 15, new Color(1f, 0.3f, 0.25f));
        UiTheme.Place(pvpLabel, Control.LayoutPreset.BottomRight, new Vector2(-258, -424), new Vector2(250, 22));
        pvpLabel.Visible = false;
        root.AddChild(pvpLabel);

        chat = new ChatBox();
        UiTheme.Place(chat, Control.LayoutPreset.BottomLeft, new Vector2(8, -198), new Vector2(520, 190));
        root.AddChild(chat);

        xpDrops = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Place(xpDrops, Control.LayoutPreset.TopRight, new Vector2(-340, 150), new Vector2(110, 20));
        root.AddChild(xpDrops);

        levelUp = UiTheme.Title("", 30);
        UiTheme.Place(levelUp, Control.LayoutPreset.Center, new Vector2(-400, -200), new Vector2(800, 50));
        levelUp.Visible = false;
        root.AddChild(levelUp);

        fps = UiTheme.Lbl("", 13, UiTheme.Gold);
        UiTheme.Place(fps, Control.LayoutPreset.TopLeft, new Vector2(10, 34), new Vector2(120, 20));
        root.AddChild(fps);

        shop = new ShopWindow(this); root.AddChild(shop);
        bank = new BankWindow(this); root.AddChild(bank);
        dialog = new DialogWindow(); root.AddChild(dialog);
        craft = new CraftWindow(); root.AddChild(craft);
        banner = UiTheme.Title("", 26);
        UiTheme.Place(banner, Control.LayoutPreset.CenterTop, new Vector2(-400, 110), new Vector2(800, 40));
        banner.HorizontalAlignment = HorizontalAlignment.Center;
        banner.Visible = false;
        root.AddChild(banner);
        worldMap = new WorldMapWindow(); root.AddChild(worldMap);

        menu = new ContextMenu();
        root.AddChild(menu);

        chat.Add("[color=#7a1a00][b]Welcome to Fantasia![/b][/color] Left-click to walk or act, right-click for options. Arrow keys or middle-mouse rotate the camera. Press [b]M[/b] for the world map, [b]Enter[/b] to chat.");
    }

    public void Send(ClientMsg m) => Net.I.Send(m);

    public bool IsPointerOverUi(Vector2 p)
    {
        if (menu.Visible) return true;
        foreach (Control c in new Control[] { minimap, side, chat, shop, bank, dialog, worldMap, craft })
            if (c.Visible && c.GetGlobalRect().HasPoint(p)) return true;
        return false;
    }

    // ---------- hover / menu ----------
    public void SetHover(MenuOption o, int more)
    {
        if (o == null) { hover.Text = ""; return; }
        string t = $"[color=white]{o.Verb}[/color]";
        if (!string.IsNullOrEmpty(o.Target)) t += $" [color=#{o.TargetColor.ToHtml(false)}]{Esc(o.Target)}{Esc(o.Suffix ?? "")}[/color]";
        if (more > 0) t += $" [color=white]/ {more} more option{(more > 1 ? "s" : "")}[/color]";
        hover.Text = t;
    }

    public static string Esc(string s) => (s ?? "").Replace("[", "[lb]");

    public void ShowContextMenu(Vector2 pos, List<MenuOption> opts) => menu.Open(pos, opts);
    public void ShowMenu(Vector2 pos, List<MenuOption> opts) => menu.Open(pos, opts);

    // ---------- messages ----------
    public static bool EchoChat;
    public void GameMessage(string s) { chat.Add(Esc(s)); if (EchoChat || DisplayServer.GetName() == "headless") GD.Print($"[Chat] {s}"); }
    public void ChatMessage(string who, string text)
    {
        chat.Add($"[color=#101010]{Esc(who)}:[/color] [color=#1030b0]{Esc(text)}[/color]");
        if (DisplayServer.GetName() == "headless") GD.Print($"[Chat] {who}: {text}");
    }

    public void XpDrop(Skill s, int amount)
    {
        var l = UiTheme.Lbl($"+{amount} {s} xp", 15, new Color(1f, 1f, 1f));
        l.Position = new Vector2(0, xpDrops.GetChildCount() * 20);
        xpDrops.AddChild(l);
        var tw = l.CreateTween();
        tw.TweenProperty(l, "position:y", l.Position.Y - 60f, 1.6f);
        tw.Parallel().TweenProperty(l, "modulate:a", 0f, 1.6f).SetDelay(0.6f);
        tw.TweenCallback(Callable.From(l.QueueFree));
    }

    public void LevelUp(string skill, int level)
    {
        levelUp.Text = $"{skill} level {level}!";
        levelUp.Visible = true;
        levelUpT = 3.5f;
    }

    public void SetPvp(bool on)
    {
        pvpLabel.Visible = on;
        if (on) GameMessage("You enter the Bloodmarch. Other adventurers may attack you here!");
    }

    public void OnMapChanged(MapData m)
    {
        minimap.SetMap(m);
        worldMap.SetMap(m);
        areaLabel.Text = m.Name;
        var tw = areaLabel.CreateTween();
        areaLabel.Modulate = new Color(1, 1, 1, 0);
        tw.TweenProperty(areaLabel, "modulate:a", 1f, 0.8f);
        tw.TweenInterval(2.0f);
        tw.TweenProperty(areaLabel, "modulate:a", 0f, 1.2f);
        CloseWindows(false);
        menu.Close();
    }

    public void OnSnapshot(Snapshot s) => minimap.QueueRedraw();

    public void OnState(PrivateState st)
    {
        State = st;
        side.Refresh(st);
        minimap.State = st;
        if (bank.Visible) bank.Refresh(st);
        if (shop.Visible) shop.Refresh();
        if (craft.Visible) craft.Refresh();
    }

    public void Banner(string text, Color color)
    {
        banner.Text = text;
        banner.AddThemeColorOverride("font_color", color);
        banner.Visible = true;
        bannerT = 4f;
    }

    // ---------- windows ----------
    Tile? dialogTile;
    public void ShowDialog(string name, string[] lines, int npcId, string[] opts = null, string questId = null)
    {
        dialog.Open(name, lines, opts, questId);
        dialogTile = GameWorld.I?.Me?.Tile;
    }
    Tile? craftTile;
    public void OpenCraft(string station, string flavour) { shop.Visible = false; bank.Visible = false; dialog.Visible = false; craft.Open(station, flavour); side.ShowTab(2); craftTile = GameWorld.I?.Me?.Tile; }
    public bool CraftOpen => craft.Visible;
    public void OpenShop(string id) { CloseWindows(false); shop.Open(id); side.ShowTab(2); }
    public void OpenBank() { CloseWindows(false); bank.Open(); bank.Refresh(State); side.ShowTab(2); }
    public bool ShopOpen => shop.Visible;
    public bool BankOpen => bank.Visible;

    public void CloseWindows(bool notifyServer)
    {
        bool had = shop.Visible || bank.Visible || craft.Visible;
        shop.Visible = false;
        bank.Visible = false;
        craft.Visible = false;
        dialog.Visible = false;
        if (had && notifyServer) Send(new ClientMsg { T = C2S.CloseUi });
    }

    public void ToggleWorldMap() => worldMap.Visible = !worldMap.Visible;
    public void ShowTab(int i) => side.ShowTab(i);

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        switch (k.Keycode)
        {
            case Key.Enter: chat.Focus(); GetViewport().SetInputAsHandled(); break;
            case Key.M: ToggleWorldMap(); break;
            case Key.O: Main.I?.SettingsUi.Open(); break;
            case Key.Escape: CloseWindows(true); worldMap.Visible = false; menu.Close(); break;
            case Key.F1: side.ShowTab(0); break;
            case Key.F2: side.ShowTab(1); break;
            case Key.F3: side.ShowTab(2); break;
            case Key.F4: side.ShowTab(3); break;
            case Key.F5: side.ShowTab(4); break;
            case Key.F6: side.ShowTab(5); break;
            case Key.F7: side.ShowTab(6); break;
        }
    }

    public override void _Process(double delta)
    {
        if (levelUpT > 0)
        {
            levelUpT -= (float)delta;
            levelUp.Modulate = new Color(1, 1, 1, Mathf.Clamp(levelUpT, 0, 1));
            if (levelUpT <= 0) levelUp.Visible = false;
        }
        if (bannerT > 0)
        {
            bannerT -= (float)delta;
            banner.Modulate = new Color(1, 1, 1, Mathf.Clamp(bannerT, 0, 1));
            if (bannerT <= 0) banner.Visible = false;
        }
        // Walking away ends the conversation (and closes a crafting window).
        var myTile = GameWorld.I?.Me?.Tile;
        if (dialog.Visible && myTile != null && dialogTile != null && myTile != dialogTile) dialog.Visible = false;
        if (craft.Visible && myTile != null && craftTile != null && myTile != craftTile) craft.Visible = false;
        overlay.QueueRedraw();
        fps.Visible = Settings.ShowFps;
        if (fps.Visible) fps.Text = $"{Engine.GetFramesPerSecond():0} FPS";
    }
}

// =====================================================================================
/// Draws HP bars, hitsplats and overhead chat above entities.
public partial class Overlay : Control
{
    public Overlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var gw = GameWorld.I;
        var cam = gw?.Rig?.Cam;
        if (cam == null) return;
        var font = ThemeDB.FallbackFont;
        foreach (var v in gw.Views.Values)
        {
            var head = v.Head;
            if (cam.IsPositionBehind(head)) continue;
            var p = cam.UnprojectPosition(head);
            float y = p.Y;
            if (v.HpShow > 0 && v.MaxHp > 0 && !v.Dead)
            {
                float w = 40, ratio = Mathf.Clamp(v.Hp / (float)v.MaxHp, 0, 1);
                DrawRect(new Rect2(p.X - w / 2 - 1, y - 1, w + 2, 7), Colors.Black);
                DrawRect(new Rect2(p.X - w / 2, y, w, 5), new Color(0.8f, 0.05f, 0.05f));
                DrawRect(new Rect2(p.X - w / 2, y, w * ratio, 5), new Color(0.1f, 0.85f, 0.1f));
                y -= 8;
            }
            if (v.IsPlayer && !v.IsMe)
            {
                DrawString(font, new Vector2(p.X - 100, y - 2), v.DisplayName, HorizontalAlignment.Center, 200, 13, Colors.Black);
                DrawString(font, new Vector2(p.X - 101, y - 3), v.DisplayName, HorizontalAlignment.Center, 200, 13, Colors.White);
                y -= 14;
            }
            if (v.ChatT > 0 && v.Chat != null)
            {
                DrawString(font, new Vector2(p.X - 201, y - 3), v.Chat, HorizontalAlignment.Center, 400, 15, Colors.Black);
                DrawString(font, new Vector2(p.X - 200, y - 4), v.Chat, HorizontalAlignment.Center, 400, 15, new Color(1f, 1f, 0.1f));
            }
            var chest = cam.UnprojectPosition(v.Chest);
            foreach (var s in v.Splats)
            {
                float a = Mathf.Clamp(1.3f - s.T, 0, 1);
                var c = new Vector2(chest.X + s.Offset, chest.Y - s.T * 14);
                DrawCircle(c, 12, new Color(0, 0, 0, a * 0.8f));
                DrawCircle(c, 10.5f, new Color(s.Color, a));
                string txt = s.Damage.ToString();
                DrawString(font, c + new Vector2(-14, 5), txt, HorizontalAlignment.Center, 28, 14, new Color(1, 1, 1, a));
            }
        }
    }
}

// =====================================================================================
public partial class ChatBox : PanelContainer
{
    RichTextLabel log;
    LineEdit input;

    public ChatBox()
    {
        AddThemeStyleboxOverride("panel", UiTheme.ParchmentStyle());
        var v = new VBoxContainer();
        AddChild(v);
        log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = false };
        log.AddThemeColorOverride("default_color", new Color(0.1f, 0.07f, 0.04f));
        log.AddThemeConstantOverride("outline_size", 0);
        log.AddThemeFontSizeOverride("normal_font_size", 14);
        v.AddChild(log);
        input = new LineEdit { PlaceholderText = "Press Enter to chat...", MaxLength = 80 };
        input.TextSubmitted += OnSubmit;
        v.AddChild(input);
    }

    public void Add(string bb)
    {
        log.AppendText(bb + "\n");
        if (log.GetLineCount() > 200) log.RemoveParagraph(0);
    }

    public void Focus() => input.GrabFocus();

    void OnSubmit(string text)
    {
        text = text.Trim();
        input.Text = "";
        input.ReleaseFocus();
        if (text.Length == 0) return;
        Net.I.Send(new ClientMsg { T = C2S.Chat, S = text });
    }
}
