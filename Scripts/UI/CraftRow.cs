using System;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// One recipe in the crafting window (Scenes/UI/CraftRow.tscn): what it makes, the requirement,
/// the ingredients you have, and make-1 / make-5 / make-all buttons.
public partial class CraftRow : PanelContainer
{
    Recipe recipe;
    PrivateState state;
    int level, canMake;
    bool known, levelOk;
    Action<int> make;

    public void Setup(Recipe r, PrivateState st, int lvl, bool isKnown, bool isLevelOk, int makeable, Action<int> onMake)
    {
        (recipe, state, level, known, levelOk, canMake, make) = (r, st, lvl, isKnown, isLevelOk, makeable, onMake);
    }

    static int Have(PrivateState st, string id) => st?.Inv.Where(s => s?.Id == id).Sum(s => s.Count) ?? 0;

    public override void _Ready()
    {
        var r = recipe;
        ThemeTypeVariation = canMake > 0 ? "CraftRowReady" : "CraftRow";
        var slot = GetNode<ItemSlot>("%Slot");
        slot.Stack = new ItemStack(r.Out, r.OutCount);
        if (!known) slot.Modulate = new Color(0.45f, 0.45f, 0.45f);

        string title = r.OutCount > 1 ? $"{r.Name} x{r.OutCount}" : r.Name;
        if (r.Sigil && known && levelOk) title += $"  (x{RecipeDb.SigilYield(r, level, false)} per essence)";
        var name = GetNode<Label>("%Name");
        name.Text = title;
        name.AddThemeColorOverride("font_color", known ? UiTheme.Text : UiTheme.Dim);
        var req = GetNode<Label>("%Req");
        req.Text = $"{r.Skill} {r.Level}  ·  {r.Xp:0.#} xp";
        req.AddThemeColorOverride("font_color", levelOk ? new Color(0.6f, 0.85f, 0.5f) : new Color(1f, 0.45f, 0.35f));

        var locked = GetNode<Label>("%Locked");
        var needs = GetNode<RichTextLabel>("%Needs");
        locked.Visible = !known;
        needs.Visible = known;
        if (!known)
        {
            var u = RecipeDb.GetUnlock(r.Unlock);
            locked.Text = $"Locked: {u?.Name}. {u?.Hint}";
        }
        else
            needs.Text = string.Join("  ", r.In.Select(x =>
            {
                int have = Have(state, x.item);
                string col = have >= x.count ? "#b8e0a0" : "#ff8070";
                return $"[color={col}]{x.count}x {Hud.Esc(ItemDb.Get(x.item)?.Name)} ({have})[/color]";
            }));

        var buttons = GetNode<Control>("%Buttons");
        buttons.Visible = known && levelOk;
        foreach (var (node, n) in new[] { ("%Make1", 1), ("%Make5", 5), ("%MakeAll", 9999) })
        {
            var b = GetNode<Button>(node);
            b.Disabled = canMake <= 0;
            int count = n;
            b.Pressed += () => make?.Invoke(count);
        }
        var can = GetNode<Label>("%CanMake");
        can.Visible = canMake > 0;
        can.Text = $"can make {canMake}";
    }
}
