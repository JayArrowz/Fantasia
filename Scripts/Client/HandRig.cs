using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia.Client;

/// The generated rigs stop at the hand bone, so their hands are rigid open palms that can't hold
/// anything. At load time this adds a finger bone (at the knuckles) and a thumb bone to each hand,
/// re-skins the hand vertices onto them from the mesh's own geometry, and exposes a grip frame so
/// weapons sit inside a properly closed fist. Results are cached per model.
public static class HandRig
{
    /// Per-instance handle used to pose the fingers every frame.
    public sealed class Hand
    {
        public int FingerBone = -1, ThumbBone = -1;
        public Vector3 CurlAxis;          // hand-local, rotates fingertips towards the palm
        public Vector3 ThumbAxis;         // hand-local, folds the thumb across the palm
        public Vector3 GripPoint;         // hand-local centre of the closed fist
        public Vector3 GripAxis;          // hand-local handle direction, out of the thumb side
        public Vector3 FingerDir;         // hand-local direction from wrist to fingertips
        public Vector3 PalmNormal;        // hand-local, out of the palm
        public Transform3D FingerRest, ThumbRest;
        float amount = 0.15f;

        /// grip: 0 = relaxed, 1 = closed fist.
        public void Pose(Skeleton3D sk, float grip)
        {
            float target = Mathf.Lerp(0.15f, 1f, grip);
            amount = Mathf.MoveToward(amount, target, 0.12f);
            if (FingerBone >= 0)
                sk.SetBonePoseRotation(FingerBone, FingerRest.Basis.GetRotationQuaternion() * new Quaternion(CurlAxis, Mathf.DegToRad(100f) * amount));
            if (ThumbBone >= 0)
                sk.SetBonePoseRotation(ThumbBone, ThumbRest.Basis.GetRotationQuaternion() * new Quaternion(ThumbAxis, Mathf.DegToRad(55f) * amount));
        }
    }

    sealed class SideData
    {
        public string Name;
        public Transform3D FingerRest, ThumbRest;
        public Vector3 CurlAxis, ThumbAxis, GripPoint, GripAxis, FingerDir, PalmNormal;
    }

    sealed class ModelData
    {
        public SideData Left, Right;
        public readonly Dictionary<string, (Mesh mesh, Skin skin)> Meshes = new();
    }

    static readonly Dictionary<string, ModelData> Cache = new();

    public static (Hand left, Hand right) Install(string key, Node3D modelRoot, Skeleton3D skel)
    {
        if (!Cache.TryGetValue(key, out var data))
        {
            try { data = Build(modelRoot, skel); }
            catch (Exception e) { GD.PrintErr($"[HandRig] {key}: {e.Message}"); data = null; Work.Clear(); }
            Cache[key] = data;
        }
        if (data == null) return (null, null);
        foreach (var mi in Assets.AllMeshes(modelRoot))
        {
            var path = modelRoot.GetPathTo(mi).ToString();
            if (data.Meshes.TryGetValue(path, out var ms)) { mi.Mesh = ms.mesh; mi.Skin = ms.skin; }
        }
        return (AddBones(skel, data.Left), AddBones(skel, data.Right));
    }

    static Hand AddBones(Skeleton3D skel, SideData d)
    {
        if (d == null) return null;
        int hand = skel.FindBone(d.Name + "Hand");
        int f = skel.FindBone(d.Name + "Fingers");
        if (f < 0)
        {
            f = skel.GetBoneCount();
            skel.AddBone(d.Name + "Fingers");
            skel.SetBoneParent(f, hand);
            skel.SetBoneRest(f, d.FingerRest);
        }
        int t = skel.FindBone(d.Name + "Thumb");
        if (t < 0)
        {
            t = skel.GetBoneCount();
            skel.AddBone(d.Name + "Thumb");
            skel.SetBoneParent(t, hand);
            skel.SetBoneRest(t, d.ThumbRest);
        }
        skel.ResetBonePose(f);
        skel.ResetBonePose(t);
        return new Hand
        {
            FingerBone = f, ThumbBone = t, CurlAxis = d.CurlAxis, ThumbAxis = d.ThumbAxis,
            GripPoint = d.GripPoint, GripAxis = d.GripAxis, FingerDir = d.FingerDir, PalmNormal = d.PalmNormal,
            FingerRest = d.FingerRest, ThumbRest = d.ThumbRest,
        };
    }

    // ------------------------------------------------------------------------------------------

    sealed class Vert { public MeshInstance3D Mi; public int Surface, Index, Slot; public Vector3 P; }

    // Bone/weight arrays being edited, per (mesh, surface); written back when meshes are rebuilt.
    static readonly Dictionary<(MeshInstance3D, int), (int[] bones, float[] weights, int per)> Work = new();

