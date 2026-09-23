using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia.Client;

/// Common interface for everything that can represent an entity visually.
public abstract partial class BodyVisual : Node3D
{
    public float Height = 1.8f;
    protected float moveBlend;     // 0 idle .. 1 walking .. 2 running
    protected string action;
    protected float actionT, actionLen;
    protected bool dead;
    protected float deadT;

    public virtual void SetEquipment(string[] eq) { }
    /// Re-applies the current gear from scratch (after the fitting data changed).
    public virtual void Refit() { }
    public void SetLocomotion(float speed) => moveBlend = Mathf.Lerp(moveBlend, speed, 0.25f);

    // Equipment as worn, plus a tool briefly held while skilling (hatchet, pickaxe, rod...).
    string[] worn;
    string tool;
    float toolT;

    public void Wear(string[] eq)
    {
        worn = eq;
        SetEquipment(tool != null ? WithTool(eq) : eq);
    }

    string[] WithTool(string[] eq)
    {
        var e = (string[])(eq ?? new string[11]).Clone();
        e[(int)EquipSlot.Weapon] = tool;
        if (e.Length > (int)EquipSlot.Shield) e[(int)EquipSlot.Shield] = null;
        return e;
    }

    public void ShowTool(string item, float seconds)
    {
        toolT = seconds;
        if (tool == item) return;
        tool = item;
        SetEquipment(WithTool(worn));
    }

    protected void TickTool(double delta)
    {
        if (tool == null) return;
        toolT -= (float)delta;
        if (toolT > 0) return;
        tool = null;
        SetEquipment(worn ?? new string[11]);
    }
    public virtual void Play(string a)
    {
        action = a;
        actionT = 0;
        actionLen = a switch { "death" => 1.2f, "heavy" or "chop" or "mine" or "smith" => 0.8f, "cast" or "enchant" => 0.7f, "shoot" => 0.7f, "hit" => 0.35f, "pickup" or "fish" or "farm" or "cook" => 0.7f, "eat" => 0.8f, _ => 0.55f };
    }
    public virtual void SetDead(bool d)
    {
        if (d && !dead) { dead = true; deadT = 0; Play("death"); }
        else if (!d && dead) { dead = false; action = null; ResetPose(); }
    }
    protected virtual void ResetPose() { }

    /// Cuts a one-shot action short (e.g. the rock ran out mid-swing) and puts the tool away.
    public void StopAction()
    {
        if (dead) return;
        action = null;
        toolT = 0;
        TickTool(0);
        OnStopAction();
    }
    protected virtual void OnStopAction() { }
    public bool Dead => dead;
}

// =====================================================================================
// Procedural humanoid made of primitives with a simple joint hierarchy.
// =====================================================================================
public partial class ProceduralHumanoid : BodyVisual
{
    public Color Skin = new(0.87f, 0.68f, 0.52f), Shirt = new(0.55f, 0.45f, 0.3f), Pants = new(0.35f, 0.28f, 0.2f), Boots = new(0.25f, 0.17f, 0.1f), Hair = new(0.3f, 0.2f, 0.1f);
    public bool Bony, Hunched;
    Node3D hips, torso, head, armL, armR, foreL, foreR, handL, handR, legL, legR, shinL, shinR, capeNode, helmSlot, root;
    readonly List<MeshInstance3D> shirtParts = new(), pantsParts = new(), bootParts = new(), handParts = new();
    Node3D weaponNode, shieldNode, helmNode;
    float t;
    string[] lastEq;

    public void Build(float height)
    {
        Height = height;
        root = new Node3D();
        AddChild(root);
        root.Scale = Vector3.One * (height / 1.8f);
        var skin = Props.Mat(Skin);
        float limb = Bony ? 0.045f : 0.07f;

        hips = Node(root, new Vector3(0, 0.95f, 0));
        torso = Node(hips, new Vector3(0, 0.05f, 0));
        if (Hunched) torso.RotationDegrees = new Vector3(18, 0, 0);
        var chest = Props.Box(torso, new Vector3(Bony ? 0.34f : 0.46f, 0.55f, Bony ? 0.18f : 0.26f), new Vector3(0, 0.3f, 0), Props.Mat(Shirt));
        shirtParts.Add(chest);
        var belt = Props.Box(torso, new Vector3(Bony ? 0.34f : 0.47f, 0.08f, Bony ? 0.2f : 0.27f), new Vector3(0, 0.04f, 0), Props.Mat(new Color(0.2f, 0.13f, 0.07f)));
        head = Node(torso, new Vector3(0, 0.62f, 0));
        Props.Sphere(head, 0.15f, new Vector3(0, 0.14f, 0), Bony ? Props.Mat(new Color(0.9f, 0.88f, 0.8f)) : skin, new Vector3(0.95f, 1.1f, 1f), 12);
        var eye = Props.Mat(Bony ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.1f, 0.08f, 0.06f));
        Props.Sphere(head, 0.025f, new Vector3(-0.055f, 0.16f, 0.13f), eye, null, 6);
        Props.Sphere(head, 0.025f, new Vector3(0.055f, 0.16f, 0.13f), eye, null, 6);
        if (!Bony) Props.Sphere(head, 0.155f, new Vector3(0, 0.2f, -0.02f), Props.Mat(Hair), new Vector3(1f, 0.7f, 1.05f), 10);
        helmSlot = Node(head, Vector3.Zero);

