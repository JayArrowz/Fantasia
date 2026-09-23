using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Tabbed side panel (Scenes/UI/SidePanel.tscn): Combat, Skills, Pack, Equipment, Spellbook,
/// Options, Quests. The layout lives in the scene; this fills it with the player's state.
public partial class SidePanel : PanelContainer
{
    static readonly PackedScene SkillCellScene = GD.Load<PackedScene>("res://Scenes/UI/SkillCell.tscn");
    Hud hud;
    readonly Control[] pages = new Control[7];
    readonly Button[] tabs = new Button[7];

    readonly ItemSlot[] inv = new ItemSlot[GameConst.InventorySize];
    readonly ItemSlot[] equip = new ItemSlot[11];
    Label bonusLabel, weaponLabel, combatLvl, spellLabel, totalLabel;
    readonly Button[] styleButtons = new Button[3];
    CheckBox retaliate;
    readonly Label[] skillLabels = new Label[Skills.Count];
    readonly ProgressBar[] skillBars = new ProgressBar[Skills.Count];
    readonly Control[] skillCells = new Control[Skills.Count];
    VBoxContainer questList;
    RichTextLabel questInfo;
    Label questCount, activityLabel;
    string questSel;
    PrivateState st;

    public void Init(Hud h) => hud = h;

    public override void _Ready()
    {
        string[] pageNames = { "Combat", "Skills", "Inventory", "Equipment", "Magic", "Options", "Quests" };
        for (int i = 0; i < pages.Length; i++)
        {
            int idx = i;
            pages[i] = GetNode<Control>("%" + pageNames[i]);
            tabs[i] = GetNode<Button>($"%Tab{i}");
            tabs[i].Pressed += () => ShowTab(idx);
        }

        // Combat
        weaponLabel = GetNode<Label>("%Weapon");
        combatLvl = GetNode<Label>("%CombatLevel");
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            styleButtons[i] = GetNode<Button>($"%Style{i}");
            styleButtons[i].Pressed += () => Send(new ClientMsg { T = C2S.Style, A = idx });
        }
        spellLabel = GetNode<Label>("%Spell");
        retaliate = GetNode<CheckBox>("%Retaliate");
        retaliate.Toggled += on => Send(new ClientMsg { T = C2S.Retaliate, A = on ? 1 : 0 });
        activityLabel = GetNode<Label>("%Activity");

        // Skills: one cell per skill (the skill list is game data, so cells are made here).
        var grid = GetNode<GridContainer>("%SkillGrid");
        foreach (var sk in Skills.All)
        {
            int i = (int)sk;
            var cell = SkillCellScene.Instantiate<Control>();
            grid.AddChild(cell);
            var ic = UiIcons.Get("skill_" + sk.ToString().ToLowerInvariant());
            cell.GetNode<TextureRect>("%Icon").Texture = ic;
            cell.GetNode<TextureRect>("%Icon").Visible = ic != null;
            skillLabels[i] = cell.GetNode<Label>("%Name");
            skillLabels[i].Text = sk.ToString();
            skillBars[i] = cell.GetNode<ProgressBar>("%Bar");
            skillBars[i].AddThemeStyleboxOverride("fill", UiTheme.Box(SkillColor(sk), SkillColor(sk), 0, 2, 0));
            skillCells[i] = cell;
        }
        totalLabel = GetNode<Label>("%Total");

        // Pack
        var invGrid = GetNode<GridContainer>("%InvGrid");
        for (int i = 0; i < inv.Length; i++)
        {
            var sl = invGrid.GetNode<ItemSlot>($"Slot{i}");
            sl.Index = i;
            sl.LeftClick = InvLeft;
            sl.RightClick = InvRight;
            sl.CanDrag = _ => !hud.BankOpen && !hud.ShopOpen;
            sl.Dropped = (a, b) =>
            {
                (a.Stack, b.Stack) = (b.Stack, a.Stack);
                Send(new ClientMsg { T = C2S.Swap, A = a.Index, B = b.Index });
            };
            inv[i] = sl;
        }

