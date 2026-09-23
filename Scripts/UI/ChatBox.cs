using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

// =====================================================================================
public partial class ChatBox : PanelContainer
{
    RichTextLabel log;
    LineEdit input;

    public override void _Ready()
    {
        log = GetNode<RichTextLabel>("%Log");
        input = GetNode<LineEdit>("%Input");
        input.TextSubmitted += OnSubmit;
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
