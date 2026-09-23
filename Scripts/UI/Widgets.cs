using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Renders each item's 3D model once in an offscreen viewport and caches the image as an icon.
public partial class IconCache : Node
{
    public static IconCache I { get; private set; }
    readonly Dictionary<string, Texture2D> done = new();
    readonly Dictionary<string, (SubViewport vp, int frames)> pending = new();
    readonly Queue<string> queue = new();

    public override void _EnterTree() => I = this;

    public Texture2D Get(string itemId)
    {
        if (itemId == null) return null;
        if (done.TryGetValue(itemId, out var t)) return t;
        if (!pending.ContainsKey(itemId) && !queue.Contains(itemId)) queue.Enqueue(itemId);
        return null;
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless") { queue.Clear(); return; }
        // Start a few renders per frame.
        for (int i = 0; i < 4 && queue.Count > 0; i++) StartRender(queue.Dequeue());
        var finished = new List<string>();
        foreach (var (id, (vp, frames)) in pending)
        {
            if (frames < 3) { pending[id] = (vp, frames + 1); continue; }
            var img = vp.GetTexture().GetImage();
            done[id] = ImageTexture.CreateFromImage(img);
            vp.QueueFree();
            finished.Add(id);
        }
        foreach (var f in finished) pending.Remove(f);
    }

    void StartRender(string id)
    {
        var def = ItemDb.Get(id);
        if (def == null) { done[id] = null; return; }
        var vp = new SubViewport { Size = new Vector2I(72, 72), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(vp);
        var root = new Node3D();
        vp.AddChild(root);
        var model = ItemVisuals.Build(def, 1f);
        root.AddChild(model);
        var box = Assets.ComputeAabb(model, Transform3D.Identity);
        model.Position -= box.GetCenter();
        var pivot = new Node3D();
        root.RemoveChild(model);
        pivot.AddChild(model);
        root.AddChild(pivot);
        bool diagonal = def.Icon is IconKind.Sword or IconKind.Scimitar or IconKind.Greatsword or IconKind.Dagger or IconKind.Warhammer
            or IconKind.Staff or IconKind.Battlestaff or IconKind.Shortbow or IconKind.Longbow or IconKind.Arrows;
        pivot.RotationDegrees = diagonal ? new Vector3(0, 30, -45) : new Vector3(18, 30, 0);
        float size = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
        var cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = Mathf.Max(0.2f, size * (diagonal ? 0.95f : 1.2f)), Position = new Vector3(0, 0, 5), Current = true };
        root.AddChild(cam);
        root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0), LightEnergy = 1.4f });
        root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(20, -150, 0), LightEnergy = 0.5f, LightColor = new Color(0.8f, 0.85f, 1f) });
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.6f), AmbientLightEnergy = 0.8f };
        root.AddChild(new WorldEnvironment { Environment = env });
        pending[id] = (vp, 0);
    }
}

/// One inventory/equipment/shop/bank cell.
public partial class ItemSlot : Control
{
    public ItemStack Stack;
    public int Index;
    public string Placeholder;         // shown faintly when empty (equipment slots)
    public Action<ItemSlot> LeftClick;
    public Action<ItemSlot, Vector2> RightClick;
    public Func<ItemSlot, bool> CanDrag;
    public Action<ItemSlot, ItemSlot> Dropped;
    public string Caption;             // e.g. shop price
    public bool Highlight;