        (armL, foreL, handL) = Arm(torso, -1, limb, skin);
        (armR, foreR, handR) = Arm(torso, 1, limb, skin);
        (legL, shinL) = Leg(hips, -1, limb * 1.2f);
        (legR, shinR) = Leg(hips, 1, limb * 1.2f);
        capeNode = Node(torso, new Vector3(0, 0.56f, -0.15f));
        ResetPose();
    }

    static Node3D Node(Node3D parent, Vector3 pos)
    {
        var n = new Node3D { Position = pos };
        parent.AddChild(n);
        return n;
    }

    (Node3D, Node3D, Node3D) Arm(Node3D torso, int side, float r, Material skin)
    {
        var sh = Node(torso, new Vector3(side * (Bony ? 0.21f : 0.29f), 0.52f, 0));
        var up = Props.Cyl(sh, r, r, 0.32f, new Vector3(0, -0.16f, 0), Props.Mat(Shirt), 6);
        shirtParts.Add(up);
        var el = Node(sh, new Vector3(0, -0.32f, 0));
        Props.Cyl(el, r * 0.9f, r * 0.85f, 0.28f, new Vector3(0, -0.14f, 0), skin, 6);
        var hand = Node(el, new Vector3(0, -0.3f, 0));
        handParts.Add(Props.Sphere(hand, r * 1.05f, Vector3.Zero, skin, null, 6));
        return (sh, el, hand);
    }

    (Node3D, Node3D) Leg(Node3D hips, int side, float r)
    {
        var hip = Node(hips, new Vector3(side * 0.12f, 0, 0));
        pantsParts.Add(Props.Cyl(hip, r, r * 0.9f, 0.45f, new Vector3(0, -0.22f, 0), Props.Mat(Pants), 6));
        var knee = Node(hip, new Vector3(0, -0.45f, 0));
        pantsParts.Add(Props.Cyl(knee, r * 0.9f, r * 0.8f, 0.42f, new Vector3(0, -0.21f, 0), Props.Mat(Pants), 6));
        bootParts.Add(Props.Box(knee, new Vector3(r * 2.2f, 0.1f, 0.24f), new Vector3(0, -0.45f, 0.05f), Props.Mat(Boots)));
        return (hip, knee);
    }

    public override void SetEquipment(string[] eq)
    {
        if (eq == null || (lastEq != null && string.Join(",", eq) == string.Join(",", lastEq))) return;
        lastEq = (string[])eq.Clone();
        ItemDef Slot(EquipSlot s) => ItemDb.Get(eq.Length > (int)s ? eq[(int)s] : null);

        var body = Slot(EquipSlot.Body);
        var bodyMat = body == null ? Props.Mat(Shirt) : body.TintModel || body.Icon == IconKind.Platebody ? Props.Mat(body.Tint, 0.35f, 0.75f) : Props.Mat(body.Tint);
        if (body != null && body.Icon == IconKind.Platebody) bodyMat = Props.Mat(body.Tint, 0.35f, 0.75f);
        foreach (var p in shirtParts) p.MaterialOverride = bodyMat;
        var legs = Slot(EquipSlot.Legs);
        var legMat = legs == null ? Props.Mat(Pants) : legs.Icon == IconKind.Platelegs ? Props.Mat(legs.Tint, 0.35f, 0.75f) : Props.Mat(legs.Tint);
        foreach (var p in pantsParts) p.MaterialOverride = legMat;
        var feet = Slot(EquipSlot.Feet);
        foreach (var p in bootParts) p.MaterialOverride = Props.Mat(feet?.Tint ?? Boots);
        var hands = Slot(EquipSlot.Hands);
        if (hands != null) foreach (var p in handParts) p.MaterialOverride = Props.Mat(hands.Tint);

        weaponNode?.QueueFree(); weaponNode = null;
        shieldNode?.QueueFree(); shieldNode = null;
        helmNode?.QueueFree(); helmNode = null;
        foreach (var c in capeNode.GetChildren()) c.QueueFree();

        var w = Slot(EquipSlot.Weapon);
        if (w != null)
        {
            weaponNode = ItemVisuals.Build(w, ItemVisuals.HeldLength(w));
            if (w.Icon is IconKind.Shortbow or IconKind.Longbow)
            {
                // Bows go in the left hand, held vertically.
                weaponNode.Position = new Vector3(0, -ItemVisuals.HeldLength(w) * 0.5f, 0);
                var holder = new Node3D();
                holder.AddChild(weaponNode);
                handL.AddChild(holder);
                weaponNode = holder;
            }
            else
            {
                weaponNode.RotationDegrees = new Vector3(90, 0, 0);
                weaponNode.Position = new Vector3(0, 0, -0.08f);
                var holder = new Node3D();
                holder.AddChild(weaponNode);
                handR.AddChild(holder);
                weaponNode = holder;
            }
        }
        var sh = Slot(EquipSlot.Shield);
        if (sh != null)
        {
            shieldNode = ItemVisuals.Build(sh, ItemVisuals.HeldLength(sh));
            shieldNode.Position = new Vector3(-0.08f, -0.35f, 0.02f);
            shieldNode.RotationDegrees = new Vector3(0, -90, 0);
            foreL.AddChild(shieldNode);
        }
        var hd = Slot(EquipSlot.Head);
        if (hd != null)
        {
            helmNode = ItemVisuals.Build(hd, ItemVisuals.HeldLength(hd));
            helmNode.Position = new Vector3(0, hd.Icon == IconKind.WizardHat ? 0.22f : 0.0f, 0);
            helmSlot.AddChild(helmNode);
        }
        var cape = Slot(EquipSlot.Cape);
        if (cape != null)
        {
            var c = Props.Box(capeNode, new Vector3(0.5f, 1.05f, 0.03f), new Vector3(0, -0.52f, 0), Props.Mat(cape.Tint));
            capeNode.RotationDegrees = new Vector3(8, 0, 0);
        }
    }

    protected override void ResetPose()
    {
        if (root == null) return;
        root.Rotation = Vector3.Zero;
        root.Position = Vector3.Zero;
        foreach (var n in new[] { armL, armR, foreL, foreR, legL, legR, shinL, shinR, head })
            n.Rotation = Vector3.Zero;
        torso.RotationDegrees = new Vector3(Hunched ? 18 : 0, 0, 0);
        armL.RotationDegrees = new Vector3(0, 0, -6);
        armR.RotationDegrees = new Vector3(0, 0, 6);
    }

    public override void _Process(double delta)
    {
        if (root == null) return;
        TickTool(delta);
        float dt = (float)delta;
        t += dt;
        if (dead)
        {
            deadT += dt;
            float k = Mathf.Clamp(deadT / 0.6f, 0, 1);
            root.RotationDegrees = new Vector3(-85 * k * k, 0, 0);
            root.Position = new Vector3(0, 0.05f * k, -0.2f * k);
            return;
        }
        // locomotion
        float speed = moveBlend;
        float freq = speed > 1.2f ? 11f : 7f;
        float amp = Mathf.Clamp(speed, 0, 1) * (speed > 1.2f ? 45f : 30f);
        float s = Mathf.Sin(t * freq);
        legL.RotationDegrees = new Vector3(s * amp, 0, 0);
        legR.RotationDegrees = new Vector3(-s * amp, 0, 0);
        shinL.RotationDegrees = new Vector3(Mathf.Max(0, -s) * amp * 0.9f, 0, 0);
        shinR.RotationDegrees = new Vector3(Mathf.Max(0, s) * amp * 0.9f, 0, 0);
        armL.RotationDegrees = new Vector3(-s * amp * 0.8f, 0, -6);
        armR.RotationDegrees = new Vector3(s * amp * 0.8f, 0, 6);
        foreL.RotationDegrees = new Vector3(-15 * Mathf.Clamp(speed, 0, 1) - 5, 0, 0);
        foreR.RotationDegrees = new Vector3(-15 * Mathf.Clamp(speed, 0, 1) - 5, 0, 0);
        hips.Position = new Vector3(0, 0.95f + Mathf.Abs(Mathf.Sin(t * freq)) * 0.04f * Mathf.Clamp(speed, 0, 1) + Mathf.Sin(t * 1.8f) * 0.006f, 0);
        torso.RotationDegrees = new Vector3((Hunched ? 18 : 0) + (speed > 1.2f ? 10 : 0), 0, 0);
        capeNode.RotationDegrees = new Vector3(8 + speed * 14 + Mathf.Sin(t * 3f) * 3, 0, 0);

        if (action != null)
        {
            actionT += dt;
            float p = Mathf.Clamp(actionT / actionLen, 0, 1);
            float swing = Mathf.Sin(p * Mathf.Pi);
            switch (action)
            {
                case "slash":
                    armR.RotationDegrees = new Vector3(Mathf.Lerp(-150, 30, p), 0, Mathf.Lerp(30, -10, p));
                    foreR.RotationDegrees = new Vector3(-30 * swing, 0, 0);
                    torso.RotationDegrees = new Vector3(0, Mathf.Lerp(25, -20, p), 0);
                    break;
                case "stab":
                    armR.RotationDegrees = new Vector3(-80 * swing, 0, 0);
                    foreR.RotationDegrees = new Vector3(-60 * (1 - swing), 0, 0);
                    torso.RotationDegrees = new Vector3(8 * swing, 0, 0);
                    break;
                case "heavy":
                    armR.RotationDegrees = new Vector3(Mathf.Lerp(-170, 10, p * p), 0, 0);
                    armL.RotationDegrees = new Vector3(Mathf.Lerp(-170, 10, p * p), 0, 0);
                    torso.RotationDegrees = new Vector3(Mathf.Lerp(-10, 25, p * p), 0, 0);
                    break;
                case "shoot":
                    armL.RotationDegrees = new Vector3(-90, 0, 0);
                    foreL.RotationDegrees = Vector3.Zero;
                    armR.RotationDegrees = new Vector3(-90, 0, 0);
                    foreR.RotationDegrees = new Vector3(-100 * swing, 0, 0);
                    torso.RotationDegrees = new Vector3(0, -15, 0);
                    break;
                case "cast":
                    armL.RotationDegrees = new Vector3(-100 * swing, 0, -20);
                    armR.RotationDegrees = new Vector3(-100 * swing, 0, 20);
                    head.RotationDegrees = new Vector3(-10 * swing, 0, 0);
                    break;
                case "bite":
                case "hit":
                    torso.RotationDegrees = new Vector3(-12 * swing, 0, 0);
                    head.RotationDegrees = new Vector3(-15 * swing, 0, 0);
                    break;
                case "pickup":
                    torso.RotationDegrees = new Vector3(55 * swing, 0, 0);
                    armR.RotationDegrees = new Vector3(-40 * swing, 0, 0);
                    break;
                case "eat":
                    armR.RotationDegrees = new Vector3(-60 * swing, 0, 25 * swing);
                    foreR.RotationDegrees = new Vector3(-110 * swing, 0, 0);
                    break;
            }
            if (p >= 1) { action = null; head.Rotation = Vector3.Zero; }
        }
    }
}

