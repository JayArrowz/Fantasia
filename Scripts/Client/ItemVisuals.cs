using Godot;

namespace Fantasia.Client;

/// 3D item meshes (held weapons, ground drops). Convention: grip at origin, "up" along +Y.
public static class ItemVisuals
{
    public static float HeldLength(ItemDef d) => EquipTuning.T.Held(d);

    static bool IsLong(ItemDef d) => d.Icon is IconKind.Sword or IconKind.Scimitar or IconKind.Warhammer or IconKind.Greatsword or IconKind.Dagger
        or IconKind.Shortbow or IconKind.Longbow or IconKind.Staff or IconKind.Battlestaff
        or IconKind.Pickaxe or IconKind.Hatchet or IconKind.FishingRod or IconKind.Harpoon or IconKind.Hammer;

    /// Model for the item, scaled to `length` along its tallest axis, base at y=0.
    public static Node3D Build(ItemDef d, float length, float? gripAt = null)
    {
        var tint = d.TintModel ? d.Tint : (Color?)null;
        bool longItem = d.Icon is IconKind.Sword or IconKind.Scimitar or IconKind.Warhammer or IconKind.Greatsword or IconKind.Dagger
            or IconKind.Shortbow or IconKind.Longbow or IconKind.Staff or IconKind.Battlestaff or IconKind.Arrows
            or IconKind.Pickaxe or IconKind.Hatchet or IconKind.FishingRod or IconKind.Harpoon or IconKind.Hammer or IconKind.FishingNet;
        bool? wideUp = d.Icon switch
        {
            IconKind.Sword or IconKind.Scimitar or IconKind.Greatsword or IconKind.Dagger => false, // crossguard by the grip
            IconKind.Warhammer or IconKind.Staff or IconKind.Battlestaff => true,                 // head at the top
            IconKind.Pickaxe or IconKind.Hatchet or IconKind.Hammer or IconKind.Harpoon or IconKind.FishingNet => true,
            IconKind.FishingRod => false,
            _ => null,
        };
        var m = longItem ? Assets.ModelLongest(d.Model, length, tint, wideUp, gripAt) : Assets.Model(d.Model, length, 0, tint);
        if (m != null)
        {
            Dress(m, d, false);
            return m;
        }
        return Procedural(d, length);
    }

    /// The item's own colouring on a model built from its GLB (tint metal, dye cloth...).
    public static void Dress(Node m, ItemDef d, bool tintModel = true)
    {
        if (tintModel && d.TintModel) Assets.ApplyTint(m, d.Tint);
        if (d.ModulateModel && d.Tint != Colors.White) Assets.Modulate(m, d.Tint);
        else if (d.Icon == IconKind.Cape && !d.TintModel) Assets.ApplyTint(m, d.Tint, false);   // cloth takes the cape's own colour
        else if (!d.TintModel && d.Tint != Colors.White && (d.Icon is IconKind.Rune or IconKind.WizardHat or IconKind.Robe or IconKind.RobeBottom or IconKind.Cape or IconKind.Arrows or IconKind.Amulet
            or IconKind.LeatherBody or IconKind.LeatherChaps or IconKind.Boots))
            Assets.Modulate(m, d.Tint.Lightened(0.2f));
    }

