using Godot;

namespace Fantasia.Client;

/// Layered on top of the shared locomotion clips: bends the weapon arm's elbow and turns the wrist
/// so a held item points where it should (blades forward-down, staves and bows upright) whatever the
/// idle/walk clip does with the arms. Fades out while attack/cast clips play, since those clips
/// already pose the arm for the swing.
public partial class HoldPose : SkeletonModifier3D
{
    public int Arm = -1, ForeArm = -1, Hand = -1;
    /// Hand-local handle direction (HandRig.Hand.GripAxis).
    public Vector3 GripAxis;
    /// Rotation from model space into skeleton space.
    public Quaternion ModelToSkel = Quaternion.Identity;
    /// Child of the skeleton that is moved to the final hand pose every frame (held items hang off it).
    public Node3D Follower;

    /// Optional item strapped to the forearm (a shield): kept upright at the forearm's middle, its
    /// face (item +X) turned towards ForeFace, pushed ForeOut metres along ForeSide. Model space.
    public Node3D ForeItem;
    public Vector3 ForeFace = new(0.5f, 0f, 0.87f), ForeSide = Vector3.Right;
    public float ForeAlong = 0.55f, ForeOut = 0.07f, ForeAhead = 0f, ForeScale = 1f;

    Vector3 targetDir = Vector3.Forward;   // model space
    float elbowDeg;
    float weight, wantWeight;
    const float MaxWristDeg = 85f;

    /// target: desired handle direction in model space (character faces +Z). null disables.
    public void SetHold(Vector3? target, float elbow, bool wrist = true)
    {
        if (target == null) { wantWeight = 0; return; }
        targetDir = target.Value.Normalized();
        elbowDeg = elbow;
        Wrist = wrist;
    }

    /// Turn the wrist towards the target too (off for a shield arm: only the elbow bends).
    public bool Wrist = true;

    /// 1 while idling/walking, 0 during one-shot actions.
    public float Want { set => wantWeight = value; }

    public override void _ProcessModificationWithDelta(double delta)
    {
        weight = Mathf.MoveToward(weight, wantWeight, (float)delta * 6f);
        var sk = GetSkeleton();
        if (sk == null || Arm < 0 || ForeArm < 0 || Hand < 0) return;
        // Built from local poses: the skeleton's cached global poses can be stale at this point.
        var upG = GlobalOf(sk, Arm);
        var foreG = upG * sk.GetBonePose(ForeArm);
        var handLocal = sk.GetBonePose(Hand);
        var handG = foreG * handLocal;
        if (weight > 0.001f) Bend(sk, upG, ref foreG, handLocal, ref handG);
        if (Follower != null) Follower.Transform = handG;
        if (ForeItem != null)
        {
            var up = (ModelToSkel * Vector3.Up).Normalized();
            var face = (ModelToSkel * ForeFace).Normalized();
            face = (face - up * face.Dot(up)).Normalized();
            var pos = foreG.Origin.Lerp(handG.Origin, ForeAlong) + ((ModelToSkel * ForeSide).Normalized() * ForeOut + face * ForeAhead) * ForeScale;
            ForeItem.Transform = new Transform3D(new Basis(face, up, face.Cross(up)).Scaled(Vector3.One * ForeScale), pos);
        }
    }

    void Bend(Skeleton3D sk, Transform3D upG, ref Transform3D foreG, Transform3D handLocal, ref Transform3D handG)
    {
        var fwd = (ModelToSkel * Vector3.Back).Normalized();   // model +Z

        // Elbow: swing the forearm towards the character's front.
        var d = (handG.Origin - foreG.Origin).Normalized();
        var axis = d.Cross(fwd);
        if (axis.LengthSquared() > 1e-6f && elbowDeg > 0)
        {
            var q = new Quaternion(axis.Normalized(), Mathf.DegToRad(elbowDeg) * weight);
            var nb = new Basis(q) * foreG.Basis;
            foreG = new Transform3D(nb, foreG.Origin);
            sk.SetBonePoseRotation(ForeArm, Rot(upG.Basis.Inverse() * nb));
            handG = foreG * handLocal;
        }
        if (!Wrist) return;

        // Wrist: turn the fist so the handle points at the target direction.
        var g = (handG.Basis * GripAxis).Normalized();
        var t = (ModelToSkel * targetDir).Normalized();
        var waxis = g.Cross(t);
        float ang = Mathf.Acos(Mathf.Clamp(g.Dot(t), -1f, 1f));
        if (waxis.LengthSquared() > 1e-6f && ang > 0.001f)
        {
            ang = Mathf.Min(ang, Mathf.DegToRad(MaxWristDeg)) * weight;
            var nb = new Basis(new Quaternion(waxis.Normalized(), ang)) * handG.Basis;
            sk.SetBonePoseRotation(Hand, Rot(foreG.Basis.Inverse() * nb));
            handG = new Transform3D(nb, handG.Origin);
        }
    }

    static Transform3D GlobalOf(Skeleton3D sk, int bone)
    {
        var xf = sk.GetBonePose(bone);
        for (int p = sk.GetBoneParent(bone); p >= 0; p = sk.GetBoneParent(p)) xf = sk.GetBonePose(p) * xf;
        return xf;
    }

    static Quaternion Rot(Basis b) => b.Orthonormalized().GetRotationQuaternion();
}
