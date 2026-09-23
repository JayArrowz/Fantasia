using Godot;

namespace Fantasia.Client;

/// Arrow or spell projectile travelling between two entities.
public partial class Projectile : Node3D
{
    public EntityView From, To;
    public float Duration = 0.6f;
    public bool Magic;
    public Color Color = Colors.White;
    /// Seconds before the projectile appears (release point of the shoot/cast animation).
    public float LaunchDelay;
    Vector3 start, lastEnd;
    float t;

    public override void _Ready()
    {
        Visible = LaunchDelay <= 0;
        start = From != null ? From.Chest : GlobalPosition;
        lastEnd = To != null ? To.Chest : start;
        if (Magic)
        {
            Props.Sphere(this, 0.18f, Vector3.Zero, Props.Mat(Color, 0.2f, 0f, 4f), null, 10);
            AddChild(new OmniLight3D { LightColor = Color, LightEnergy = 2f, OmniRange = 4f });
            var pm = new ParticleProcessMaterial { Gravity = Vector3.Zero, InitialVelocityMin = 0.1f, InitialVelocityMax = 0.4f, Spread = 180, ScaleMin = 0.5f, ScaleMax = 1f };
            var quad = new QuadMesh { Size = new Vector2(0.18f, 0.18f), Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = Color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add } };
            AddChild(new GpuParticles3D { Amount = 30, Lifetime = 0.4f, ProcessMaterial = pm, DrawPass1 = quad, LocalCoords = false });
        }
        else
        {
            Props.Box(this, new Vector3(0.025f, 0.025f, 0.7f), Vector3.Zero, Props.Mat(new Color(0.45f, 0.3f, 0.15f)));
            Props.Box(this, new Vector3(0.06f, 0.06f, 0.1f), new Vector3(0, 0, 0.36f), Props.Mat(Color, 0.4f, 0.7f));
            Props.Box(this, new Vector3(0.08f, 0.01f, 0.12f), new Vector3(0, 0, -0.3f), Props.Mat(new Color(0.9f, 0.9f, 0.9f)));
        }
        GlobalPosition = start;
    }

    public override void _Process(double delta)
    {
        if (LaunchDelay > 0)
        {
            LaunchDelay -= (float)delta;
            if (LaunchDelay > 0) return;
            if (IsInstanceValid(From) && From.IsInsideTree()) start = From.Chest + From.Basis.Z * 0.4f;
            Visible = true;
        }
        t += (float)delta;
        if (IsInstanceValid(To) && To.IsInsideTree()) lastEnd = To.Chest;
        float p = Mathf.Clamp(t / Duration, 0, 1);
        var pos = start.Lerp(lastEnd, p) + new Vector3(0, Mathf.Sin(p * Mathf.Pi) * (Magic ? 0.3f : 0.8f), 0);
        var next = start.Lerp(lastEnd, Mathf.Min(1, p + 0.02f)) + new Vector3(0, Mathf.Sin(Mathf.Min(1, p + 0.02f) * Mathf.Pi) * (Magic ? 0.3f : 0.8f), 0);
        GlobalPosition = pos;
        if ((next - pos).LengthSquared() > 0.00001f) LookAt(next, Vector3.Up, true);
        if (p >= 1)
        {
            if (Magic) GetParent()?.AddChild(new SpellImpact { Color = Color, Position = lastEnd });
            QueueFree();
        }
    }
}

/// Burst, shockwave ring and flash where a spell lands.
public partial class SpellImpact : Node3D
{
    public Color Color = Colors.White;
    float t;
    MeshInstance3D ring, core;
    OmniLight3D light;

    public override void _Ready()
    {
        AddChild(Burst.Make(Vector3.Zero, Color, 70, 3.5f, -1.5f, 0.14f));
        AddChild(Burst.Make(Vector3.Zero, Color.Lightened(0.5f), 30, 1.5f, 0f, 0.22f));
        var add = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = Color, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        core = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f, RadialSegments = 12, Rings = 6 }, MaterialOverride = add };
        AddChild(core);
        ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.4f, OuterRadius = 0.5f, Rings = 20, RingSegments = 4 }, MaterialOverride = add, RotationDegrees = new Vector3(90, 0, 0) };
        AddChild(ring);
        light = new OmniLight3D { LightColor = Color, LightEnergy = 4f, OmniRange = 5f };
        AddChild(light);
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float k = Mathf.Clamp(t / 0.35f, 0, 1);
        core.Scale = Vector3.One * (0.6f + k * 1.2f);
        core.Transparency = k;
        ring.Scale = Vector3.One * (0.5f + k * 2.2f);
        ring.Transparency = k;
        light.LightEnergy = 4f * (1 - k);
        if (t > 1.6f) QueueFree();
    }
}

/// Yellow (walk) or red (interact) cross where the player clicked.
public partial class ClickMarker : Node3D
{
    float t;
    public static ClickMarker Make(Vector3 pos, bool red)
    {
        var m = new ClickMarker { Position = pos + new Vector3(0, 0.05f, 0) };
        var mat = new StandardMaterial3D { AlbedoColor = red ? new Color(0.95f, 0.1f, 0.1f) : new Color(1f, 0.9f, 0.1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        Props.Box(m, new Vector3(0.6f, 0.02f, 0.08f), Vector3.Zero, mat, new Vector3(0, 45, 0));
        Props.Box(m, new Vector3(0.6f, 0.02f, 0.08f), Vector3.Zero, mat, new Vector3(0, -45, 0));
        return m;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        Scale = Vector3.One * Mathf.Max(0.01f, 1f - t * 1.5f);
        if (t > 0.65f) QueueFree();
    }
}

/// One-shot particle burst (level up, teleport, death puff).
public partial class Burst : GpuParticles3D
{
    public static Burst Make(Vector3 pos, Color color, int amount = 60, float speed = 3f, float gravity = -2f, float size = 0.12f)
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 60, InitialVelocityMin = speed * 0.5f, InitialVelocityMax = speed,
            Gravity = new Vector3(0, gravity, 0), ScaleMin = 0.5f, ScaleMax = 1.2f,
        };
        var quad = new QuadMesh
        {
            Size = new Vector2(size, size),
            Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = color, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
        };
        var b = new Burst { Amount = amount, Lifetime = 1.2f, OneShot = true, Explosiveness = 0.9f, ProcessMaterial = pm, DrawPass1 = quad, Position = pos, Emitting = true };
        return b;
    }

    float t;
    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t > Lifetime + 0.3f) QueueFree();
    }
}

/// Dropped item lying on a tile.
public partial class GroundItemView : Node3D
{
    public int Uid;
    public string ItemId;
    public int Count;
    public Tile Tile;
}