    public ItemSlot()
    {
        CustomMinimumSize = new Vector2(44, 40);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left && !mb.DoubleClick) { LeftClick?.Invoke(this); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.Right) { RightClick?.Invoke(this, GetGlobalMousePosition()); AcceptEvent(); }
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Stack == null || CanDrag == null || !CanDrag(this)) return default;
        var prev = new TextureRect { Texture = IconCache.I?.Get(Stack.Id), Size = new Vector2(40, 40), Modulate = new Color(1, 1, 1, 0.7f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        SetDragPreview(prev);
        return this;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => Dropped != null && data.Obj is ItemSlot;
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.Obj is ItemSlot src && src != this) Dropped?.Invoke(src, this);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        if (Highlight) DrawRect(r, new Color(1, 0.85f, 0.3f, 0.25f));
        if (Stack == null)
        {
            if (Placeholder != null)
            {
                DrawRect(r.Grow(-2), UiTheme.Slot);
                DrawString(ThemeDB.FallbackFont, new Vector2(4, Size.Y / 2 + 5), Placeholder, HorizontalAlignment.Center, Size.X - 8, 11, new Color(1, 1, 1, 0.25f));
            }
            return;
        }
        if (Placeholder != null) DrawRect(r.Grow(-2), UiTheme.Slot);
        var tex = IconCache.I?.Get(Stack.Id);
        if (tex != null)
        {
            float s = Mathf.Min(Size.X, Size.Y);
            DrawTextureRect(tex, new Rect2((Size.X - s) / 2, (Size.Y - s) / 2, s, s), false);
        }
        else DrawCircle(Size / 2, 10, ItemDb.Get(Stack.Id)?.Tint ?? Colors.White);
        var def = ItemDb.Get(Stack.Id);
        if (def != null && (def.Stackable || Stack.Count > 1))
        {
            string txt = FormatCount(Stack.Count);
            var col = Stack.Count >= 10_000_000 ? new Color(0.3f, 1f, 0.5f) : Stack.Count >= 100_000 ? Colors.White : new Color(1f, 1f, 0.2f);
            DrawString(ThemeDB.FallbackFont, new Vector2(3, 13), txt, HorizontalAlignment.Left, -1, 12, Colors.Black);
            DrawString(ThemeDB.FallbackFont, new Vector2(2, 12), txt, HorizontalAlignment.Left, -1, 12, col);
        }
        if (Caption != null)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(1, Size.Y - 1), Caption, HorizontalAlignment.Center, Size.X, 11, Colors.Black);
            DrawString(ThemeDB.FallbackFont, new Vector2(0, Size.Y - 2), Caption, HorizontalAlignment.Center, Size.X, 11, new Color(1f, 0.9f, 0.4f));
        }
    }

    public static string FormatCount(int n) => n >= 10_000_000 ? $"{n / 1_000_000}M" : n >= 100_000 ? $"{n / 1000}K" : n.ToString();

    public override GodotObject _MakeCustomTooltip(string forText)
    {
        var d = ItemDb.Get(Stack?.Id);
        if (d == null) return Placeholder != null ? RichTooltip.Make($"[color=#a09070]{Placeholder} slot[/color]", 140) : null;
        var b = new System.Text.StringBuilder($"[b][color=#ffcc55]{d.Name}[/color][/b]");
        if (Stack.Count > 1) b.Append($"  [color=#c0b090]× {Stack.Count:N0}[/color]");
        b.Append($"\n[i][color=#b0a080]{d.Examine}[/color][/i]\n");
        if (d.Equippable)
        {
            b.Append($"[color=#e0c080]Attack[/color]  melee {d.AtkMelee:+0;-0;0}  archery {d.AtkRanged:+0;-0;0}  sorcery {d.AtkMagic:+0;-0;0}\n");
            b.Append($"[color=#e0c080]Defence[/color] melee {d.DefMelee:+0;-0;0}  archery {d.DefRanged:+0;-0;0}  sorcery {d.DefMagic:+0;-0;0}\n");
            if (d.StrMelee != 0 || d.StrRanged != 0 || d.MagicDmg != 0)
                b.Append($"[color=#e0c080]Power[/color]   might {d.StrMelee:+0;-0;0}  arrows {d.StrRanged:+0;-0;0}  spells {d.MagicDmg:+0;-0;0}%\n");
            if (d.Weapon != WeaponKind.None) b.Append($"[color=#e0c080]Speed[/color] {d.Speed * GameConst.TickSeconds:0.0}s{(d.TwoHanded ? "  • two-handed" : "")}\n");
            if (d.ReqLevel > 1)
            {
                var st = GameWorld.I?.State;
                bool ok = st?.Xp != null && Xp.LevelForXp(st.Xp[(int)d.ReqSkill]) >= d.ReqLevel;
                b.Append($"[color={(ok ? "#80ff80" : "#ff7060")}]Requires level {d.ReqLevel} {d.ReqSkill}[/color]\n");
            }
        }
        if (d.IsFood) b.Append($"[color=#80ff80]Restores {d.Heal} vitality[/color]\n");
        var ps = GameWorld.I?.State;
        if (d.Tool != ToolKind.None && d.ReqLevel > 1 && !d.Equippable)
        {
            bool ok = ps?.Xp != null && Xp.LevelForXp(ps.Xp[(int)d.ReqSkill]) >= d.ReqLevel;
            b.Append($"[color={(ok ? "#80ff80" : "#ff7060")}]Tool tier {d.ToolPower} • needs {d.ReqSkill} {d.ReqLevel}[/color]\n");
        }
        if (d.BoostPct > 0) b.Append($"[color=#a0d0ff]{d.BoostSkill} charm[/color]\n");
        if (d.Teaches != null)
        {
            var u = RecipeDb.GetUnlock(d.Teaches);
            bool known = ps?.Unlocks != null && System.Array.IndexOf(ps.Unlocks, d.Teaches) >= 0;
            b.Append(known ? "[color=#80ff80]You already know this.[/color]\n" : $"[color=#ffd080]Read to learn: {u?.Name}[/color]\n");
        }
        if (FarmDb.BySeed(d.Id) is { } crop) b.Append($"[color=#a0e080]Farming {crop.Level} • grows in ~{crop.GrowSeconds / 60} min[/color]\n");
        if (d.Use != null && d.Teaches == null) b.Append($"[color=#c0c0ff]Click to {d.Use.ToLowerInvariant()}.[/color]\n");
        b.Append($"[color=#a09070]Value: {d.Value:N0} crowns[/color]");
        return RichTooltip.Make(b.ToString(), 290);
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var d = ItemDb.Get(Stack?.Id);
        if (d == null) return Placeholder ?? "";
        var s = d.Name;
        if (d.Equippable)
        {
            s += $"\nAtt: {d.AtkMelee} melee / {d.AtkRanged} ranged / {d.AtkMagic} magic";
            s += $"\nDef: {d.DefMelee} melee / {d.DefRanged} ranged / {d.DefMagic} magic";
            if (d.StrMelee != 0 || d.StrRanged != 0 || d.MagicDmg != 0) s += $"\nStr: {d.StrMelee} melee / {d.StrRanged} ranged / {d.MagicDmg}% magic";
            if (d.ReqLevel > 1) s += $"\nRequires {d.ReqSkill} {d.ReqLevel}";
        }
        if (d.IsFood) s += $"\nHeals {d.Heal}";
        return s;
    }
}

/// RuneScape-style right-click menu drawn by hand.
public partial class ContextMenu : Control
{
    List<MenuOption> options = new();
    int hover = -1;
    const int RowH = 20, Header = 22, Pad = 6;
    public bool IsOpen => Visible;

    public ContextMenu()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;
    }

    public void Open(Vector2 pos, List<MenuOption> opts)
    {
        options = new List<MenuOption>(opts) { new MenuOption { Verb = "Cancel", Target = "" } };
        float w = 120;
        var font = ThemeDB.FallbackFont;
        foreach (var o in options)
            w = Mathf.Max(w, font.GetStringSize(Label(o), HorizontalAlignment.Left, -1, 14).X + Pad * 2 + 8);
        Size = new Vector2(w, Header + options.Count * RowH + 4);
        var vs = GetViewportRect().Size;
        Position = new Vector2(Mathf.Clamp(pos.X - w / 2, 0, vs.X - w), Mathf.Clamp(pos.Y - 8, 0, vs.Y - Size.Y));
        hover = -1;
        Visible = true;
        QueueRedraw();
    }

    static string Label(MenuOption o) => $"{o.Verb} {o.Target}{o.Suffix}";

    public void Close() { Visible = false; }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int h = (int)((mm.Position.Y - Header) / RowH);
            hover = mm.Position.Y < Header || h < 0 || h >= options.Count ? -1 : h;
            QueueRedraw();
        }
        else if (e is InputEventMouseButton mb && mb.Pressed && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            if (hover >= 0 && hover < options.Count)
            {
                var o = options[hover];
                Close();
                if (o.Run != null) GameWorld.I?.Execute(o);
            }
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var m = GetGlobalMousePosition();
        var r = GetGlobalRect().Grow(12);
        if (!r.HasPoint(m)) Close();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.36f, 0.32f, 0.25f, 0.97f));
        DrawRect(new Rect2(1, 1, Size.X - 2, Header - 2), new Color(0, 0, 0, 0.95f));
        DrawString(font, new Vector2(Pad, 16), "Actions", HorizontalAlignment.Left, -1, 14, new Color(0.36f, 0.32f, 0.25f));
        DrawRect(new Rect2(1, Header, Size.X - 2, Size.Y - Header - 1), new Color(0, 0, 0, 0.92f));
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            float y = Header + i * RowH + 15;
            if (i == hover) DrawRect(new Rect2(2, Header + i * RowH + 1, Size.X - 4, RowH), new Color(1, 1, 1, 0.12f));
            var verbCol = i == hover ? new Color(1f, 1f, 0.3f) : Colors.White;
            DrawString(font, new Vector2(Pad, y), o.Verb, HorizontalAlignment.Left, -1, 14, verbCol);
            float x = Pad + font.GetStringSize(o.Verb + " ", HorizontalAlignment.Left, -1, 14).X;
            if (!string.IsNullOrEmpty(o.Target))
            {
                DrawString(font, new Vector2(x, y), o.Target, HorizontalAlignment.Left, -1, 14, o.TargetColor);
                x += font.GetStringSize(o.Target, HorizontalAlignment.Left, -1, 14).X;
            }
            if (!string.IsNullOrEmpty(o.Suffix)) DrawString(font, new Vector2(x, y), o.Suffix, HorizontalAlignment.Left, -1, 14, o.TargetColor);
        }
    }
}

