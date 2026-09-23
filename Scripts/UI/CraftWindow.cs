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
    static readonly PackedScene RowScene = GD.Load<PackedScene>("res://Scenes/UI/CraftRow.tscn");
    VBoxContainer list;
    Label hint;
    CheckBox onlyMakeable;
    string station;

    public override void _Ready()
    {
        base._Ready();
        hint = GetNode<Label>("%Hint");
        onlyMakeable = GetNode<CheckBox>("%OnlyMakeable");
        onlyMakeable.Toggled += _ => Refresh();
        list = GetNode<VBoxContainer>("%List");
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
        var row = RowScene.Instantiate<CraftRow>();
        row.Setup(r, st, lvl, known, levelOk, canMake, count =>
        {
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = r.Id, A = count });
            // Close and let the crafting animation play; the station stays open server-side.
            Visible = false;
        });
        return row;
    }
}