// =====================================================================================
// Rigged Higgsfield/Meshy humanoid driven by the shared animation library.
// =====================================================================================
public partial class RiggedHumanoid : BodyVisual
{
    Node3D model;
    Skeleton3D skel;
    AnimationPlayer anim;
    string loco = "";
    BoneAttachment3D rightHand, leftHand, headAtt, chestAtt, foreArmL;
    readonly List<Node3D> attached = new();
    string[] lastEq;
    float t;
    public Vector3? WeaponRotOverride, WeaponOffOverride;
    HandRig.Hand handL, handR;
    HoldPose holdL, holdR;
    bool gripL, gripR, shieldL, debugLoco;
    public bool? BowRightOverride;

    /// Debug: freeze the rig at a point in a clip.
    public void PoseAt(string clip, float fraction)
    {
        if (anim == null || !anim.HasAnimation(clip)) return;
        anim.Play(clip);
        anim.Seek(anim.GetAnimation(clip).Length * fraction, true);
        anim.Pause();
        action = "debug"; actionLen = 1e9f; loco = "debug";
        debugLoco = clip is "idle" or "walk" or "run";
    }

    /// Debug: held weapon handle direction in model space (+Z forward).
    public Vector3 DebugWeaponDir() => attached.Count == 0 ? Vector3.Zero
        : (model.GlobalTransform.Basis.Inverse() * attached[0].GlobalTransform.Basis.Y).Normalized();

    public static RiggedHumanoid TryCreate(string modelKey, float height, Color tint)
    {
        if (!Assets.HasModel(modelKey)) return null;
        var m = Assets.Model(modelKey, height);
        if (m == null) return null;
        var sk = Assets.Find<Skeleton3D>(m);
        if (sk == null) { m.Free(); return null; }
        var r = new RiggedHumanoid { model = m, skel = sk, Height = height, modelKey = modelKey };
        r.AddChild(m);
        if (tint != Colors.White) Assets.Modulate(m, tint);
        // Imported clips inside the character GLB are ignored; shared clips are retargeted in.
        foreach (var ap in FindAll<AnimationPlayer>(m)) ap.Stop();
        (r.handL, r.handR) = HandRig.Install(modelKey, m, sk);
        var skelToModel = Quaternion.Identity;
        for (Node n = sk; n != null && n != m; n = n.GetParent())
            if (n is Node3D n3) skelToModel = n3.Quaternion * skelToModel;
        HoldPose MakeHold(string side, HandRig.Hand h)
        {
            if (h == null) return null;
            var hp = new HoldPose
            {
                Name = side + "Hold", Arm = sk.FindBone(side + "Arm"), ForeArm = sk.FindBone(side + "ForeArm"), Hand = sk.FindBone(side + "Hand"),
                GripAxis = h.GripAxis, ModelToSkel = skelToModel.Inverse(),
            };
            sk.AddChild(hp);
            hp.Follower = new Node3D { Name = side + "HandAnchor" };
            sk.AddChild(hp.Follower);
            return hp;
        }
        r.holdL = MakeHold("Left", r.handL);
        r.holdR = MakeHold("Right", r.handR);
        r.anim = AnimLib.Attach(m, sk, modelKey);
        r.SetupAttachments();
        return r;
    }