    static Node3D Procedural(ItemDef d, float len)
    {
        var r = new Node3D();
        var metal = Props.Mat(d.Tint, 0.35f, 0.8f);
        var wood = Props.Mat(new Color(0.40f, 0.26f, 0.14f));
        var grip = Props.Mat(new Color(0.22f, 0.14f, 0.08f));
        switch (d.Icon)
        {
            case IconKind.Sword:
            case IconKind.Dagger:
            case IconKind.Greatsword:
                Props.Box(r, new Vector3(0.04f, len * 0.2f, 0.04f), new Vector3(0, len * 0.1f, 0), grip);
                Props.Box(r, new Vector3(len * 0.25f, 0.04f, 0.05f), new Vector3(0, len * 0.2f, 0), metal);
                Props.Box(r, new Vector3(len * 0.07f, len * 0.75f, 0.02f), new Vector3(0, len * 0.58f, 0), metal);
                break;
            case IconKind.Scimitar:
                Props.Box(r, new Vector3(0.04f, len * 0.2f, 0.04f), new Vector3(0, len * 0.1f, 0), grip);
                Props.Box(r, new Vector3(0.12f, 0.04f, 0.05f), new Vector3(0, len * 0.2f, 0), metal);
                Props.Box(r, new Vector3(len * 0.09f, len * 0.45f, 0.02f), new Vector3(0.01f, len * 0.43f, 0), metal, new Vector3(0, 0, -6));
                Props.Box(r, new Vector3(len * 0.09f, len * 0.35f, 0.02f), new Vector3(0.06f, len * 0.78f, 0), metal, new Vector3(0, 0, -18));
                break;
            case IconKind.Warhammer:
                Props.Cyl(r, 0.025f, 0.03f, len * 0.8f, new Vector3(0, len * 0.4f, 0), wood, 6);
                Props.Box(r, new Vector3(len * 0.35f, len * 0.16f, len * 0.16f), new Vector3(0, len * 0.85f, 0), metal);
                break;
            case IconKind.Shortbow:
            case IconKind.Longbow:
            {
                var bw = Props.Mat(d.Tint);
                for (int i = -3; i <= 3; i++)
                {
                    float a = i / 3f;
                    Props.Box(r, new Vector3(0.03f, len / 7f + 0.02f, 0.03f), new Vector3(-0.12f * (1 - a * a) * len, len * 0.5f + a * len * 0.45f, 0), bw, new Vector3(0, 0, a * 25f));
                }
                Props.Box(r, new Vector3(0.006f, len * 0.9f, 0.006f), new Vector3(0, len * 0.5f, 0), Props.Mat(new Color(0.9f, 0.9f, 0.85f)));
                break;
            }
            case IconKind.Staff:
            case IconKind.Battlestaff:
                Props.Cyl(r, 0.025f, 0.03f, len, new Vector3(0, len * 0.5f, 0), Props.Mat(new Color(0.35f, 0.24f, 0.14f)), 6);
                Props.Sphere(r, 0.07f, new Vector3(0, len + 0.04f, 0), Props.Mat(d.Tint, 0.2f, 0.1f, 2.5f), null, 8);
                break;
            case IconKind.Kiteshield:
                Props.Box(r, new Vector3(len * 0.6f, len * 0.7f, 0.05f), new Vector3(0, len * 0.55f, 0), metal);
                Props.Prism(r, new Vector3(len * 0.6f, len * 0.3f, 0.05f), new Vector3(0, len * 0.05f, 0), metal, new Vector3(0, 0, 180));
                break;
            case IconKind.Roundshield:
                Props.Cyl(r, len * 0.5f, len * 0.5f, 0.05f, new Vector3(0, len * 0.5f, 0), Props.Mat(d.Tint), 12, new Vector3(90, 0, 0));
                Props.Sphere(r, 0.07f, new Vector3(0, len * 0.5f, 0.03f), Props.Mat(new Color(0.4f, 0.4f, 0.42f), 0.4f, 0.8f), null, 6);
                break;
            case IconKind.FullHelm:
                Props.Sphere(r, 0.18f, new Vector3(0, 0.15f, 0), metal, new Vector3(1, 1.1f, 1.05f), 10);
                Props.Box(r, new Vector3(0.22f, 0.03f, 0.02f), new Vector3(0, 0.15f, 0.18f), Props.Mat(new Color(0.05f, 0.05f, 0.05f)));
                break;
            case IconKind.WizardHat:
                Props.Cyl(r, 0.27f, 0.27f, 0.02f, new Vector3(0, 0.02f, 0), Props.Mat(d.Tint), 12);
                Props.Cyl(r, 0.01f, 0.16f, 0.45f, new Vector3(0, 0.24f, 0), Props.Mat(d.Tint), 10, new Vector3(-8, 0, 0));
                break;
            case IconKind.Coif:
                Props.Sphere(r, 0.19f, new Vector3(0, 0.12f, -0.01f), Props.Mat(d.Tint), new Vector3(1, 1.1f, 1.05f), 10);
                break;
            case IconKind.Arrows:
                for (int i = 0; i < 5; i++)
                    Props.Box(r, new Vector3(0.015f, 0.5f, 0.015f), new Vector3((i - 2) * 0.03f, 0.25f, 0), Props.Mat(new Color(0.5f, 0.35f, 0.2f)), new Vector3(0, 0, (i - 2) * 4));
                Props.Box(r, new Vector3(0.18f, 0.06f, 0.03f), new Vector3(0, 0.5f, 0), Props.Mat(d.Tint, 0.4f, 0.7f));
                break;
            case IconKind.Coins:
                for (int i = 0; i < 5; i++)
                    Props.Cyl(r, 0.08f, 0.08f, 0.02f, new Vector3((i % 3 - 1) * 0.09f, 0.01f + i * 0.02f, (i / 3) * 0.08f), Props.Mat(new Color(0.95f, 0.8f, 0.25f), 0.3f, 0.9f), 10);
                break;
            case IconKind.Rune:
                Props.Box(r, new Vector3(0.18f, 0.06f, 0.14f), new Vector3(0, 0.03f, 0), Props.Mat(new Color(0.55f, 0.55f, 0.55f)));
                Props.Box(r, new Vector3(0.08f, 0.01f, 0.06f), new Vector3(0, 0.065f, 0), Props.Mat(d.Tint, 0.3f, 0f, 2f));
                break;
            case IconKind.Fish:
                Props.Sphere(r, 0.1f, new Vector3(0, 0.06f, 0), Props.Mat(d.Tint), new Vector3(2.2f, 0.7f, 1f), 8);
                Props.Prism(r, new Vector3(0.12f, 0.12f, 0.02f), new Vector3(-0.25f, 0.06f, 0), Props.Mat(d.Tint), new Vector3(0, 0, 90));
                break;
            case IconKind.Bread:
                Props.Sphere(r, 0.12f, new Vector3(0, 0.07f, 0), Props.Mat(d.Tint), new Vector3(1.6f, 0.8f, 1f), 8);
                break;
            case IconKind.Bones:
                Props.Cyl(r, 0.03f, 0.03f, 0.35f, new Vector3(0, 0.04f, 0), Props.Mat(new Color(0.9f, 0.88f, 0.8f)), 6, new Vector3(0, 0, 90));
                Props.Cyl(r, 0.03f, 0.03f, 0.3f, new Vector3(0, 0.04f, 0.06f), Props.Mat(new Color(0.9f, 0.88f, 0.8f)), 6, new Vector3(0, 40, 90));
                break;
            case IconKind.Cape:
                Props.Box(r, new Vector3(0.5f, len, 0.03f), new Vector3(0, len * 0.5f, 0), Props.Mat(d.Tint));
                break;
            case IconKind.Needle:
                Props.Cyl(r, 0.004f, 0.012f, 0.3f, new Vector3(0, 0.02f, 0), Props.Mat(d.Tint, 0.3f, 0.9f), 6, new Vector3(0, 0, 80));
                Props.Cyl(r, 0.03f, 0.03f, 0.08f, new Vector3(0.1f, 0.05f, 0), Props.Mat(new Color(0.8f, 0.2f, 0.2f)), 8);
                break;
            case IconKind.WateringCan:
                Props.Cyl(r, 0.12f, 0.14f, 0.22f, new Vector3(0, 0.11f, 0), Props.Mat(d.Tint, 0.4f, 0.7f), 10);
                Props.Cyl(r, 0.015f, 0.03f, 0.25f, new Vector3(0.17f, 0.17f, 0), Props.Mat(d.Tint, 0.4f, 0.7f), 6, new Vector3(0, 0, -55));
                Props.Cyl(r, 0.1f, 0.1f, 0.02f, new Vector3(-0.03f, 0.27f, 0), Props.Mat(d.Tint, 0.4f, 0.7f), 8, new Vector3(90, 0, 0));
                break;
            case IconKind.Bait:
                Props.Cyl(r, 0.1f, 0.1f, 0.1f, new Vector3(0, 0.05f, 0), Props.Mat(new Color(0.5f, 0.5f, 0.52f), 0.4f, 0.6f), 10);
                for (int i = 0; i < 3; i++) Props.Sphere(r, 0.03f, new Vector3((i - 1) * 0.04f, 0.11f, 0.02f), Props.Mat(d.Tint), new Vector3(2f, 0.8f, 0.8f), 6);
                break;
            case IconKind.Seed:
                Props.Sphere(r, 0.12f, new Vector3(0, 0.1f, 0), Props.Mat(new Color(0.75f, 0.62f, 0.42f)), new Vector3(1, 1.2f, 1), 8);
                Props.Cyl(r, 0.03f, 0.05f, 0.06f, new Vector3(0, 0.23f, 0), Props.Mat(new Color(0.55f, 0.4f, 0.25f)), 6);
                for (int i = 0; i < 3; i++) Props.Sphere(r, 0.022f, new Vector3((i - 1) * 0.05f, 0.02f, 0.13f), Props.Mat(d.Tint), null, 5);
                break;
            case IconKind.Produce:
                for (int i = 0; i < 3; i++) Props.Sphere(r, 0.08f, new Vector3((i - 1) * 0.1f, 0.07f, (i % 2) * 0.06f), Props.Mat(d.Tint), new Vector3(1.1f, 0.9f, 1f), 8);
                break;
            case IconKind.Nest:
                Props.Cyl(r, 0.2f, 0.14f, 0.1f, new Vector3(0, 0.05f, 0), Props.Mat(d.Tint), 10);
                for (int i = 0; i < 3; i++) Props.Sphere(r, 0.045f, new Vector3((i - 1) * 0.06f, 0.12f, (i % 2) * 0.03f), Props.Mat(new Color(0.75f, 0.85f, 0.9f)), new Vector3(1, 1.3f, 1), 6);
                break;
            case IconKind.Feather:
                Props.Sphere(r, 0.12f, new Vector3(0, 0.03f, 0), Props.Mat(d.Tint), new Vector3(0.4f, 0.12f, 1.6f), 8);
                break;
            case IconKind.Vial:
                Props.Cyl(r, 0.06f, 0.08f, 0.2f, new Vector3(0, 0.1f, 0), Props.Mat(d.Tint, 0.1f, 0f, 0.3f), 10);
                Props.Cyl(r, 0.03f, 0.03f, 0.06f, new Vector3(0, 0.23f, 0), Props.Mat(new Color(0.55f, 0.4f, 0.25f)), 6);
                break;
            case IconKind.Thread:
                Props.Cyl(r, 0.09f, 0.09f, 0.16f, new Vector3(0, 0.08f, 0), Props.Mat(d.Tint, 0.6f), 10);
                Props.Cyl(r, 0.11f, 0.11f, 0.02f, new Vector3(0, 0.01f, 0), Props.Mat(new Color(0.5f, 0.35f, 0.2f)), 10);
                Props.Cyl(r, 0.11f, 0.11f, 0.02f, new Vector3(0, 0.17f, 0), Props.Mat(new Color(0.5f, 0.35f, 0.2f)), 10);
                break;
            case IconKind.Meat:
                Props.Sphere(r, 0.12f, new Vector3(0, 0.08f, 0), Props.Mat(d.Tint), new Vector3(1.4f, 0.8f, 1f), 8);
                Props.Cyl(r, 0.025f, 0.025f, 0.14f, new Vector3(0.2f, 0.06f, 0), Props.Mat(new Color(0.92f, 0.9f, 0.82f)), 6, new Vector3(0, 0, 90));
                break;
            default:
                Props.Box(r, new Vector3(0.25f, 0.2f, 0.18f), new Vector3(0, 0.1f, 0), Props.Mat(d.Tint));
                break;
        }
        return r;
    }

