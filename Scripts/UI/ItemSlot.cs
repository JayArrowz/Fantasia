using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

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
