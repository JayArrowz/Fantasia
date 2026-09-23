using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

public partial class DialogWindow : PanelContainer
{
    Label name;
    Label text;
    Button next;
    HBoxContainer choices;
    string[] lines = Array.Empty<string>();
    string[] opts;
    string questId;
    int index;

    public override void _Ready()
    {
        name = GetNode<Label>("%Speaker");
        text = GetNode<Label>("%Text");
        next = GetNode<Button>("%Next");
        next.Pressed += Next;
        choices = GetNode<HBoxContainer>("%Choices");
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