/// 2D UI icons from assets/icons/<name>.png (generated art), cached.
public static class UiIcons
{
    static readonly Dictionary<string, Texture2D> Cache = new();
    public static Texture2D Get(string name)
    {
        if (Cache.TryGetValue(name, out var t)) return t;
        string p = $"res://assets/icons/{name}.png";
        t = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
        Cache[name] = t;
        return t;
    }
}

/// A styled rich tooltip panel used by spells, items and skills.
public static class RichTooltip
{
    public static Control Make(string bbcode, float width = 280)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.08f, 0.065f, 0.05f, 0.97f), UiTheme.Gold, 2, 4, 10));
        var rt = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(width, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        rt.AddThemeFontSizeOverride("normal_font_size", 14);
        rt.AddThemeFontSizeOverride("bold_font_size", 16);
        rt.AddThemeColorOverride("default_color", UiTheme.Text);
        rt.Text = bbcode;
        panel.AddChild(rt);
        return panel;
    }
}

/// One spellbook entry: icon, state highlighting, rich tooltip.
public partial class SpellButton : Control
{
    public SpellDef Spell;
    public Func<PrivateState> State;
    public Action<SpellButton> Clicked;

    public SpellButton()
    {
        CustomMinimumSize = new Vector2(52, 52);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
    }

