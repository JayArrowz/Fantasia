using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

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