    static IEnumerable<T> FindAll<T>(Node n) where T : class
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren()) foreach (var x in FindAll<T>(c)) yield return x;
    }

    BoneAttachment3D Attach(params string[] patterns)
    {
        int b = AnimLib.FindBone(skel, patterns);
        if (b < 0) return null;
        var a = new BoneAttachment3D { BoneName = skel.GetBoneName(b) };
        skel.AddChild(a);
        return a;
    }

    void SetupAttachments()
    {
        rightHand = Attach("righthand", "handr", "rhand");
        leftHand = Attach("lefthand", "handl", "lhand");
        headAtt = Attach("head");
        foreArmL = Attach("leftforearm", "forearml", "lforearm");
        // Meshy rigs: Spine02 (lowest) -> Spine01 -> Spine (upper chest).
        int upper = skel.FindBone("Spine");
        if (upper >= 0) { chestAtt = new BoneAttachment3D { BoneName = "Spine" }; skel.AddChild(chestAtt); }
        else chestAtt = Attach("chest", "upperchest", "spine");
    }

    /// Hand items hang off the hold-pose follower when there is one: it tracks the hand after the
    /// hold pose has bent the arm, which a BoneAttachment3D does not see.
    Node3D Anchor(BoneAttachment3D at) =>
        (at == rightHand ? holdR?.Follower : at == leftHand ? holdL?.Follower : null) ?? (Node3D)at;

    // Skeleton space is scaled by the model normalisation; compensate so items keep world size.
    float WorldToSkel()
    {
        var s = skel.GlobalTransform.Basis.Scale;
        return s.X > 0.0001f ? 1f / s.X : 1f;
    }

    public override void SetEquipment(string[] eq)
    {
        if (eq == null || (lastEq != null && string.Join(",", eq) == string.Join(",", lastEq))) return;
        if (!IsInsideTree()) { CallDeferred(MethodName.SetEquipmentDeferred, eq); return; }
        lastEq = (string[])eq.Clone();
        foreach (var n in attached) n.QueueFree();
        attached.Clear();
        float k = WorldToSkel();
        ItemDef Slot(EquipSlot s) => ItemDb.Get(eq.Length > (int)s ? eq[(int)s] : null);

        // Skeleton orientation inside the model (Armature node rotation etc).
        var qs = Quaternion.Identity;
        for (Node n = skel; n != null && n != model; n = n.GetParent())
            if (n is Node3D n3) qs = n3.Quaternion * qs;

        // Attach `d` to a bone so that, at rest, it has `rotDeg` orientation and `offset` (metres) in model space.
        void Put(BoneAttachment3D at, ItemDef d, Vector3 offset, Vector3 rotDeg, float gripAlong = 0f, float lenMul = 1f, float palm = 0f)
        {
            if (at == null || d == null) return;
            int bone = skel.FindBone(at.BoneName);
            var qb = skel.GetBoneGlobalRest(bone).Basis.Orthonormalized().GetRotationQuaternion();
            var inv = (qs * qb).Inverse();
            if (palm > 0)
            {
                // Hand bones sit at the wrist; move the grip into the fist along the forearm->hand direction.
                int parent = skel.GetBoneParent(bone);
                if (parent >= 0)
                {
                    var dirSkel = (skel.GetBoneGlobalRest(bone).Origin - skel.GetBoneGlobalRest(parent).Origin).Normalized();
                    offset += (qs * dirSkel) * palm;
                }
            }
            float len = ItemVisuals.HeldLength(d) * lenMul;
            var item = ItemVisuals.Build(d, len);
            item.Position = new Vector3(0, -len * gripAlong, 0);
            var holder = new Node3D();
            holder.AddChild(item);
            holder.Quaternion = inv * Quaternion.FromEuler(rotDeg * (Mathf.Pi / 180f));
            holder.Position = inv * (offset * k);
            holder.Scale = Vector3.One * k;
            Anchor(at).AddChild(holder);
            attached.Add(holder);
        }

        gripL = gripR = false;
        holdL?.SetHold(null, 0);
        holdR?.SetHold(null, 0);
        // Put a held item inside a closed fist: the handle runs across the palm (GripAxis) and the
        // blade/limb comes out of the thumb side, following the hand's own geometry.
        bool PutFist(BoneAttachment3D at, HandRig.Hand hand, ItemDef d, float gripAlong, float lenMul = 1f)
        {
            if (at == null || hand == null || d == null) return false;
            float len = ItemVisuals.HeldLength(d) * lenMul;
            var item = ItemVisuals.Build(d, len, gripAlong);
            item.Position = new Vector3(0, -len * gripAlong, 0);
            var y = hand.GripAxis;
            var z = (hand.FingerDir - y * hand.FingerDir.Dot(y)).Normalized();
            var x = y.Cross(z).Normalized();
            var holder = new Node3D { Transform = new Transform3D(new Basis(x, y, z).Scaled(Vector3.One * k), hand.GripPoint) };
            holder.AddChild(item);
            Anchor(at).AddChild(holder);
            attached.Add(holder);
            return true;
        }

        var w = Slot(EquipSlot.Weapon);
        if (w != null)
        {
            bool bow = w.Icon is IconKind.Shortbow or IconKind.Longbow;
            bool staff = w.Icon is IconKind.Staff or IconKind.Battlestaff;
            var useLeft = bow && BowRightOverride == false;
            if (PutFist(useLeft ? leftHand : rightHand, useLeft ? handL : handR, w, bow ? 0.5f : staff ? 0.4f : 0.1f, staff ? 0.85f : 1f))
            {
                if (useLeft) gripL = true; else gripR = true;
                // Resting carry pose while idle/walking (model space: +Z forward, +X the character's left).
                float s = useLeft ? 1f : -1f;
                var carry = EquipTuning.T.CarryFor(bow ? "bow" : staff ? "staff" : w.TwoHanded ? "twoHanded" : "oneHanded");
                var dir = new Vector3(carry.Dir.X * s, carry.Dir.Y, carry.Dir.Z);
                (useLeft ? holdL : holdR)?.SetHold(dir, carry.Elbow);
                w = null;
            }
        }
        if (w != null)
        {
            // Fallback for rigs without hand bones found: model-space placement.
            // The archery clip extends the right arm with the bow and draws with the left.
            if (w.Icon is IconKind.Shortbow or IconKind.Longbow)
                Put(BowRightOverride == false ? leftHand : rightHand, w, new Vector3(0, 0, 0.02f), new Vector3(0, 90, 0), 0.5f, 1f, 0.03f);
            else if (w.Icon is IconKind.Staff or IconKind.Battlestaff)
                Put(rightHand, w, new Vector3(0, 0, 0.02f), new Vector3(8, 0, 0), 0.4f, 0.85f, 0.03f);
            else
                Put(rightHand, w, WeaponOffOverride ?? EquipTuning.T.Fallback.WeaponOffset, WeaponRotOverride ?? EquipTuning.T.Fallback.WeaponRotation, 0.1f, 1f, 0.03f);
        }
        // Frames measured from this rig's own rest skeleton (skeleton space): model up / forward.
        var up = (qs.Inverse() * Vector3.Up).Normalized();
        var fwd = (qs.Inverse() * Vector3.Back).Normalized();

        // Place `d` on bone `at` with its up axis along `y`, its face along `z`, base at `pos` (skeleton space).
        void PutFramed(BoneAttachment3D at, ItemDef d, float lenMeters, Vector3 y, Vector3 z, Vector3 pos, float centre = 0f)
        {
            if (at == null || d == null) return;
            var rest = skel.GetBoneGlobalRest(skel.FindBone(at.BoneName));
            var item = ItemVisuals.Build(d, lenMeters);
            item.Position = new Vector3(0, -lenMeters * centre, 0);
            y = y.Normalized();
            z = (z - y * z.Dot(y)).Normalized();
            var basis = new Basis(y.Cross(z), y, z).Scaled(Vector3.One * k);
            var holder = new Node3D { Transform = rest.AffineInverse() * new Transform3D(basis, pos) };
            holder.AddChild(item);
            Anchor(at).AddChild(holder);
            attached.Add(holder);
        }

        // Shields: the left elbow bends so the forearm comes forward, and the shield rides on its outer
        // side, upright and facing mostly forward (HoldPose keeps it there as the arm animates).
        var shield = Slot(EquipSlot.Shield);
        shieldL = false;
        if (holdL != null) holdL.ForeItem = null;
        if (shield != null && holdL != null && !gripL)
        {
            float len = ItemVisuals.HeldLength(shield);
            var item = ItemVisuals.Build(shield, len);
            item.Position = new Vector3(0, -len * 0.5f, 0);
            var holder = new Node3D { Name = "ShieldHolder" };
            holder.AddChild(item);
            skel.AddChild(holder);
            attached.Add(holder);
            holdL.ForeItem = holder;
            holdL.ForeScale = k;
            holdL.ForeAlong = EquipTuning.T.Shield.Along;
            holdL.ForeOut = EquipTuning.T.Shield.Out;
            holdL.ForeFace = EquipTuning.T.Shield.Face;
            holdL.ForeAhead = EquipTuning.T.Shield.Ahead;
            holdL.SetHold(Vector3.Back, EquipTuning.T.Shield.Elbow, false);
            shieldL = true;
        }
        else
        {
            int fa = foreArmL != null ? skel.FindBone(foreArmL.BoneName) : -1, lh = skel.FindBone(leftHand?.BoneName ?? "");
            if (shield != null && fa >= 0 && lh >= 0)
            {
                var elbow = skel.GetBoneGlobalRest(fa).Origin;
                var wrist = skel.GetBoneGlobalRest(lh).Origin;
                var dir = (wrist - elbow).Normalized();
                var outward = fwd.Cross(dir).Normalized();
                float len = ItemVisuals.HeldLength(shield);
                PutFramed(foreArmL, shield, len, -dir, outward, elbow.Lerp(wrist, 0.55f) + outward * (0.06f * k), 0.5f);
            }
            else Put(leftHand, shield, EquipTuning.T.Fallback.ShieldOffset, EquipTuning.T.Fallback.ShieldRotation, 0.5f, 1f, 0.04f);
        }

        // Headgear is fitted to this character's actual head: the skinned vertices driven by the head
        // bone give its extent along the rig's up / forward / side axes, so helms cover the whole skull
        // (hair and ears included) on every body model instead of guessing from standing height.
        var hd = Slot(EquipSlot.Head);
        int headB = headAtt != null ? skel.FindBone(headAtt.BoneName) : -1;
        var side = fwd.Cross(up).Normalized();
        var hb = headB >= 0 ? HeadBox(headB, up, fwd, side) : null;
        if (hd != null && hb is var (lo, hi))
        {
            // lo/hi: (side, up, fwd) extents in skeleton units; convert to metres for sizing.
            var size = (hi - lo) / k;
            var mid = (lo + hi) * 0.5f;
            var raw = Assets.ModelBounds(hd.Model).Size;        // model faces +X: X depth, Y height, Z width
            if (raw.Y < 0.0001f) { ItemVisuals.Build(hd, 1f).Free(); raw = Assets.ModelBounds(hd.Model).Size; }
            float hh = size.Y;
            Vector3 fit;   // target (width, height, depth) in metres
            float top, back;
            switch (hd.Icon)
            {
                case IconKind.WizardHat:
                {
                    // Keep the hat's own shape; the brim spans ~2.3 heads and sits on the brow.
                    float s = size.X * EquipTuning.T.Headgear.HatBrim / Mathf.Max(raw.X, raw.Z);
                    fit = raw * s;
                    fit = new Vector3(fit.Z, fit.Y, fit.X);
                    top = hi.Y / k - hh * EquipTuning.T.Headgear.HatSink + fit.Y;
                    back = 0f;
                    break;
                }
                case IconKind.Coif:
                    fit = new Vector3(size.X * EquipTuning.T.Headgear.HoodW, hh * EquipTuning.T.Headgear.HoodH, size.Z * EquipTuning.T.Headgear.HoodD);
                    top = hi.Y / k + hh * EquipTuning.T.Headgear.HoodAbove;
                    back = size.Z * EquipTuning.T.Headgear.HoodBack;
                    break;
                default:
                    fit = new Vector3(size.X * EquipTuning.T.Headgear.HelmW, hh * EquipTuning.T.Headgear.HelmH, size.Z * EquipTuning.T.Headgear.HelmD);
                    top = hi.Y / k + hh * EquipTuning.T.Headgear.Above;
                    back = size.Z * EquipTuning.T.Headgear.HelmBack;
                    break;
            }
            var basePos = side * mid.X + up * ((top - fit.Y) * k) + fwd * (mid.Z - back * k);
            PutFramed(headAtt, hd, fit.Y, up, side, basePos);
            // PutFramed scales uniformly to the height; stretch width (item Z) and depth (item X) to fit.
            var item = attached[^1].GetChild<Node3D>(0);
            float sy = fit.Y / raw.Y;
            item.Scale = new Vector3(fit.Z / (raw.X * sy), 1f, fit.X / (raw.Z * sy));
        }
        else if (hd != null) Put(headAtt, hd, EquipTuning.T.Fallback.HelmOffset, Vector3.Zero, 0f, 1.15f);

        // Pendants rest against the upper chest: find the shirt's front surface at that height.
        var neckItem = Slot(EquipSlot.Neck);
        if (neckItem != null && chestAtt != null && headB >= 0)
        {
            var neck = skel.GetBoneGlobalRest(headB).Origin;
            var pt = EquipTuning.T.Pendant;
            float y = neck.Dot(up) - pt.Drop * Height * k, sx = neck.Dot(side);
            float front = FrontAt(y, sx, 0.025f * k) ?? neck.Dot(fwd) + 0.1f * k;
            float len = pt.Length;
            var p = side * sx + up * (y - len * 0.5f * k) + fwd * (front + pt.Gap * k);
            PutFramed(chestAtt, neckItem, len, up, side, p);   // model faces +X, like the headgear
        }
        var cape = Slot(EquipSlot.Cape);
        if (cape != null)
        {
            Put(chestAtt, cape, EquipTuning.T.Cape.Offset, EquipTuning.T.Cape.Rotation, 0f);
            // Narrower, and hung a little further back, so the flared hem doesn't wrap in front of the legs.
            if (attached.Count > 0) attached[^1].Scale = EquipTuning.T.Cape.Scale * k;
        }

        // Body, legs and boots: the matching part of a rigged outfit, moved onto this skeleton (ArmourFit).
        // The skin it covers is hidden; anything that can't be dressed falls back to tinting that band.
        ItemDef bodyD = Slot(EquipSlot.Body), legsD = Slot(EquipSlot.Legs), feetD = Slot(EquipSlot.Feet);
        var covered = new HashSet<ArmourFit.Region>();
        bool Dress(ItemDef it, ArmourFit.Region? region = null)
        {
            var mi = ArmourFit.Attach(modelKey, model, skel, it, region);
            if (mi == null) return false;
            attached.Add(mi);
            covered.Add(region ?? ArmourFit.RegionOf(it).Value);
            return true;
        }
        bool robeOn = bodyD?.Icon == IconKind.Robe && Dress(bodyD);
        bool topOn = robeOn || bodyD != null && Dress(bodyD);
        if (topOn) bodyD = null;
        // A robe already has its skirt; a skirt without a body piece goes on over the tunic from the belt.
        if (legsD != null && (robeOn && legsD.Icon == IconKind.RobeBottom
            || Dress(legsD, legsD.Icon == IconKind.RobeBottom && !topOn ? ArmourFit.Region.Skirt : null))) legsD = null;
        if (feetD != null && Dress(feetD)) feetD = null;
        ArmourFit.HideCovered(modelKey, skel, ArmourFit.MainBody(model), covered);
        ApplyArmourOverlay(bodyD, legsD, feetD, Slot(EquipSlot.Hands), covered);
    }

    void SetEquipmentDeferred(string[] eq) => SetEquipment(eq);

    public override void Refit()
    {
        var eq = lastEq;
        lastEq = null;
        if (eq != null) SetEquipment(eq);
    }

    string modelKey;
    static readonly Dictionary<string, (Vector3, Vector3)?> headBoxes = new();
    static readonly Dictionary<string, List<Vector3>> restPoints = new();

    /// Extent of the head mesh at rest in skeleton units, as (side, up, fwd) min/max: every skinned
    /// vertex mostly weighted to the head bone or its children (head end, jaw, eyes...).
    (Vector3, Vector3)? HeadBox(int headB, Vector3 up, Vector3 fwd, Vector3 side)
    {
        if (modelKey != null && headBoxes.TryGetValue(modelKey, out var cached)) return cached;
        var inHead = new bool[skel.GetBoneCount()];
        for (int b = 0; b < inHead.Length; b++)
            for (int p = b; p >= 0; p = skel.GetBoneParent(p))
                if (p == headB) { inHead[b] = true; break; }
        Vector3 lo = Vector3.Inf, hi = -Vector3.Inf;
        int n = 0;
        var pts = new List<Vector3>();
        foreach (var mi in Assets.AllMeshes(model))
        {
            if (mi.Skin == null || mi.Mesh is not ArrayMesh am) continue;
            var skin = mi.Skin;
            var bindBone = new int[skin.GetBindCount()];
            var bindXf = new Transform3D[bindBone.Length];
            for (int i = 0; i < bindBone.Length; i++)
            {
                int sb = skin.GetBindBone(i);
                if (sb < 0) sb = skel.FindBone(skin.GetBindName(i));
                bindBone[i] = sb;
                bindXf[i] = sb >= 0 ? skel.GetBoneGlobalRest(sb) * skin.GetBindPose(i) : Transform3D.Identity;
            }
            for (int s = 0; s < am.GetSurfaceCount(); s++)
            {
                var arr = am.SurfaceGetArrays(s);
                var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                if (verts.Length == 0 || bones.Length < verts.Length) continue;
                int per = bones.Length / verts.Length;
                for (int v = 0; v < verts.Length; v++)
                {
                    float wHead = 0, wBest = -1; int best = -1;
                    for (int j = 0; j < per; j++)
                    {
                        int bi = bones[v * per + j]; float w = weights[v * per + j];
                        if (bi < 0 || bi >= bindBone.Length) continue;
                        if (bindBone[bi] >= 0 && inHead[bindBone[bi]]) wHead += w;
                        if (w > wBest) { wBest = w; best = bi; }
                    }
                    if (best < 0) continue;
                    var p = bindXf[best] * verts[v];
                    var q = new Vector3(p.Dot(side), p.Dot(up), p.Dot(fwd));
                    pts.Add(q);
                    if (wHead < 0.5f) continue;
                    lo = lo.Min(q); hi = hi.Max(q); n++;
                }
            }
        }
        // Horns, crests and plumes stretch the box: a skull is about as wide as it is deep and a
        // little taller, so clamp width and height (from the chin up) to the depth.
        if (n > 20)
        {
            float d = hi.Z - lo.Z, cx = (lo.X + hi.X) * 0.5f;
            float w = Mathf.Min(hi.X - lo.X, d * 0.95f), h = Mathf.Min(hi.Y - lo.Y, d * 1.15f);
            lo.X = cx - w * 0.5f; hi.X = cx + w * 0.5f; hi.Y = lo.Y + h;
        }
        (Vector3, Vector3)? box = n > 20 ? (lo, hi) : null;
        if (modelKey != null) { headBoxes[modelKey] = box; restPoints[modelKey] = pts; }
        else lastPoints = pts;
        return box;
    }

    List<Vector3> lastPoints;

    /// Front surface of the body (fwd, skeleton units) near a height and side offset, from the rest mesh.
    float? FrontAt(float upY, float sideX, float tol)
    {
        var pts = modelKey != null && restPoints.TryGetValue(modelKey, out var c) ? c : lastPoints;
        if (pts == null) return null;
        float best = float.MinValue;
        foreach (var q in pts)
            if (Mathf.Abs(q.Y - upY) < tol && Mathf.Abs(q.X - sideX) < tol * 1.5f && q.Z > best) best = q.Z;
        return best > float.MinValue ? best : null;
    }

    const string ArmourShader = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, depth_test_default, cull_back;
