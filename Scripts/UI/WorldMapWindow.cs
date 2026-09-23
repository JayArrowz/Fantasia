using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

public partial class WorldMapWindow : PanelContainer
{
    MapData map;
    ImageTexture tex;
    WorldMapCanvas canvas;

    public override void _Ready()
    {
        canvas = GetNode<WorldMapCanvas>("%Canvas");
        GetNode<Button>("%Close").Pressed += () => Visible = false;
    }

    public void SetMap(MapData m)
    {
        map = m;
        tex = MapImage.Build(m);
        canvas.Map = m;
        canvas.Tex = tex;
    }
}
