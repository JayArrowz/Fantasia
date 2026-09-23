using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// The in-game interface: side panel tabs, chat, minimap, orbs, windows, hover text and menus.
public partial class Hud : CanvasLayer
{
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

    /// The HUD scene (Scenes/UI/Hud.tscn): layout and styling live there; this wires it to the game.
    public static Hud Create() => GD.Load<PackedScene>("res://Scenes/UI/Hud.tscn").Instantiate<Hud>();

    public override void _Ready()
    {
        overlay = GetNode<Overlay>("%Overlay");
        hover = GetNode<RichTextLabel>("%Hover");
        areaLabel = GetNode<Label>("%AreaLabel");
        minimap = GetNode<Minimap>("%Minimap");
        side = GetNode<SidePanel>("%SidePanel");
        pvpLabel = GetNode<Label>("%PvpLabel");
        chat = GetNode<ChatBox>("%Chat");
        xpDrops = GetNode<Control>("%XpDrops");
        levelUp = GetNode<Label>("%LevelUp");
        fps = GetNode<Label>("%Fps");
        shop = GetNode<ShopWindow>("%Shop");
        bank = GetNode<BankWindow>("%Bank");
        dialog = GetNode<DialogWindow>("%Dialog");
        craft = GetNode<CraftWindow>("%Craft");
        banner = GetNode<Label>("%Banner");
        worldMap = GetNode<WorldMapWindow>("%WorldMap");
        menu = GetNode<ContextMenu>("%Menu");
        side.Init(this);
        shop.Init(this);
        bank.Init(this);

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