    static ModelData Build(Node3D modelRoot, Skeleton3D skel)
    {
        var meshes = new List<MeshInstance3D>();
        foreach (var mi in Assets.AllMeshes(modelRoot))
            if (mi.Skin != null && mi.Mesh is ArrayMesh) meshes.Add(mi);
        if (meshes.Count == 0) return null;

        // Skeleton orientation in the model (for the palm-facing heuristic).
        var skelXf = Transform3D.Identity;
        for (Node n = skel; n != null && n != modelRoot; n = n.GetParent())
            if (n is Node3D n3) skelXf = n3.Transform * skelXf;

        // Working copies of every surface's arrays and each mesh's skin.
        var arrays = new Dictionary<(MeshInstance3D, int), Godot.Collections.Array>();
        var skins = new Dictionary<MeshInstance3D, Skin>();
        foreach (var mi in meshes)
        {
            skins[mi] = (Skin)mi.Skin.Duplicate();
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var arr = mi.Mesh.SurfaceGetArrays(s);
                arrays[(mi, s)] = arr;
                var bones = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                int vc = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                Work[(mi, s)] = (bones, arr[(int)Mesh.ArrayType.Weights].AsFloat32Array(), vc > 0 ? bones.Length / vc : 4);
            }
        }

        var data = new ModelData();
        foreach (var side in new[] { "Left", "Right" })
        {
            var sd = BuildSide(side, skel, skelXf, meshes, arrays, skins);
            if (side == "Left") data.Left = sd; else data.Right = sd;
        }
        if (data.Left == null && data.Right == null) return null;

