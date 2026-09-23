using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Recipe list for a crafting station. Shows what you can make, what you're missing and which
/// recipes are still locked (and where to find them). The server validates every request.
public partial class CraftWindow : GameWindow
{
    readonly VBoxContainer list;
    readonly Label hint;
    readonly CheckBox onlyMakeable;
    string station;

    public CraftWindow() : base("Crafting", new Vector2(520, 520))
    {
        hint = UiTheme.Lbl("", 13, UiTheme.Gold);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(hint);
        onlyMakeable = new CheckBox { Text = "Only show what I can make now", FocusMode = FocusModeEnum.None };
        onlyMakeable.Toggled += _ => Refresh();
        Body.AddChild(onlyMakeable);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        Body.AddChild(scroll);
    }

    public string Station => Visible ? station : null;

    public void Open(string st, string flavour)
    {
        station = st;
        TitleLabel.Text = RecipeDb.StationName(st);
        hint.Text = (st, flavour) switch
        {
            ("enchant", "altar") => "The dark altar doubles every sigil you carve here.",
            ("cook", "hearth") => "A hearth: food burns less often than on a campfire.",
            ("cook", _) => "Campfires are handy, but food burns more often than on a hearth.",
            ("smith", _) => "You need a hammer in your pack.",
            ("sew", _) => "Sew cloth into garments with your needle.",
            ("smelt", _) => "Iron is finicky: it crumbles often until you've practised.",
            ("enchant", _) => "Carve sigils from essence (more per stone as you level), craft jewellery, then imbue it. The dark altar at the stone circle doubles sigils.",
            ("spin", _) => "Spin cocoons into silk thread. Silk thread also strings bows.",
            ("weave", _) => "Three threads weave a bolt of cloth. Dye silk with madder or woad once you've learnt how.",
            ("workbench", _) => "Bows need silk thread for a string. Fletch shafts, feathers and arrowheads into arrows.",
            _ => "",
        };
        Visible = true;
        Refresh();
    }

    int Have(PrivateState st, string id) => st?.Inv.Where(s => s?.Id == id).Sum(s => s.Count) ?? 0;

    public void Refresh()
    {
        if (!Visible || station == null) return;
        var st = GameWorld.I?.State;
        foreach (var c in list.GetChildren()) c.QueueFree();
        var unlocks = new HashSet<string>(st?.Unlocks ?? Array.Empty<string>());
        string group = null;
        foreach (var r in RecipeDb.ForStation(station))
        {
            int lvl = st?.Xp != null ? Xp.LevelForXp(st.Xp[(int)r.Skill]) : 1;
            bool known = r.Unlock == null || unlocks.Contains(r.Unlock);
            bool levelOk = lvl >= r.Level;
            int canMake = known && levelOk ? r.In.Min(x => Have(st, x.item) / Math.Max(1, x.count)) : 0;
            if (onlyMakeable.ButtonPressed && canMake <= 0) continue;
            if (r.Group != group)
            {
                group = r.Group;
                list.AddChild(UiTheme.Title(group, 15));
            }
            list.AddChild(Row(r, st, lvl, known, levelOk, canMake));
        }
    }

    Control Row(Recipe r, PrivateState st, int lvl, bool known, bool levelOk, int canMake)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(canMake > 0 ? new Color(0.22f, 0.18f, 0.11f) : new Color(0.14f, 0.12f, 0.09f), UiTheme.PanelBorder, 1, 3, 4));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        panel.AddChild(h);
        var slot = new ItemSlot { Stack = new ItemStack(r.Out, r.OutCount), CustomMinimumSize = new Vector2(44, 44), TooltipText = " " };
        if (!known) slot.Modulate = new Color(0.45f, 0.45f, 0.45f);
        h.AddChild(slot);

        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        h.AddChild(v);
        string title = r.OutCount > 1 ? $"{r.Name} x{r.OutCount}" : r.Name;
        if (r.Sigil && known && levelOk) title += $"  (x{RecipeDb.SigilYield(r, lvl, false)} per essence)";
        v.AddChild(UiTheme.Lbl(title, 14, known ? UiTheme.Text : UiTheme.Dim));
        var req = UiTheme.Lbl($"{r.Skill} {r.Level}  ·  {r.Xp:0.#} xp", 12, levelOk ? new Color(0.6f, 0.85f, 0.5f) : new Color(1f, 0.45f, 0.35f));
        v.AddChild(req);
        if (!known)
        {
            var u = RecipeDb.GetUnlock(r.Unlock);
            var lk = UiTheme.Lbl($"Locked: {u?.Name}. {u?.Hint}", 12, new Color(0.9f, 0.7f, 0.4f));
            lk.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            v.AddChild(lk);
        }
        else
        {
            var needs = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(250, 0) };
            needs.AddThemeFontSizeOverride("normal_font_size", 12);
            needs.Text = string.Join("  ", r.In.Select(x =>
            {
                int have = Have(st, x.item);
                string col = have >= x.count ? "#b8e0a0" : "#ff8070";
                return $"[color={col}]{x.count}x {Hud.Esc(ItemDb.Get(x.item)?.Name)} ({have})[/color]";
            }));
            v.AddChild(needs);
        }

        if (known && levelOk)
        {
            var btns = new VBoxContainer();
            btns.AddThemeConstantOverride("separation", 2);
            h.AddChild(btns);
            var row1 = new HBoxContainer();
            btns.AddChild(row1);
            foreach (var (label, n) in new[] { ("1", 1), ("5", 5), ("All", 9999) })
            {
                var b = new Button { Text = label, CustomMinimumSize = new Vector2(38, 30), Disabled = canMake <= 0, FocusMode = FocusModeEnum.None, TooltipText = $"Make {(n > 100 ? "as many as you can" : n.ToString())}" };
                int count = n;
                b.Pressed += () =>
                {
                    Net.I.Send(new ClientMsg { T = C2S.Craft, S = r.Id, A = count });
                    // Close and let the crafting animation play; the station stays open server-side.
                    Visible = false;
                };
                row1.AddChild(b);
            }
            if (canMake > 0) btns.AddChild(UiTheme.Lbl($"can make {canMake}", 11, UiTheme.Dim));
        }
        return panel;
    }
}
