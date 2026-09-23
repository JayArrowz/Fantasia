using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Base for the centred game windows (shop, bank, crafting). Layout: Scenes/UI/GameWindow.tscn,
/// which each window's scene inherits and fills in under %Body.
public abstract partial class GameWindow : PanelContainer
{
    protected VBoxContainer Body;
    protected Label TitleLabel;

    public override void _Ready()
    {
        TitleLabel = GetNode<Label>("%Title");
        Body = GetNode<VBoxContainer>("%Body");
        GetNode<Button>("%Close").Pressed += OnClose;
    }

    protected virtual void OnClose() => GameWorld.I?.Hud.CloseWindows(true);
}