        // Equipment: slot nodes are named after their EquipSlot.
        foreach (var node in GetNode("%EquipGrid").GetChildren())
        {
            if (node is not ItemSlot sl || !Enum.TryParse<EquipSlot>(sl.Name, out var es)) continue;
            sl.Index = (int)es;
            sl.Placeholder = es.ToString();
            sl.LeftClick = x => { if (x.Stack != null) Send(new ClientMsg { T = C2S.Unequip, A = x.Index }); };
            sl.RightClick = (x, pos) =>
            {
                if (x.Stack == null) return;
                var d = ItemDb.Get(x.Stack.Id);
                hud.ShowMenu(pos, new List<MenuOption>
                {
                    new() { Verb = "Remove", Target = d.Name, TargetColor = new Color(1f, 0.6f, 0.25f), Run = () => Send(new ClientMsg { T = C2S.Unequip, A = x.Index }) },
                    new() { Verb = "Inspect", Target = d.Name, TargetColor = new Color(1f, 0.6f, 0.25f), Run = () => hud.GameMessage(d.Examine) },
                });
            };
            equip[(int)es] = sl;
        }
        bonusLabel = GetNode<Label>("%Bonuses");

        // Spellbook: one button per spell in spells.json.
        var spells = GetNode<GridContainer>("%SpellGrid");
        foreach (var sp in SpellDb.List)
        {
            var btn = new SpellButton { Spell = sp, State = () => st };
            btn.Clicked = b =>
            {
                var spell = b.Spell;
                if (spell.Kind == SpellKind.Combat)
                    Send(new ClientMsg { T = C2S.Spell, S = st?.Spell == spell.Id ? "" : spell.Id });
                else Send(new ClientMsg { T = C2S.Spell, S = spell.Id });
            };
            spells.AddChild(btn);
        }

        // Options
        GetNode<Button>("%OpenSettings").Pressed += () => Main.I?.SettingsUi.Open();
        GetNode<CheckBox>("%RunToggle").Toggled += on => Send(new ClientMsg { T = C2S.Run, A = on ? 1 : 0 });
        GetNode<Button>("%Logout").Pressed += () => Main.I?.Logout();

        // Quests
        questCount = GetNode<Label>("%QuestCount");
        questList = GetNode<VBoxContainer>("%QuestList");
        questInfo = GetNode<RichTextLabel>("%QuestInfo");