uniform vec3 up_axis = vec3(0.0, 1.0, 0.0);
uniform float hmin = 0.0;
uniform float hmax = 1.0;
uniform vec4 body_col = vec4(0.0);
uniform float body_metal = 0.0;
uniform vec4 legs_col = vec4(0.0);
uniform float legs_metal = 0.0;
uniform vec4 feet_col = vec4(0.0);
uniform vec4 hand_col = vec4(0.0);
uniform int hand_bones[24];
uniform int hand_count = 0;
varying float hn;
varying float hw;
void vertex() {
    hn = (dot(VERTEX, up_axis) - hmin) / max(hmax - hmin, 0.0001);
    hw = 0.0;
    for (int i = 0; i < 4; i++)
        for (int j = 0; j < hand_count; j++)
            if (int(BONE_INDICES[i]) == hand_bones[j]) hw += BONE_WEIGHTS[i];
}
void fragment() {
    float hand = smoothstep(0.35, 0.6, hw);
    float body = smoothstep(0.44, 0.48, hn) * (1.0 - smoothstep(0.80, 0.84, hn)) * (1.0 - hand);
    float legs = smoothstep(0.06, 0.09, hn) * (1.0 - smoothstep(0.46, 0.50, hn)) * (1.0 - hand);
    float feet = 1.0 - smoothstep(0.06, 0.09, hn);
    vec4 c = body_col * body + legs_col * legs + feet_col * feet + hand_col * hand;
    ALBEDO = c.rgb / max(c.a, 0.0001);
    ALPHA = c.a;
    METALLIC = body_metal * body + legs_metal * legs;
    ROUGHNESS = 0.4;
}";

    static Shader armourShader;

    /// Tints torso / legs / feet bands of the base mesh to show equipped armour.
    void ApplyArmourOverlay(ItemDef body, ItemDef legs, ItemDef feet, ItemDef hands, HashSet<ArmourFit.Region> covered)
    {
        static (Color c, float metal) Look(ItemDef d, bool plate) =>
            d == null ? (new Color(0, 0, 0, 0), 0f) : (new Color(d.Tint, plate ? 0.9f : 0.7f), plate ? 0.7f : 0f);
        var (bc, bm) = Look(body, body?.Icon == IconKind.Platebody);
        var (lc, lm) = Look(legs, legs?.Icon == IconKind.Platelegs);
        var (fc, _) = Look(feet, false);
        var (hc, _) = Look(hands, false);
        if (hands != null) hc.A = 0.92f;
        bool any = body != null || legs != null || feet != null || hands != null;
        // Gloves follow the skin weights: every bone from the wrists down.
        var handBone = new bool[skel.GetBoneCount()];
        foreach (var hn in new[] { leftHand?.BoneName, rightHand?.BoneName })
        {
            int hb = hn != null ? skel.FindBone(hn) : -1;
            if (hb < 0) continue;
            for (int b = 0; b < handBone.Length; b++)
                for (int p = b; p >= 0; p = skel.GetBoneParent(p))
                    if (p == hb) { handBone[b] = true; break; }
        }
        // Skinned vertices end up in skeleton space, so measure the body from the bones.
        Aabb bb = new();
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            var p = skel.GetBoneGlobalRest(i).Origin;
            bb = i == 0 ? new Aabb(p, Vector3.Zero) : bb.Expand(p);
        }
        var sz = bb.Size;
        int ax = sz.Z > sz.Y && sz.Z > sz.X ? 2 : sz.X > sz.Y ? 0 : 1;
        var up = ax == 2 ? Vector3.Back : ax == 0 ? Vector3.Right : Vector3.Up;
        // Calibrate from the hips: on a humanoid they sit at ~53% of standing height.
        int hips = skel.FindBone("Hips");
        float lo = bb.Position[ax];
        float hi = hips >= 0 ? lo + (skel.GetBoneGlobalRest(hips).Origin[ax] - lo) / 0.53f : bb.End[ax];
        foreach (var mi in Assets.AllMeshes(model))
        {
            if (attached.Exists(a => a == mi || a.IsAncestorOf(mi)) || mi.Mesh == null) continue;
            if (!any) { mi.MaterialOverlay = null; continue; }
            armourShader ??= new Shader { Code = ArmourShader };
            var mat = new ShaderMaterial { Shader = armourShader };
            mat.SetShaderParameter("up_axis", up);
            mat.SetShaderParameter("hmin", lo);
            mat.SetShaderParameter("hmax", hi);
            mat.SetShaderParameter("body_col", bc);
            mat.SetShaderParameter("body_metal", bm);
            mat.SetShaderParameter("legs_col", lc);
            mat.SetShaderParameter("legs_metal", lm);
            mat.SetShaderParameter("feet_col", fc);
            mat.SetShaderParameter("hand_col", hc);
            var binds = new List<int>();
            if (mi.Skin != null)   // always: bare hands are kept out of the sleeve tint
                for (int i = 0; i < mi.Skin.GetBindCount() && binds.Count < 24; i++)
                {
                    int sb = mi.Skin.GetBindBone(i);
                    if (sb < 0) sb = skel.FindBone(mi.Skin.GetBindName(i));
                    if (sb >= 0 && handBone[sb]) binds.Add(i);
                }
            mat.SetShaderParameter("hand_bones", binds.ToArray());
            mat.SetShaderParameter("hand_count", binds.Count);
            mi.MaterialOverlay = mat;
        }
    }

    public override void Play(string a)
    {
        base.Play(a);
        if (anim == null) return;
        string clip = a switch
        {
            "bite" => "slash", "eat" => "pickup",
            "chop" => anim.HasAnimation("chop") ? "chop" : "heavy",
            "mine" or "smith" => "heavy",
            "fish" or "farm" or "cook" or "craft" or "gather" => "pickup",
            "enchant" => "cast",
            _ => a,
        };
        if (!anim.HasAnimation(clip)) return;
        // Library clips are long; squeeze them to fit the 0.6s tick rhythm.
        float want = a is "chop" ? 1.6f : a is "mine" ? 1.5f : a is "smith" ? 1.3f : a is "fish" or "cook" or "craft" or "farm" ? 1.6f : clip switch { "slash" or "stab" => 0.9f, "heavy" => 1.2f, "shoot" => 1.3f, "cast" => 1.1f, "hit" => 0.5f, "death" => 1.4f, "pickup" => 1.0f, "block" => 0.7f, _ => 1f };
        float len = (float)anim.GetAnimation(clip).Length;
        anim.Play(clip, 0.12, Mathf.Max(0.5f, len / want));
        actionLen = want;
        loco = "";
    }

    protected override void ResetPose()
    {
        loco = "";
        if (model != null) model.Rotation = Vector3.Zero;
    }

    protected override void OnStopAction()
    {
        loco = "";
        if (anim != null && anim.HasAnimation("idle")) { anim.Play("idle", 0.15); loco = "idle"; }
    }

    public override void _Process(double delta)
    {
        TickTool(delta);
        t += (float)delta;
        handR?.Pose(skel, gripR ? 1f : 0f);
        handL?.Pose(skel, gripL ? 1f : 0f);
        float hold = !dead && (action == null || (action == "debug" && debugLoco)) ? 1f : 0f;
        if (holdR != null) holdR.Want = gripR ? hold : 0f;
        if (holdL != null) holdL.Want = gripL || shieldL ? hold : 0f;
        if (anim == null)
        {
            // No clips: minimal bob so it doesn't look frozen.
            model.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(t * 8)) * 0.05f * Mathf.Clamp(moveBlend, 0, 1), 0);
            if (dead) model.RotationDegrees = new Vector3(-80, 0, 0);
            return;
        }
        if (dead) return;
        if (action != null)
        {
            actionT += (float)delta;
            // Let the one-shot finish unless we start moving (then blend back to locomotion).
            if (actionT < actionLen && (moveBlend < 0.5f || action == "hit")) return;
            action = null;
        }
        string want = moveBlend > 1.2f ? "run" : moveBlend > 0.3f ? "walk" : "idle";
        if (!anim.HasAnimation(want)) want = "idle";
        if (want != loco)
        {
            loco = want;
            anim.Play(want, 0.2);
        }
        anim.SpeedScale = want == "walk" ? 1.15f : want == "run" ? 1.0f : 1f;
    }
}




