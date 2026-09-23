using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia.Client;

/// res://data/equipment.json: how worn gear is fitted to characters. Which outfit model dresses
/// each armour type, how the wearer's own clothes are hidden under it, and the fit of headgear,
/// shields, pendants, capes and held items. In debug builds the file is watched and changes are
/// applied to everyone on screen straight away.
public sealed class EquipTuning
{
    public static EquipTuning T { get; private set; } = GameData.Load<EquipTuning>(File);
    const string File = "equipment.json";

    /// Armour icon kind -> the rigged outfit model its piece is taken from.
    public Dictionary<IconKind, OutfitDef> Outfits = new();
    /// How dyed/metal gear recolours its outfit texture.
    public RecolourDef Metal = new(), Cloth = new();
    public ArmourDef Armour = new();
    /// Per character model overrides (fractions of standing height, measured up from the feet).
    public Dictionary<string, WearerDef> Wearers = new();
    public HeadgearDef Headgear = new();
    public ShieldDef Shield = new();
    public PendantDef Pendant = new();
    public CapeDef Cape = new();
    /// Resting hold of a weapon while idle/walking: handle direction (model space, +Z forward, +X the
    /// wearer's left, mirrored for the other hand) and elbow bend in degrees.
    public Dictionary<string, CarryDef> Carry = new();
    /// Length in metres of held and worn item models, by icon kind.
    public Dictionary<IconKind, float> HeldLength = new();
    public float DefaultHeldLength = 0.4f;
    /// Fallback placements for rigs without finger bones.
    public FallbackDef Fallback = new();

    public sealed class OutfitDef { public string Model; public bool Metal; }
    public sealed class RecolourDef { public float Amount, Metallic, Roughness = -1; }

    public sealed class ArmourDef
    {
        public float Pull = 0.04f;               // metres armour is drawn towards the camera (wins over clothes)
        public float HemLuminance = 0.55f;       // body texture brighter than this counts as shirt
        public float ShirtSaturation = 0.25f;    // shirt texels are less saturated than this (skin is more saturated)...
        public float ShirtLuminance = 0.4f;      // ...and brighter than this (shaded collar included)
        public float CollarRise = 0.02f;         // body pieces keep their collar up to the neck bone + this
        public float HemShirtShare = 0.25f;      // a height band is shirt while this share of it is
        public float HemMargin = 0.03f;          // extra below the found hem (fraction of height)
        public float HemFallback = 0.1f;         // hem below the hips when the texture can't tell
        public float Belt = 0.045f;              // skirts start this far above the hips bone
        public float Ankle = 0.12f;              // boots take the shin up to this fraction of leg length
        public float TorsoOverlap = 0.01f;       // body piece reaches this far below the hem
        public float LegsOverlap = 0.005f;
        public float NeckKeep = 0f;              // shoulders/trunk above the shoulder bones + this (fraction) are never hidden
        public float NeckColumnKeep = 0f;        // the neck column above the shoulder bones + this is never hidden
    }

    public sealed class WearerDef { public float? Hem, Belt; }

    public sealed class HeadgearDef
    {
        // Multiples of the measured head size (width / height / depth); Above lifts the crown clear of the hair.
        public float HelmW = 1.28f, HelmH = 1.34f, HelmD = 1.36f, HelmBack = -0.02f, Above = 0.12f;
        public float HoodW = 1.28f, HoodH = 1.3f, HoodD = 1.22f, HoodBack = 0.06f, HoodAbove = 0.06f;
        public float HatBrim = 1.9f, HatSink = 0.5f;
    }

    public sealed class ShieldDef
    {
        public float Elbow = 80f, Along = 0.8f, Out = 0.04f, Ahead = 0.07f;
        public Vector3 Face = new(0.45f, 0f, 0.9f);
    }

    public sealed class PendantDef { public float Drop = 0.105f, Length = 0.12f, Gap = 0.004f; }

    public sealed class CapeDef
    {
        public Vector3 Offset = new(0, -1.0f, -0.26f), Rotation = Vector3.Zero, Scale = new(0.85f, 1f, 0.5f);
    }

    public sealed class CarryDef { public Vector3 Dir; public float Elbow; }

    public sealed class FallbackDef
    {
        public Vector3 WeaponOffset = new(0, 0, 0.01f), WeaponRotation = new(115, 0, 0);
        public Vector3 ShieldOffset = new(0.07f, 0, 0), ShieldRotation = new(0, 90, 0);
        public Vector3 HelmOffset = new(0, 0.02f, 0.01f);
    }

    public float Held(ItemDef d) => HeldLength.TryGetValue(d.Icon, out var l) ? l : DefaultHeldLength;
    public CarryDef CarryFor(string kind) => Carry.TryGetValue(kind, out var c) ? c : new CarryDef { Dir = Vector3.Back, Elbow = 20 };

    /// Raised after a hot reload, so bodies can refit their gear.
    public static event Action Changed;
    static ulong stamp = GameData.Modified(File);
    static double nextCheck;

    /// Debug builds: reload when the file changes (checked about once a second).
    public static void Poll(double now)
    {
        if (!OS.IsDebugBuild() || now < nextCheck) return;
        nextCheck = now + 1.0;
        ulong m = GameData.Modified(File);
        if (m == stamp) return;
        stamp = m;
        try
        {
            T = GameData.Load<EquipTuning>(File);
            GD.Print("[EquipTuning] reloaded equipment.json");
            Changed?.Invoke();
        }
        catch (Exception e) { GD.PrintErr($"[EquipTuning] {e.Message}"); }
    }
}
