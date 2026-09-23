using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Renders each item's 3D model once in an offscreen viewport and caches the image as an icon.
public partial class IconCache : Node
{
    public static IconCache I { get; private set; }
    readonly Dictionary<string, Texture2D> done = new();
    readonly Dictionary<string, (SubViewport vp, int frames)> pending = new();
    readonly Queue<string> queue = new();

    public override void _EnterTree() => I = this;

    public Texture2D Get(string itemId)
    {
        if (itemId == null) return null;
        if (done.TryGetValue(itemId, out var t)) return t;
        if (!pending.ContainsKey(itemId) && !queue.Contains(itemId)) queue.Enqueue(itemId);
        return null;
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless") { queue.Clear(); return; }
        // Start a few renders per frame.
        for (int i = 0; i < 4 && queue.Count > 0; i++) StartRender(queue.Dequeue());
        var finished = new List<string>();
        foreach (var (id, (vp, frames)) in pending)
        {
            if (frames < 3) { pending[id] = (vp, frames + 1); continue; }
            var img = vp.GetTexture().GetImage();
            done[id] = ImageTexture.CreateFromImage(img);
            vp.QueueFree();
            finished.Add(id);
        }
        foreach (var f in finished) pending.Remove(f);
    }

    void StartRender(string id)
    {
        var def = ItemDb.Get(id);
        if (def == null) { done[id] = null; return; }
        var vp = new SubViewport { Size = new Vector2I(72, 72), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(vp);
        var root = new Node3D();
        vp.AddChild(root);
        var model = ItemVisuals.Build(def, 1f);
        root.AddChild(model);
        var box = Assets.ComputeAabb(model, Transform3D.Identity);
        model.Position -= box.GetCenter();
        var pivot = new Node3D();
        root.RemoveChild(model);
        pivot.AddChild(model);
        root.AddChild(pivot);
        bool diagonal = def.Icon is IconKind.Sword or IconKind.Scimitar or IconKind.Greatsword or IconKind.Dagger or IconKind.Warhammer
            or IconKind.Staff or IconKind.Battlestaff or IconKind.Shortbow or IconKind.Longbow or IconKind.Arrows;
        pivot.RotationDegrees = diagonal ? new Vector3(0, 30, -45) : new Vector3(18, 30, 0);
        float size = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
        var cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = Mathf.Max(0.2f, size * (diagonal ? 0.95f : 1.2f)), Position = new Vector3(0, 0, 5), Current = true };
        root.AddChild(cam);
        root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0), LightEnergy = 1.4f });
        root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(20, -150, 0), LightEnergy = 0.5f, LightColor = new Color(0.8f, 0.85f, 1f) });
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.6f), AmbientLightEnergy = 0.8f };
        root.AddChild(new WorldEnvironment { Environment = env });
        pending[id] = (vp, 0);
    }
}