        // Rebuild meshes with the new weights.
        foreach (var mi in meshes)
        {
            var src = (ArrayMesh)mi.Mesh;
            var dst = new ArrayMesh();
            for (int s = 0; s < src.GetSurfaceCount(); s++)
            {
                var arr = arrays[(mi, s)];
                var (wb, ww, per) = Work[(mi, s)];
                arr[(int)Mesh.ArrayType.Bones] = wb;
                arr[(int)Mesh.ArrayType.Weights] = ww;
                var flags = per == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0;
                dst.AddSurfaceFromArrays(src.SurfaceGetPrimitiveType(s), arr, null, null, flags);
                dst.SurfaceSetMaterial(s, src.SurfaceGetMaterial(s));
            }
            data.Meshes[modelRoot.GetPathTo(mi).ToString()] = (dst, skins[mi]);
        }
        Work.Clear();
        return data;
    }

    static int BindIndexFor(Skin skin, Skeleton3D skel, int bone)
    {
        for (int i = 0; i < skin.GetBindCount(); i++)
        {
            if (skin.GetBindBone(i) == bone) return i;
            var nm = skin.GetBindName(i);
            if (!string.IsNullOrEmpty(nm) && skel.FindBone(nm) == bone) return i;
        }
        return -1;
    }

    static SideData BuildSide(string side, Skeleton3D skel, Transform3D skelXf, List<MeshInstance3D> meshes,
        Dictionary<(MeshInstance3D, int), Godot.Collections.Array> arrays, Dictionary<MeshInstance3D, Skin> skins)
    {
        int hand = skel.FindBone(side + "Hand"), fore = skel.FindBone(side + "ForeArm");
        if (hand < 0 || fore < 0) return null;
        var handG = skel.GetBoneGlobalRest(hand);
        var foreG = skel.GetBoneGlobalRest(fore);
        var dir = (handG.Origin - foreG.Origin).Normalized();
        var origin = handG.Origin;

        // Gather vertices dominated by the hand bone, in skeleton space.
        var verts = new List<Vert>();
        foreach (var mi in meshes)
        {
            var skin = skins[mi];
            int b = BindIndexFor(skin, skel, hand);
            if (b < 0) continue;
            var toSkel = handG * skin.GetBindPose(b);
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var pos = arrays[(mi, s)][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var (bones, weights, per) = Work[(mi, s)];
                if (pos.Length == 0) continue;
                for (int v = 0; v < pos.Length; v++)
                    for (int j = 0; j < per; j++)
                        if (bones[v * per + j] == b && weights[v * per + j] > 0.35f)
                        {
                            verts.Add(new Vert { Mi = mi, Surface = s, Index = v, Slot = j, P = toSkel * pos[v] });
                            break;
                        }
            }
        }
        if (verts.Count < 20) return null;

        // Hand length (wrist to fingertip) and knuckle line.
        float len = 0;
        foreach (var v in verts) len = Mathf.Max(len, (v.P - origin).Dot(dir));
        if (len <= 0) return null;
        float knuckle = len * 0.5f;

        // Palm normal = thinnest direction of the palm, perpendicular to the finger direction.
        var u = dir.Cross(Mathf.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var w = dir.Cross(u).Normalized();
        double suu = 0, sww = 0, suw = 0; int cnt = 0;
        Vector3 c = Vector3.Zero;
        foreach (var v in verts) { float t = (v.P - origin).Dot(dir); if (t > 0.1f * len && t < knuckle) { c += v.P; cnt++; } }
        if (cnt < 8) return null;
        c /= cnt;
        foreach (var v in verts)
        {
            float t = (v.P - origin).Dot(dir);
            if (t <= 0.1f * len || t >= knuckle) continue;
            var d = v.P - c;
            float du = d.Dot(u), dw = d.Dot(w);
            suu += du * du; sww += dw * dw; suw += du * dw;
        }
        // Minor principal axis of the 2x2 covariance.
        double ang = 0.5 * Math.Atan2(2 * suw, suu - sww);
        var major = (u * (float)Math.Cos(ang) + w * (float)Math.Sin(ang)).Normalized();
        var normal = dir.Cross(major).Normalized();
        // Palms face the body / downwards in the A-pose.
        var basisM = skelXf.Basis;
        var handModel = skelXf * origin;
        var inward = new Vector3(-Mathf.Sign(handModel.X), 0, 0);
        var nModel = (basisM * normal).Normalized();
        if (nModel.Dot(inward) + 0.6f * nModel.Dot(Vector3.Down) < 0) normal = -normal;

        var curl = dir.Cross(normal).Normalized();      // +angle moves fingertips towards the palm
        // Thumb side: the palm region bulges towards the thumb along the knuckle axis.
        float mean = 0; int mc = 0; float halfWidth = 0;
        foreach (var v in verts)
        {
            float t = (v.P - origin).Dot(dir);
            float a = (v.P - origin).Dot(curl);
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(a));
            if (t > 0.05f * len && t < knuckle) { mean += a; mc++; }
        }
        mean = mc > 0 ? mean / mc : 0;
        var thumbDir = mean >= 0 ? curl : -curl;

        // New bones and binds.
        var handInv = handG.AffineInverse();
        var knucklePt = origin + dir * knuckle;
        var thumbBase = origin + dir * (0.18f * len) + thumbDir * (halfWidth * 0.45f);
        var fingerRest = new Transform3D(Basis.Identity, handInv * knucklePt);
        var thumbRest = new Transform3D(Basis.Identity, handInv * thumbBase);
        var fingerG = handG * fingerRest;
        var thumbG = handG * thumbRest;
        string fName = side + "Fingers", tName = side + "Thumb";

        var fingerBind = new Dictionary<MeshInstance3D, int>();
        var thumbBind = new Dictionary<MeshInstance3D, int>();
        foreach (var mi in meshes)
        {
            var skin = skins[mi];
            int b = BindIndexFor(skin, skel, hand);
            if (b < 0) continue;
            var bindHand = skin.GetBindPose(b);
            fingerBind[mi] = skin.GetBindCount();
            skin.AddNamedBind(fName, fingerG.AffineInverse() * handG * bindHand);
            thumbBind[mi] = skin.GetBindCount();
            skin.AddNamedBind(tName, thumbG.AffineInverse() * handG * bindHand);
        }

        // Re-weight: fingers beyond the knuckles, thumb on the thumb side near the palm.
        foreach (var v in verts)
        {
            var rel = v.P - origin;
            float t = rel.Dot(dir);
            float side1 = rel.Dot(thumbDir);
            float thumbW = side1 > halfWidth * 0.35f && t < knuckle * 1.35f && t > 0.1f * len
                ? Mathf.SmoothStep(halfWidth * 0.35f, halfWidth * 0.6f, side1) : 0f;
            float fingerW = thumbW > 0.5f ? 0f : Mathf.SmoothStep(knuckle - 0.07f * len, knuckle + 0.07f * len, t);
            if (thumbW > 0) Transfer(arrays, v, thumbBind[v.Mi], thumbW);
            else if (fingerW > 0) Transfer(arrays, v, fingerBind[v.Mi], fingerW);
        }

        // Grip: a closed fist wraps around a handle just in front of the palm at the knuckle line.
        var grip = origin + dir * (knuckle * 0.95f) + normal * (0.2f * len);
        var basisInv = handG.Basis.Inverse();
        return new SideData
        {
            Name = side, FingerRest = fingerRest, ThumbRest = thumbRest,
            CurlAxis = (basisInv * curl).Normalized(),
            ThumbAxis = (basisInv * thumbDir.Cross(normal)).Normalized(),
            GripPoint = handInv * grip,
            GripAxis = (basisInv * thumbDir).Normalized(),
            FingerDir = (basisInv * dir).Normalized(),
            PalmNormal = (basisInv * normal).Normalized(),
        };
    }

    /// Moves `fraction` of the hand weight in this vertex's slot onto `bind`, keeping the sum at 1.
    static void Transfer(Dictionary<(MeshInstance3D, int), Godot.Collections.Array> arrays, Vert v, int bind, float fraction)
    {
        var (bones, weights, per) = Work[(v.Mi, v.Surface)];
        int baseI = v.Index * per;
        float moved = weights[baseI + v.Slot] * Mathf.Clamp(fraction, 0f, 1f);
        if (moved <= 0.0001f) return;
        // Use an empty slot, else the lightest other slot (folding its weight into the hand).
        int target = -1; float lightest = float.MaxValue;
        for (int j = 0; j < per; j++)
        {
            if (j == v.Slot) continue;
            if (weights[baseI + j] <= 0.0001f) { target = j; break; }
            if (weights[baseI + j] < lightest) { lightest = weights[baseI + j]; target = j; }
        }
        if (target < 0) return;
        if (weights[baseI + target] > 0.0001f) weights[baseI + v.Slot] += weights[baseI + target];
        weights[baseI + v.Slot] -= moved;
        bones[baseI + target] = bind;
        weights[baseI + target] = moved;
    }
}