// =====================================================================================
// Creatures: static GLB or procedural body, animated with simple transforms.
// =====================================================================================
public partial class CreatureVisual : BodyVisual
{
    NpcDef def;
    Node3D body;
    readonly List<(Node3D leg, float phase)> legs = new();
    Node3D wingL, wingR;
    float t;

    public void Build(NpcDef d)
    {
        def = d;
        Height = d.Height;
        body = new Node3D();
        AddChild(body);
        var m = Assets.Model(d.Model, d.Height);
        if (m != null)
        {
            body.AddChild(m);
            if (d.Tint != Colors.White) Assets.Modulate(m, d.Tint);
            if (d.Body == NpcBody.Floating && d.Id == "ghost")
                foreach (var mi in Assets.AllMeshes(m))
                    mi.Transparency = 0.35f;
        }
        else BuildProcedural(d);
    }

    void BuildProcedural(NpcDef d)
    {
        float h = d.Height;
        switch (d.Body)
        {
            case NpcBody.Quadruped:
            case NpcBody.Dragon:
            {
                Color c = d.Id switch { "cow" => new Color(0.9f, 0.9f, 0.88f), "wolf" => new Color(0.45f, 0.45f, 0.48f), "bear" => new Color(0.36f, 0.24f, 0.14f), "rat" => new Color(0.4f, 0.35f, 0.3f), "moss_drake" => new Color(0.2f, 0.5f, 0.2f), _ => new Color(0.5f, 0.4f, 0.3f) };
                float len = h * 1.3f, bh = h * 0.45f;
                Props.Box(body, new Vector3(h * 0.55f, bh, len), new Vector3(0, h * 0.62f, 0), Props.Mat(c));
                if (d.Id == "cow") Props.Box(body, new Vector3(h * 0.56f, bh * 0.5f, len * 0.3f), new Vector3(0, h * 0.7f, len * 0.1f), Props.Mat(new Color(0.1f, 0.1f, 0.1f)));
                Props.Box(body, new Vector3(h * 0.35f, h * 0.32f, h * 0.45f), new Vector3(0, h * 0.8f, len * 0.6f), Props.Mat(c));
                Props.Sphere(body, h * 0.04f, new Vector3(-h * 0.1f, h * 0.88f, len * 0.82f), Props.Mat(Colors.Black), null, 6);
                Props.Sphere(body, h * 0.04f, new Vector3(h * 0.1f, h * 0.88f, len * 0.82f), Props.Mat(Colors.Black), null, 6);
                foreach (var (x, z, ph) in new[] { (-1, 1, 0f), (1, 1, Mathf.Pi), (-1, -1, Mathf.Pi), (1, -1, 0f) })
                {
                    var leg = new Node3D { Position = new Vector3(x * h * 0.2f, h * 0.45f, z * len * 0.35f) };
                    body.AddChild(leg);
                    Props.Box(leg, new Vector3(h * 0.12f, h * 0.45f, h * 0.12f), new Vector3(0, -h * 0.22f, 0), Props.Mat(c.Darkened(0.2f)));
                    legs.Add((leg, ph));
                }
                if (d.Body == NpcBody.Dragon)
                {
                    wingL = new Node3D { Position = new Vector3(-h * 0.25f, h * 0.85f, 0) };
                    wingR = new Node3D { Position = new Vector3(h * 0.25f, h * 0.85f, 0) };
                    body.AddChild(wingL); body.AddChild(wingR);
                    Props.Box(wingL, new Vector3(h * 0.9f, 0.04f, h * 0.6f), new Vector3(-h * 0.45f, 0, 0), Props.Mat(c.Darkened(0.3f)));
                    Props.Box(wingR, new Vector3(h * 0.9f, 0.04f, h * 0.6f), new Vector3(h * 0.45f, 0, 0), Props.Mat(c.Darkened(0.3f)));
                    Props.Cyl(body, 0.02f, h * 0.12f, len * 0.8f, new Vector3(0, h * 0.55f, -len * 0.8f), Props.Mat(c), 6, new Vector3(-80, 0, 0));
                }
                break;
            }
            case NpcBody.Bird:
                Props.Sphere(body, h * 0.35f, new Vector3(0, h * 0.5f, 0), Props.Mat(new Color(0.95f, 0.93f, 0.88f)), new Vector3(1, 0.9f, 1.2f), 8);
                Props.Sphere(body, h * 0.18f, new Vector3(0, h * 0.85f, h * 0.25f), Props.Mat(new Color(0.95f, 0.93f, 0.88f)), null, 8);
                Props.Prism(body, new Vector3(h * 0.08f, h * 0.12f, h * 0.08f), new Vector3(0, h * 0.83f, h * 0.45f), Props.Mat(new Color(0.95f, 0.7f, 0.1f)), new Vector3(90, 0, 0));
                Props.Box(body, new Vector3(h * 0.06f, h * 0.12f, h * 0.1f), new Vector3(0, h * 1.02f, h * 0.25f), Props.Mat(new Color(0.85f, 0.1f, 0.1f)));
                foreach (var x in new[] { -1, 1 })
                {
                    var leg = new Node3D { Position = new Vector3(x * h * 0.1f, h * 0.2f, 0) };
                    body.AddChild(leg);
                    Props.Box(leg, new Vector3(0.03f, h * 0.25f, 0.03f), new Vector3(0, -h * 0.1f, 0), Props.Mat(new Color(0.95f, 0.7f, 0.1f)));
                    legs.Add((leg, x > 0 ? 0 : Mathf.Pi));
                }
                break;
            case NpcBody.Spider:
            {
                var c = new Color(0.12f, 0.1f, 0.1f);
                Props.Sphere(body, h * 0.45f, new Vector3(0, h * 0.6f, -h * 0.3f), Props.Mat(c), new Vector3(1, 0.8f, 1.2f), 10);
                Props.Sphere(body, h * 0.25f, new Vector3(0, h * 0.5f, h * 0.3f), Props.Mat(c), null, 8);
                for (int i = 0; i < 8; i++)
                {
                    int side = i < 4 ? -1 : 1;
                    var leg = new Node3D { Position = new Vector3(side * h * 0.2f, h * 0.55f, (i % 4 - 1.5f) * h * 0.2f), RotationDegrees = new Vector3(0, side * (60 + (i % 4) * 20 - 30), 0) };
                    body.AddChild(leg);
                    Props.Box(leg, new Vector3(h * 0.9f, 0.05f, 0.05f), new Vector3(side * h * 0.4f, -h * 0.2f, 0), Props.Mat(c), new Vector3(0, 0, side * -30));
                    legs.Add((leg, i * 0.8f));
                }
                Props.Sphere(body, 0.04f, new Vector3(-0.06f, h * 0.6f, h * 0.5f), Props.Mat(new Color(1, 0.1f, 0.1f), 0.3f, 0, 2f), null, 6);
                Props.Sphere(body, 0.04f, new Vector3(0.06f, h * 0.6f, h * 0.5f), Props.Mat(new Color(1, 0.1f, 0.1f), 0.3f, 0, 2f), null, 6);
                break;
            }
            case NpcBody.Floating:
            {
                bool ghost = d.Id == "ghost";
                var c = ghost ? new Color(0.7f, 1f, 0.9f) : new Color(0.25f, 0.2f, 0.2f);
                var mat = new StandardMaterial3D { AlbedoColor = new Color(c, ghost ? 0.55f : 1f), Transparency = ghost ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled, EmissionEnabled = ghost, Emission = c, EmissionEnergyMultiplier = 0.6f };
                if (ghost)
                {
                    Props.Cyl(body, h * 0.15f, h * 0.3f, h * 0.8f, new Vector3(0, h * 0.55f, 0), mat, 10);
                    Props.Sphere(body, h * 0.14f, new Vector3(0, h * 1.02f, 0), mat, null, 10);
                }
                else
                {
                    Props.Sphere(body, h * 0.2f, new Vector3(0, h * 0.8f, 0), mat, null, 8);
                    wingL = new Node3D { Position = new Vector3(-h * 0.15f, h * 0.85f, 0) };
                    wingR = new Node3D { Position = new Vector3(h * 0.15f, h * 0.85f, 0) };
                    body.AddChild(wingL); body.AddChild(wingR);
                    Props.Box(wingL, new Vector3(h * 0.6f, 0.02f, h * 0.3f), new Vector3(-h * 0.3f, 0, 0), mat);
                    Props.Box(wingR, new Vector3(h * 0.6f, 0.02f, h * 0.3f), new Vector3(h * 0.3f, 0, 0), mat);
                }
                break;
            }
            default:
            {
                // Winged demons and giants reuse the humanoid rig.
                var hum = new ProceduralHumanoid
                {
                    Skin = d.Id == "pit_fiend" ? new Color(0.65f, 0.12f, 0.1f) : new Color(0.35f, 0.45f, 0.25f),
                    Shirt = d.Id == "pit_fiend" ? new Color(0.5f, 0.1f, 0.08f) : new Color(0.3f, 0.4f, 0.2f),
                    Pants = new Color(0.25f, 0.2f, 0.15f), Hunched = true,
                };
                hum.Build(h);
                body.AddChild(hum);
                if (d.Body == NpcBody.Winged)
                {
                    wingL = new Node3D { Position = new Vector3(-0.2f, h * 0.75f, -0.2f) };
                    wingR = new Node3D { Position = new Vector3(0.2f, h * 0.75f, -0.2f) };
                    body.AddChild(wingL); body.AddChild(wingR);
                    Props.Box(wingL, new Vector3(h * 0.5f, h * 0.4f, 0.03f), new Vector3(-h * 0.25f, 0, 0), Props.Mat(new Color(0.3f, 0.05f, 0.05f)));
                    Props.Box(wingR, new Vector3(h * 0.5f, h * 0.4f, 0.03f), new Vector3(h * 0.25f, 0, 0), Props.Mat(new Color(0.3f, 0.05f, 0.05f)));
                }
                break;
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        t += dt;
        if (dead)
        {
            deadT += dt;
            float k = Mathf.Clamp(deadT / 0.5f, 0, 1);
            body.RotationDegrees = new Vector3(0, 0, 90 * k);
            body.Position = new Vector3(0, def.Body == NpcBody.Floating ? 0 : -0.1f * k, 0);
            body.Scale = Vector3.One * (1 - 0.3f * Mathf.Clamp(deadT - 0.6f, 0, 1));
            return;
        }
        float speed = Mathf.Clamp(moveBlend, 0, 1.5f);
        float hover = def.Body == NpcBody.Floating ? 0.6f + Mathf.Sin(t * 2.2f) * 0.15f : 0;
        float bob = def.Body == NpcBody.Bird ? Mathf.Abs(Mathf.Sin(t * 12)) * 0.08f * speed : Mathf.Abs(Mathf.Sin(t * 8)) * 0.04f * speed;
        float lunge = 0, tilt = 0;
        if (action != null)
        {
            actionT += dt;
            float p = Mathf.Clamp(actionT / actionLen, 0, 1);
            float sw = Mathf.Sin(p * Mathf.Pi);
            if (action is "slash" or "bite" or "stab" or "heavy") { lunge = sw * 0.35f * Mathf.Max(1, Height * 0.4f); tilt = -sw * 12; }
            else if (action == "hit") tilt = sw * 10;
            else if (action is "cast" or "shoot") tilt = -sw * 6;
            if (p >= 1) action = null;
        }
        body.Position = new Vector3(0, hover + bob, lunge);
        body.RotationDegrees = new Vector3(tilt + Mathf.Sin(t * 1.5f) * 1.5f, 0, 0);
        foreach (var (leg, ph) in legs)
            leg.RotationDegrees = new Vector3(Mathf.Sin(t * 10 + ph) * 30 * Mathf.Clamp(speed, 0, 1), leg.RotationDegrees.Y, 0);
        if (wingL != null)
        {
            float flap = def.Body == NpcBody.Floating ? Mathf.Sin(t * 14) * 40 : Mathf.Sin(t * 3) * 10 + 15;
            wingL.RotationDegrees = new Vector3(0, 0, flap);
            wingR.RotationDegrees = new Vector3(0, 0, -flap);
        }
    }
}
