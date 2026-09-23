using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia.Client;

public sealed class Hitsplat
{
    public int Damage;
    public float T;
    public Color Color;
    public float Offset;
}

/// Client-side representation of a server entity: interpolates between server ticks and
/// hosts the body visual. HP bars, hitsplats and chat are drawn in 2D by the Overlay.
public partial class EntityView : Node3D
{
    public int Id;
    public bool IsPlayer;
    public bool IsMe;
    public string DefId;
    public NpcDef Def;
    public string DisplayName;
    public int CombatLevel;
    public int Hp, MaxHp;
    public int TargetId = -1;
    public bool Dead;
    public Tile Tile;
    public BodyVisual Body;
    public float HpShow;
    public string Chat;
    public float ChatT;
    public readonly List<Hitsplat> Splats = new();

    MapData map;
    Vector3 from, to;
    float lerp = 1f;
    float yaw;
    float moveSpeed;
    string[] eq;

    public float VisualHeight => Body?.Height ?? 1.8f;
    public float Radius => Mathf.Clamp(VisualHeight * 0.28f, 0.3f, 1.4f);

    public void Init(EntitySnap s, MapData m, bool isMe)
    {
        map = m;
        Id = s.Id;
        IsPlayer = s.K == 0;
        IsMe = isMe;
        DefId = s.D;
        Tile = new Tile(s.X, s.Z);
        Position = from = to = map.TileCenter(Tile);
        yaw = (float)(Hash.Unit(s.Id, 0) * Math.PI * 2);
        if (IsPlayer)
        {
            DisplayName = s.D;
            Body = MakePlayerBody(s.D);
        }
        else
        {
            Def = NpcDb.Get(s.D);
            DisplayName = Def?.Name ?? s.D;
            Body = MakeNpcBody(Def);
        }
        AddChild(Body);
        Apply(s, true);
    }

    static BodyVisual MakePlayerBody(string name)
    {
        if (Settings.UseGeneratedCharacters)
        {
            var rig = RiggedHumanoid.TryCreate("char_player", 1.8f, Colors.White);
            if (rig != null) return rig;
        }
        uint h = Hash.Of(name);
        Color[] skins = { new(0.95f, 0.78f, 0.62f), new(0.85f, 0.65f, 0.48f), new(0.65f, 0.45f, 0.3f), new(0.45f, 0.3f, 0.2f) };
        Color[] hairs = { new(0.15f, 0.1f, 0.05f), new(0.45f, 0.3f, 0.12f), new(0.8f, 0.65f, 0.3f), new(0.6f, 0.2f, 0.08f), new(0.1f, 0.1f, 0.1f) };
        var b = new ProceduralHumanoid { Skin = skins[h % skins.Length], Hair = hairs[(h / 7) % hairs.Length], Shirt = new Color(0.55f, 0.48f, 0.35f), Pants = new Color(0.32f, 0.26f, 0.2f) };
        b.Build(1.8f);
        return b;
    }

    static BodyVisual MakeNpcBody(NpcDef d)
    {
        if (d == null)
        {
            var p = new ProceduralHumanoid();
            p.Build(1.8f);
            return p;
        }
        if (d.Body == NpcBody.Humanoid && d.Model != null && d.Model.StartsWith("npc_"))
        {
            if (Settings.UseGeneratedCharacters)
            {
                var rig = RiggedHumanoid.TryCreate(d.Model, d.Height, d.Tint);
                if (rig != null)
                {
                    if (d.WeaponModel != null) rig.SetEquipment(NpcEquip(d, true));
                    return rig;
                }
            }
            var h = new ProceduralHumanoid();
            NpcLook(d, h);
            h.Build(d.Height);
            if (d.WeaponModel != null) h.SetEquipment(NpcEquip(d, false));
            return h;
        }
        var c = new CreatureVisual();
        c.Build(d);
        return c;
    }

    /// Fake equipment array so NPCs can hold weapons using the same attachment code.
    static string[] NpcEquip(NpcDef d, bool rigged)
    {
        var eq = new string[11];
        string id = d.WeaponModel switch
        {
            "item_sword" => d.WeaponTint.R > 0.7f ? "steel_sword" : d.WeaponTint.R < 0.5f && d.WeaponTint.G < 0.45f ? "bronze_sword" : "iron_sword",
            "item_dagger" => "bronze_dagger",
            "item_scimitar" => d.WeaponTint.R > 0.7f ? "steel_sabre" : "iron_sabre",
            "item_warhammer" => "iron_warhammer",
            "item_greatsword" => d.WeaponTint.R < 0.3f ? "emberforged_greatsword" : "steel_greatsword",
            "item_shortbow" => "shortbow",
            "item_staff" => "staff",
            _ => null,
        };
        eq[(int)EquipSlot.Weapon] = id;
        if (d.Id == "guard")
        {
            // The generated guard already wears a helm, mail and tabard; only the primitive body needs them added.
            eq[(int)EquipSlot.Shield] = "iron_heater";
            if (!rigged) { eq[(int)EquipSlot.Head] = "iron_helm"; eq[(int)EquipSlot.Body] = "iron_chestplate"; }
        }
        if (d.Id == "goblin_chieftain") eq[(int)EquipSlot.Head] = "iron_helm";
        return eq;
    }

