using Godot;

namespace Fantasia.Client;

/// Procedural crop rows for an allotment: sprouts grow into leafy plants bearing produce.
public static class CropVisual
{
    public static Node3D Build(CropDef c, int stage, PatchPhase phase, int w, int d)
    {
        var root = new Node3D { Name = "Crop" };
        if (c == null) return root;
        bool sick = phase == PatchPhase.Diseased, dead = phase == PatchPhase.Dead;
        var leafCol = c.Id switch
        {
            "wheat" => stage >= 3 ? new Color(0.9f, 0.75f, 0.3f) : new Color(0.45f, 0.7f, 0.3f),
            "woad" => new Color(0.3f, 0.55f, 0.5f),
            "madder" => new Color(0.5f, 0.55f, 0.25f),
            "moonpetal" => new Color(0.55f, 0.7f, 0.75f),
            _ => new Color(0.30f, 0.62f, 0.22f),
        };
        if (sick) leafCol = new Color(0.55f, 0.48f, 0.18f);
        if (dead) leafCol = new Color(0.35f, 0.30f, 0.25f);
        var leaf = Props.Mat(leafCol, 0.9f);
        float glow = c.Id is "moonpetal" or "emberbloom" && stage >= 3 && !sick && !dead ? 1.2f : 0f;
        var fruit = Props.Mat(dead ? new Color(0.3f, 0.28f, 0.25f) : c.Color, 0.6f, 0f, glow);
        var spots = Props.Mat(new Color(0.1f, 0.08f, 0.05f));

        float sx = (w - 0.6f) / 2f, sz = (d - 0.6f) / 2f;
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                var pos = new Vector3(ix * sx * 0.75f, 0f, iz * sz * 0.75f);
                float jitter = Hash.Unit(ix + 5, iz + 5, stage) * 0.1f;
                if (stage == 0)
                {
                    Props.Sphere(root, 0.08f, pos + new Vector3(0, 0.02f, 0), Props.Mat(new Color(0.25f, 0.17f, 0.1f)), new Vector3(1.4f, 0.5f, 1.4f), 6);
                    Props.Sphere(root, 0.03f, pos + new Vector3(0.03f, 0.06f, 0), leaf, null, 5);
                    continue;
                }
                float h = stage switch { 1 => 0.18f, 2 => 0.32f, _ => 0.45f } + jitter;
                if (dead) h *= 0.5f;
                if (c.Id == "wheat")
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * 1.57f + ix;
                        var o = pos + new Vector3(Mathf.Cos(a) * 0.08f, 0, Mathf.Sin(a) * 0.08f);
                        Props.Cyl(root, 0.012f, 0.02f, h * 1.6f, o + new Vector3(0, h * 0.8f, 0), leaf, 4, new Vector3(dead ? 50 : 6, a * 57f, 0));
                        if (stage >= 3) Props.Sphere(root, 0.035f, o + new Vector3(0, h * 1.6f, 0), fruit, new Vector3(1, 2.2f, 1), 5);
                    }
                    continue;
                }
                // Leafy rosette: a ring of flattened, tilted leaves around a centre.
                int leaves = stage switch { 1 => 3, 2 => 5, _ => 7 };
                for (int k = 0; k < leaves; k++)
                {
                    float a = k * Mathf.Tau / leaves + ix * 0.7f + iz;
                    float lr = h * 0.35f;
                    Props.Sphere(root, h * 0.32f, pos + new Vector3(Mathf.Cos(a) * lr, h * 0.35f, Mathf.Sin(a) * lr), leaf,
                        new Vector3(0.5f, 0.18f, 1.25f), 6).RotationDegrees = new Vector3(dead ? 20 : -30, -Mathf.RadToDeg(a) + 90, 0);
                }
                Props.Cyl(root, 0.01f, 0.02f, h * 0.5f, pos + new Vector3(0, h * 0.25f, 0), leaf, 4);
                if (sick) Props.Sphere(root, h * 0.2f, pos + new Vector3(0.05f, h * 0.7f, 0.05f), spots, null, 5);
                if (stage >= 3 && !dead)
                {
                    bool flower = c.Id is "moonpetal" or "emberbloom" or "madder" or "woad";
                    if (flower)
                        for (int k = 0; k < 3; k++)
                            Props.Sphere(root, 0.05f, pos + new Vector3(Mathf.Cos(k * 2.1f) * 0.12f, h + 0.05f, Mathf.Sin(k * 2.1f) * 0.12f), fruit, null, 6);
                    else if (c.Id == "strawberry")
                        for (int k = 0; k < 4; k++)
                            Props.Sphere(root, 0.04f, pos + new Vector3(Mathf.Cos(k * 1.6f) * 0.16f, 0.06f, Mathf.Sin(k * 1.6f) * 0.16f), fruit, new Vector3(1, 1.2f, 1), 6);
                    else
                        Props.Sphere(root, c.Id == "cabbage" ? 0.16f : 0.09f, pos + new Vector3(0, c.Id == "cabbage" ? h * 0.5f : 0.07f, 0), fruit, null, 7);
                }
            }
        if (phase == PatchPhase.Ready)
        {
            // A gentle sparkle so ripe patches stand out.
            var pm = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 30, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.5f, Gravity = Vector3.Zero, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w * 0.4f, 0.1f, d * 0.4f) };
            var quad = new QuadMesh { Size = new Vector2(0.06f, 0.06f), Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = new Color(1f, 0.95f, 0.5f), BlendMode = BaseMaterial3D.BlendModeEnum.Add } };
            root.AddChild(new GpuParticles3D { Amount = 12, Lifetime = 1.5f, ProcessMaterial = pm, DrawPass1 = quad, Position = new Vector3(0, 0.5f, 0) });
        }
        return root;
    }
}