    PrivateState St => State?.Invoke();
    int Magic => St?.Xp != null ? Xp.LevelForXp(St.Xp[(int)Skill.Sorcery]) : 1;
    string Staff => ItemDb.Get(St?.Eq?[(int)EquipSlot.Weapon]?.Id)?.ProvidesRune;

    int Have(string rune)
    {
        if (St?.Inv == null) return 0;
        int n = 0;
        foreach (var s in St.Inv) if (s?.Id == rune) n += s.Count;
        return n;
    }

    bool CanAfford()
    {
        foreach (var (rune, count) in Spell.Runes)
            if (rune != Staff && Have(rune) < count) return false;
        return true;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Clicked?.Invoke(this);
            AcceptEvent();
        }
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        bool known = Magic >= Spell.Level;
        bool active = St?.Spell == Spell.Id;
        bool hover = GetGlobalRect().HasPoint(GetGlobalMousePosition());
        var tex = UiIcons.Get(Spell.Icon);
        var tint = !known ? new Color(0.3f, 0.3f, 0.3f) : CanAfford() ? Colors.White : new Color(0.75f, 0.55f, 0.55f);
        if (tex != null) DrawTextureRect(tex, r.Grow(-2), false, tint);
        else
        {
            var c = Color.FromHtml(Spell.Color);
            DrawRect(r.Grow(-2), new Color(0.1f, 0.08f, 0.06f));
            DrawCircle(Size / 2, Size.X * 0.32f, c * tint);
            var initials = string.Concat(Spell.Name.Split(' ').Select(w => w[0]));
            DrawString(ThemeDB.FallbackFont, new Vector2(0, Size.Y / 2 + 6), initials, HorizontalAlignment.Center, Size.X, 16, Colors.Black);
        }
        if (active) DrawRect(r.Grow(-1), UiTheme.Gold, false, 3);
        else if (hover && known) DrawRect(r.Grow(-1), new Color(1, 1, 1, 0.6f), false, 2);
        if (!known) DrawString(ThemeDB.FallbackFont, new Vector2(0, Size.Y - 4), Spell.Level.ToString(), HorizontalAlignment.Right, Size.X - 4, 12, new Color(1, 0.5f, 0.4f));
    }

    public override GodotObject _MakeCustomTooltip(string forText)
    {
        var sp = Spell;
        string kind = sp.Kind switch { SpellKind.Teleport => "Teleport", SpellKind.Heal => "Restoration", _ => "Combat spell" };
        var lvlCol = Magic >= sp.Level ? "#80ff80" : "#ff7060";
        var b = new System.Text.StringBuilder();
        b.Append($"[b][color=#{Color.FromHtml(sp.Color).Lerp(UiTheme.Gold, 0.5f).ToHtml(false)}]{sp.Name}[/color][/b]\n");
        b.Append($"[color={lvlCol}]Level {sp.Level} Sorcery[/color]  [color=#a09070]• {kind}[/color]\n");
        b.Append($"{sp.Description}\n");
        if (sp.Kind == SpellKind.Combat) b.Append($"[color=#e0c080]Max hit:[/color] {sp.MaxHit}    [color=#e0c080]XP:[/color] {sp.BaseXp:0.#} + 2 per damage\n");
        else b.Append($"[color=#e0c080]XP:[/color] {sp.BaseXp:0.#}    [color=#e0c080]Cooldown:[/color] {sp.CooldownTicks * GameConst.TickSeconds:0}s\n");
        b.Append("[color=#e0c080]Sigils:[/color]\n");
        foreach (var (rune, count) in sp.Runes)
        {
            var name = ItemDb.Get(rune)?.Name ?? rune;
            if (rune == Staff) b.Append($"   [color=#80c0ff]{count} × {name}  (supplied by your staff)[/color]\n");
            else
            {
                int have = Have(rune);
                b.Append($"   [color={(have >= count ? "#80ff80" : "#ff7060")}]{count} × {name}  (you have {have:N0})[/color]\n");
            }
        }
        string hint = Magic < sp.Level ? "You are not skilled enough to cast this yet."
            : sp.Kind == SpellKind.Combat ? (St?.Spell == sp.Id ? "Click to stop autocasting." : "Click to autocast when you attack.")
            : "Click to cast.";
        b.Append($"[i][color=#b0a080]{hint}[/color][/i]");
        return RichTooltip.Make(b.ToString(), 300);
    }
}