    static void NpcLook(NpcDef d, ProceduralHumanoid h)
    {
        switch (d.Id)
        {
            case "goblin": case "cave_goblin": case "goblin_archer": case "goblin_chieftain":
                h.Skin = new Color(0.35f, 0.6f, 0.2f) * d.Tint; h.Shirt = new Color(0.4f, 0.3f, 0.2f); h.Pants = new Color(0.3f, 0.25f, 0.15f); h.Hair = h.Skin; h.Hunched = true; break;
            case "skeleton": case "skeletal_warlord":
                h.Bony = true; h.Skin = new Color(0.9f, 0.88f, 0.8f); h.Shirt = d.Id == "skeletal_warlord" ? new Color(0.12f, 0.1f, 0.12f) : new Color(0.85f, 0.83f, 0.75f); h.Pants = h.Shirt; h.Boots = h.Skin; break;
            case "zombie":
                h.Skin = new Color(0.45f, 0.55f, 0.4f); h.Shirt = new Color(0.35f, 0.3f, 0.25f); h.Pants = new Color(0.25f, 0.25f, 0.3f); h.Hunched = true; break;
            case "hexer": case "mage_elara":
                h.Shirt = d.Id == "hexer" ? new Color(0.15f, 0.1f, 0.2f) : new Color(0.2f, 0.25f, 0.7f); h.Pants = h.Shirt; break;
            case "barbarian": case "weaponsmith":
                h.Skin = new Color(0.9f, 0.7f, 0.55f); h.Shirt = new Color(0.5f, 0.35f, 0.2f); h.Hair = new Color(0.7f, 0.4f, 0.1f); break;
            case "bandit": case "bowyer":
                h.Shirt = d.Id == "bandit" ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.25f, 0.45f, 0.2f); h.Pants = new Color(0.2f, 0.18f, 0.15f); break;
            case "guard":
                h.Shirt = new Color(0.7f, 0.1f, 0.1f); h.Pants = new Color(0.3f, 0.3f, 0.32f); break;
            case "mossback_ogre":
                h.Skin = new Color(0.4f, 0.55f, 0.3f); h.Shirt = new Color(0.3f, 0.45f, 0.2f); h.Pants = new Color(0.3f, 0.25f, 0.15f); h.Hunched = true; break;
            default:
                h.Shirt = new Color(0.3f, 0.5f, 0.3f) * d.Tint; break;
        }
    }

    public void Apply(EntitySnap s, bool snap = false)
    {
        Hp = s.Hp; MaxHp = s.Max; TargetId = s.Tgt; CombatLevel = s.Cb;
        var nt = new Tile(s.X, s.Z);
        if (nt != Tile || snap)
        {
            var dest = map.TileCenter(nt);
            if (snap || nt.Chebyshev(Tile) > 3) { Position = from = to = dest; lerp = 1; }
            else { from = Position; to = dest; lerp = 0; moveSpeed = nt.Chebyshev(Tile) >= 2 ? 2f : 1f; }
            Tile = nt;
        }
        if (s.Dead != Dead)
        {
            Dead = s.Dead;
            Body.SetDead(Dead);
        }
        if (IsPlayer && s.Eq != null && (eq == null || string.Join(",", eq) != string.Join(",", s.Eq)))
        {
            eq = s.Eq;
            Body.Wear(eq);
        }
    }

    public void OnHit(int dmg, int hpAfter, string type)
    {
        Hp = hpAfter;
        HpShow = 6f;
        var col = dmg > 0 ? new Color(0.8f, 0.1f, 0.1f) : new Color(0.2f, 0.35f, 0.8f);
        Splats.Add(new Hitsplat { Damage = dmg, Color = col, Offset = (Splats.Count % 3 - 1) * 16 });
        if (dmg > 0 && !Dead) Body.Play("hit");
    }

    public void Say(string text) { Chat = text; ChatT = 5f; }

    public Vector3 Chest => GlobalPosition + new Vector3(0, VisualHeight * 0.6f, 0);
    public Vector3 Head => GlobalPosition + new Vector3(0, VisualHeight + 0.25f, 0);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool moving = lerp < 1f;
        if (moving)
        {
            lerp = Mathf.Min(1f, lerp + dt / GameConst.TickSeconds);
            var p = from.Lerp(to, lerp);
            p.Y = map.HeightAt(p.X, p.Z);
            if (map.Ground[Math.Clamp((int)p.X, 0, map.W - 1), Math.Clamp((int)p.Z, 0, map.H - 1)] == Ground.Water) p.Y = Mathf.Max(p.Y, 0.4f);
            Position = p;
        }
        Body.SetLocomotion(moving ? moveSpeed : 0f);

        // Facing: movement direction, else combat target.
        Vector3 dir = Vector3.Zero;
        if (moving) dir = to - from;
        else if (TargetId >= 0 && GameWorld.I != null && GameWorld.I.Views.TryGetValue(TargetId, out var tv) && tv != this)
            dir = tv.Position - Position;
        dir.Y = 0;
        if (dir.LengthSquared() > 0.001f && !Dead)
        {
            float want = Mathf.Atan2(dir.X, dir.Z);
            yaw = Mathf.LerpAngle(yaw, want, Mathf.Min(1f, dt * 10f));
        }
        Rotation = new Vector3(0, yaw, 0);

        if (HpShow > 0) HpShow -= dt;
        if (ChatT > 0) ChatT -= dt;
        for (int i = Splats.Count - 1; i >= 0; i--)
        {
            Splats[i].T += dt;
            if (Splats[i].T > 1.3f) Splats.RemoveAt(i);
        }
    }

    public void FaceToward(Vector3 p)
    {
        var d = p - Position;
        if (d.LengthSquared() > 0.001f) yaw = Mathf.Atan2(d.X, d.Z);
    }
}