    /// Small model lying on the ground for dropped items.
    public static Node3D Ground(ItemDef d)
    {
        var size = d.Icon switch
        {
            IconKind.Platebody or IconKind.Robe or IconKind.LeatherBody or IconKind.Platelegs or IconKind.RobeBottom or IconKind.LeatherChaps => 0.5f,
            IconKind.Sword or IconKind.Scimitar or IconKind.Greatsword or IconKind.Warhammer or IconKind.Staff or IconKind.Battlestaff or IconKind.Shortbow or IconKind.Longbow => 0.7f,
            IconKind.Kiteshield or IconKind.Roundshield => 0.5f,
            _ => 0.3f,
        };
        var root = new Node3D();
        if (IsLong(d) && Assets.HasModel(d.Model))
        {
            // Weapons and tools lie flat at their held length (a footprint fit would size a sword by its crossguard).
            float len = HeldLength(d) * 0.8f;
            var flat = Build(d, len);
            flat.RotationDegrees = new Vector3(90, 0, 0);
            flat.Position = new Vector3(0, 0.04f, -len * 0.5f);
            root.AddChild(flat);
            return root;
        }
        var tint = d.TintModel ? d.Tint : (Color?)null;
        var m = Assets.Model(d.Model, 0, size, tint) ?? Procedural(d, size);
        bool lay = d.Icon is IconKind.Sword or IconKind.Scimitar or IconKind.Greatsword or IconKind.Warhammer or IconKind.Staff or IconKind.Battlestaff
            or IconKind.Shortbow or IconKind.Longbow or IconKind.Dagger or IconKind.Arrows or IconKind.Kiteshield or IconKind.Roundshield or IconKind.Cape;
        if (lay && !Assets.HasModel(d.Model)) m.RotationDegrees = new Vector3(90, 0, 0);
        root.AddChild(m);
        return root;
    }
}