        ShowTab(2);
    }

    public void ShowTab(int i)
    {
        for (int k = 0; k < pages.Length; k++)
        {
            pages[k].Visible = k == i;
            tabs[k].SetPressedNoSignal(k == i);
        }
    }

    void Send(ClientMsg m) => Net.I.Send(m);

    // ================= skills =================
    static Color SkillColor(Skill s) => s switch
    {
        Skill.Prowess => new Color(0.8f, 0.2f, 0.2f), Skill.Might => new Color(0.2f, 0.7f, 0.3f), Skill.Fortitude => new Color(0.4f, 0.55f, 0.9f),
        Skill.Archery => new Color(0.3f, 0.75f, 0.3f), Skill.Sorcery => new Color(0.35f, 0.5f, 1f), Skill.Vitality => new Color(0.95f, 0.3f, 0.3f),
        Skill.Fishing => new Color(0.35f, 0.7f, 0.95f), Skill.Cooking => new Color(0.85f, 0.45f, 0.2f), Skill.Woodcutting => new Color(0.55f, 0.4f, 0.2f),
        Skill.Farming => new Color(0.45f, 0.8f, 0.3f), Skill.Mining => new Color(0.6f, 0.6f, 0.65f), Skill.Smithing => new Color(0.75f, 0.75f, 0.8f),
        Skill.Silkweaving => new Color(0.95f, 0.75f, 0.9f), _ => new Color(0.65f, 0.45f, 1f),
    };

    // ================= quests =================
    void RefreshQuests(PrivateState s)
    {
        foreach (var c in questList.GetChildren()) c.QueueFree();
        int done = 0;
        foreach (var q in QuestDb.All)
        {
            int stage = s.Quests != null && s.Quests.TryGetValue(q.Id, out var v) ? v : 0;
            if (stage >= QuestDb.Done) done++;
            var col = stage >= QuestDb.Done ? new Color(0.4f, 0.95f, 0.35f) : stage > 0 ? new Color(1f, 0.9f, 0.3f) : new Color(0.95f, 0.35f, 0.3f);
            var b = new Button { Text = q.Name, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 20) };
            b.AddThemeColorOverride("font_color", col);
            b.AddThemeColorOverride("font_hover_color", col.Lightened(0.3f));
            b.AddThemeFontSizeOverride("font_size", 13);
            var qid = q.Id;
            b.Pressed += () => { questSel = qid; ShowQuest(st); };
            questList.AddChild(b);
        }
        questCount.Text = $"Quests  {done}/{QuestDb.All.Count}";
        ShowQuest(s);
    }

    void ShowQuest(PrivateState s)
    {
        var q = QuestDb.Get(questSel);
        if (q == null || s == null) { questInfo.Text = "[color=#c8b89a]Select a quest to see its details. Talk to folk around Aldmoor to find work.[/color]"; return; }
        int stage = s.Quests != null && s.Quests.TryGetValue(q.Id, out var v) ? v : 0;
        var giver = NpcDb.Get(q.Giver)?.Name ?? q.Giver;
        var t = $"[b][color=#f0d080]{Hud.Esc(q.Name)}[/color][/b]\n[color=#e8dcc0]{Hud.Esc(q.Summary)}[/color]\n";
        if (stage == 0)
        {
            t += $"[color=#c8b89a]Start by talking to {Hud.Esc(giver)}.[/color]";
            if (q.Reqs.Length > 0) t += "\n[color=#c8b89a]Requires: " + string.Join(", ", q.Reqs.Select(r => $"{r.skill} {r.level}")) + "[/color]";
        }
        else if (stage >= QuestDb.Done) t += "[color=#80f070]Completed![/color]";
        else
        {
            var sg = q.Stages[stage - 1];
            t += $"[color=#fff0a0]▸ {Hud.Esc(sg.Journal)}[/color]";
            if (sg.Kind == QStep.Kill)
            {
                int k = s.QVars != null && s.QVars.TryGetValue(q.Id, out var kv) ? kv : 0;
                t += $" [color=#a0d0ff]({Math.Min(k, sg.Count)}/{sg.Count})[/color]";
            }
            else if (sg.Kind == QStep.Bring)
                t += "\n[color=#a0d0ff]" + string.Join(", ", sg.Items.Select(x => $"{Math.Min(s.Inv.Where(i => i?.Id == x.item).Sum(i => i.Count), x.count)}/{x.count} {Hud.Esc(ItemDb.Get(x.item)?.Name)}")) + "[/color]";
            t += $"\n[color=#c8b89a]Return to {Hud.Esc(giver)}.[/color]";
        }
        t += $"\n[color=#9fd89f]Reward: {Hud.Esc(q.RewardText())}[/color]";
        questInfo.Text = t;
    }

    static string WearVerb(ItemDef d) => "Equip";

    void InvLeft(ItemSlot s)
    {
        if (s.Stack == null) return;
        var d = ItemDb.Get(s.Stack.Id);
        if (hud.BankOpen) { Send(new ClientMsg { T = C2S.Deposit, A = s.Index, B = 1 }); return; }
        if (hud.ShopOpen) { hud.GameMessage($"{d.Name}: the shop will buy this for {Server.ServerWorld.SellPrice(d)} crowns."); return; }
        if (d.Equippable) Send(new ClientMsg { T = C2S.Inv, A = s.Index, S = WearVerb(d) });
        else if (d.IsFood) Send(new ClientMsg { T = C2S.Inv, A = s.Index, S = "Eat" });
        else if (d.Use != null) Send(new ClientMsg { T = C2S.Inv, A = s.Index, S = d.Use });
        else hud.GameMessage(d.Examine);
    }

    void InvRight(ItemSlot s, Vector2 pos)
    {
        if (s.Stack == null) return;
        var d = ItemDb.Get(s.Stack.Id);
        var col = new Color(1f, 0.6f, 0.25f);
        var opts = new List<MenuOption>();
        int idx = s.Index;
        if (hud.BankOpen)
        {
            foreach (var (label, n) in new[] { ("Deposit-1", 1), ("Deposit-5", 5), ("Deposit-10", 10), ("Deposit-All", int.MaxValue) })
                opts.Add(new MenuOption { Verb = label, Target = d.Name, TargetColor = col, Run = () => Send(new ClientMsg { T = C2S.Deposit, A = idx, B = n }) });
        }
        else if (hud.ShopOpen)
        {
            opts.Add(new MenuOption { Verb = "Value", Target = d.Name, TargetColor = col, Run = () => hud.GameMessage($"{d.Name}: the shop will buy this for {Server.ServerWorld.SellPrice(d)} crowns.") });
            foreach (var n in new[] { 1, 5, 10 })
                opts.Add(new MenuOption { Verb = $"Sell {n}", Target = d.Name, TargetColor = col, Run = () => Send(new ClientMsg { T = C2S.Sell, A = idx, B = n }) });
        }
        else
        {
            if (d.Equippable) opts.Add(new MenuOption { Verb = WearVerb(d), Target = d.Name, TargetColor = col, Run = () => Send(new ClientMsg { T = C2S.Inv, A = idx, S = WearVerb(d) }) });
            if (d.IsFood) opts.Add(new MenuOption { Verb = "Eat", Target = d.Name, TargetColor = col, Run = () => Send(new ClientMsg { T = C2S.Inv, A = idx, S = "Eat" }) });
            if (d.Use != null) opts.Add(new MenuOption { Verb = d.Use, Target = d.Name, TargetColor = col, Run = () => Send(new ClientMsg { T = C2S.Inv, A = idx, S = d.Use }) });
            opts.Add(new MenuOption { Verb = "Drop", Target = d.Name, TargetColor = col, Run = () => Send(new ClientMsg { T = C2S.Inv, A = idx, S = "Drop" }) });
        }
        opts.Add(new MenuOption { Verb = "Inspect", Target = d.Name, TargetColor = col, Run = () => hud.GameMessage(d.Examine + (s.Stack != null && s.Stack.Count > 1 ? $" ({s.Stack.Count:N0})" : "")) });
        hud.ShowMenu(pos, opts);
    }

    // ================= refresh =================
    public void Refresh(PrivateState s)
    {
        st = s;
        for (int i = 0; i < inv.Length; i++) inv[i].Stack = i < s.Inv.Length ? s.Inv[i] : null;
        foreach (var e in equip) if (e != null) e.Stack = s.Eq.Length > e.Index ? s.Eq[e.Index] : null;

        int L(Skill k) => Xp.LevelForXp(s.Xp[(int)k]);
        int total = 0;
        for (int i = 0; i < Skills.Count; i++)
        {
            int xp = i < s.Xp.Length ? s.Xp[i] : 0, lvl = Xp.LevelForXp(xp);
            total += lvl;
            var sk = (Skill)i;
            string cur = sk == Skill.Vitality ? $"{s.Hp}/{lvl}" : $"{lvl}";
            skillLabels[i].Text = $"{sk} {cur}";
            int a = Xp.XpForLevel(lvl), b = lvl >= 99 ? a + 1 : Xp.XpForLevel(lvl + 1);
            skillBars[i].Value = lvl >= 99 ? 1 : (xp - a) / (double)Math.Max(1, b - a);
            skillCells[i].TooltipText = $"{sk}: level {lvl}\n{(lvl >= 99 ? $"{xp:N0} xp" : $"{xp:N0} xp, {b - xp:N0} to level {lvl + 1}")}\n{Skills.Blurb(sk)}";
        }
        int cb = Xp.CombatLevel(L(Skill.Prowess), L(Skill.Might), L(Skill.Fortitude), L(Skill.Vitality), L(Skill.Archery), L(Skill.Sorcery));
        totalLabel.Text = $"Total level: {total}    Combat: {cb}";
        combatLvl.Text = $"Combat level: {cb}";

        var w = ItemDb.Get(s.Eq[(int)EquipSlot.Weapon]?.Id);
        weaponLabel.Text = w?.Name ?? "Unarmed";
        bool ranged = w != null && w.AttackType == CombatType.Ranged;
        string[] names = ranged
            ? new[] { "Aimed\n(Archery XP)", "Rapid\n(Archery XP)", "Distant\n(Archery + Fortitude XP)" }
            : new[] { "Precise\n(Prowess XP)", "Forceful\n(Might XP)", "Guarded\n(Fortitude XP)" };
        for (int i = 0; i < 3; i++)
        {
            styleButtons[i].Text = names[i];
            styleButtons[i].SetPressedNoSignal(s.Style == i);
        }
        var spell = SpellDb.Get(s.Spell);
        spellLabel.Text = spell != null ? $"Autocasting: {spell.Name}\n(click it again in the Magic tab to stop)" : "";
        retaliate.SetPressedNoSignal(s.Retal);
        activityLabel.Text = s.Activity != null ? $"Busy: {s.Activity}" : "";
        RefreshQuests(s);

        var bo = s.Bonus ?? new int[9];
        bonusLabel.Text =
            $"Attack\n  Melee {Sign(bo[0])}   Archery {Sign(bo[1])}   Sorcery {Sign(bo[2])}\n" +
            $"Defence\n  Melee {Sign(bo[3])}   Archery {Sign(bo[4])}   Sorcery {Sign(bo[5])}\n" +
            $"Power\n  Might {Sign(bo[6])}   Arrows {Sign(bo[7])}   Spells {Sign(bo[8])}%";
    }

    static string Sign(int v) => v >= 0 ? $"+{v}" : v.ToString();
}
