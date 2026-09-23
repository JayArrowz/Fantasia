using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia.Client;

/// Worn body armour, leggings, robes and boots come from *rigged outfit models* on the same
/// Meshy skeleton: the part of the outfit mesh that belongs to the slot (torso and arms, legs, feet)
/// is carried over bone by bone onto the wearer's skeleton, keeping its own skin weights, texture and
/// shape, so it animates exactly like the body. The wearer's own skin under it is hidden (see
/// <see cref="HideBones"/>) so nothing pokes through.
public static class ArmourFit
{
    /// Skirt: a robe bottom worn without a body piece. It is worn over the tunic, from the belt down.
    public enum Region { Torso, Robe, Legs, Skirt, Feet }

    /// Which outfit model dresses an item (equipment.json "outfits").
    public static string SourceOf(ItemDef d) => EquipTuning.T.Outfits.TryGetValue(d.Icon, out var o) ? o.Model : null;

    public static Region? RegionOf(ItemDef d) => d.Slot switch
    {
        EquipSlot.Body => d.Icon == IconKind.Robe ? Region.Robe : Region.Torso,
        EquipSlot.Legs => Region.Legs,
        EquipSlot.Feet => Region.Feet,
        _ => null,
    };

    enum Part { Other, Head, Neck, Spine, Hips, Shoulder, UpperArm, ForeArm, Hand, UpLeg, Leg, Foot }

    static Part PartOf(string bone)
    {
        var n = bone.ToLowerInvariant();
        if (n.Contains("upleg")) return Part.UpLeg;
        if (n.Contains("leg")) return Part.Leg;
        if (n.Contains("foot") || n.Contains("toe")) return Part.Foot;
        if (n.Contains("forearm")) return Part.ForeArm;
        if (n.Contains("hand") || n.Contains("finger") || n.Contains("thumb")) return Part.Hand;
        if (n.Contains("shoulder")) return Part.Shoulder;
        if (n.EndsWith("arm")) return Part.UpperArm;
        if (n.Contains("spine") || n.Contains("chest")) return Part.Spine;
        if (n == "hips" || n.Contains("pelvis")) return Part.Hips;
        if (n.Contains("neck")) return Part.Neck;
        if (n.Contains("head")) return Part.Head;
        return Part.Other;
    }

    // ------------------------------------------------------------------ rig sampling

    sealed class Rig
    {
        public Transform3D[] Rest;                  // per skeleton bone
        public Part[] Parts;
        public float Upper, Lower, Height;          // hips->head, hip->ankle, feet->crown (bones)
        public float HipsUp; public Vector3 Up;     // for splitting the hips into waist / seat
        public float FootUp;
    }

    /// Rest data of a rig. "Up" is the model's true vertical (not hips->head, which leans on some rigs).
    static Rig RigOf(Skeleton3D sk, Node stopAt)
    {
        var r = new Rig { Rest = new Transform3D[sk.GetBoneCount()], Parts = new Part[sk.GetBoneCount()] };
        for (int i = 0; i < r.Rest.Length; i++) { r.Rest[i] = sk.GetBoneGlobalRest(i); r.Parts[i] = PartOf(sk.GetBoneName(i)); }
        Vector3 At(Part p) { int i = Array.IndexOf(r.Parts, p); return i >= 0 ? r.Rest[i].Origin : Vector3.Zero; }
        var toModel = Basis.Identity;
        for (Node n = sk; n != null && n != stopAt; n = n.GetParent())
            if (n is Node3D n3) toModel = n3.Basis.Orthonormalized() * toModel;
        r.Up = (toModel.Inverse() * Vector3.Up).Normalized();
        float lo = r.Rest.Min(x => x.Origin.Dot(r.Up)), hi = r.Rest.Max(x => x.Origin.Dot(r.Up));
        r.Height = hi - lo;
        r.Upper = At(Part.Hips).DistanceTo(At(Part.Head));
        r.Lower = At(Part.UpLeg).DistanceTo(At(Part.Foot));
        r.HipsUp = At(Part.Hips).Dot(r.Up);
        r.FootUp = At(Part.Foot).Dot(r.Up);
        return r;
    }

    static MeshInstance3D MainMesh(Node model)
    {
        MeshInstance3D main = null; int most = 0;
        foreach (var mi in Assets.AllMeshes(model))
        {
            if (mi.Skin == null || mi.Mesh is not ArrayMesh am || mi.Name.ToString().StartsWith("Armour_")) continue;
            int n = 0;
            for (int s = 0; s < am.GetSurfaceCount(); s++) n += am.SurfaceGetArrayLen(s);
            if (n > most) { most = n; main = mi; }
        }
        return main;
    }

    static (int[] bone, Transform3D[] xf) Binds(Skin skin, Skeleton3D sk)
    {
        var bone = new int[skin.GetBindCount()]; var xf = new Transform3D[bone.Length];
        for (int i = 0; i < bone.Length; i++)
        {
            int b = skin.GetBindBone(i);
            if (b < 0) b = sk.FindBone(skin.GetBindName(i));
            bone[i] = b;
            xf[i] = b >= 0 ? sk.GetBoneGlobalRest(b) * skin.GetBindPose(i) : Transform3D.Identity;
        }
        return (bone, xf);
    }

    static Vector3 Anchor(Vector3 joint, Vector3? centroid, Vector3 up) =>
        centroid is Vector3 c ? c - up * c.Dot(up) + up * joint.Dot(up) : joint;

    /// Rest-pose centroid of the vertices each bone drives most, per skeleton bone.
    static Vector3?[] Centroids(MeshInstance3D mi, int[] bindBone, Transform3D[] bindXf, int boneCount)
    {
        var sum = new Vector3[boneCount]; var n = new int[boneCount];
        var am = (ArrayMesh)mi.Mesh;
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
                int best = -1; float bw = 0;
                for (int j = 0; j < per; j++)
                {
                    int b = bones[v * per + j];
                    if (b >= 0 && b < bindBone.Length && weights[v * per + j] > bw) { bw = weights[v * per + j]; best = b; }
                }
                if (best < 0 || bindBone[best] < 0) continue;
                sum[bindBone[best]] += bindXf[best] * verts[v];
                n[bindBone[best]]++;
            }
        }
        var c = new Vector3?[boneCount];
        for (int i = 0; i < boneCount; i++) if (n[i] > 10) c[i] = sum[i] / n[i];
        return c;
    }

    /// Rig facing as a basis (left, up, forward) in skeleton space.
    static Basis Facing(Skeleton3D sk, Rig r)
    {
        int l = sk.FindBone("LeftUpLeg"), rr = sk.FindBone("RightUpLeg");
        var left = l >= 0 && rr >= 0 ? r.Rest[l].Origin - r.Rest[rr].Origin : Vector3.Right;
        left = (left - r.Up * left.Dot(r.Up)).Normalized();
        return new Basis(left, r.Up, left.Cross(r.Up).Normalized());
    }

    /// Where a bone points: along the spine for the hips and upper spine (not towards the legs or
    /// shoulders), otherwise the average of its children, or on past its own end for a leaf.
    static Vector3 Tip(Skeleton3D sk, Rig r, int b)
    {
        for (int c = 0; c < r.Rest.Length; c++)
            if (sk.GetBoneParent(c) == b && r.Parts[b] is Part.Hips or Part.Spine or Part.Neck && r.Parts[c] is Part.Spine or Part.Neck or Part.Head)
                return r.Rest[c].Origin;
        var sum = Vector3.Zero; int n = 0;
        for (int c = 0; c < r.Rest.Length; c++)
            if (sk.GetBoneParent(c) == b) { sum += r.Rest[c].Origin; n++; }
        if (n > 0) return sum / n;
        int p = sk.GetBoneParent(b);
        return p >= 0 ? r.Rest[b].Origin + (r.Rest[b].Origin - r.Rest[p].Origin) : r.Rest[b].Origin;
    }

    static List<int> SpineChain(Skeleton3D sk)
    {
        int head = -1, hips = -1;
        for (int i = 0; i < sk.GetBoneCount(); i++)
        {
            var n = sk.GetBoneName(i).ToLowerInvariant();
            if (n == "head") head = i;
            if (n == "hips") hips = i;
        }
        var chain = new List<int>();
        if (head < 0 || hips < 0) return chain;
        for (int p = sk.GetBoneParent(head); p >= 0 && p != hips; p = sk.GetBoneParent(p)) chain.Insert(0, p);
        return chain;
    }

    static int[] MapBones(Skeleton3D src, Skeleton3D dst)
    {
        var map = new int[src.GetBoneCount()];
        for (int i = 0; i < map.Length; i++) map[i] = dst.FindBone(src.GetBoneName(i));
        var sc = SpineChain(src); var dc = SpineChain(dst);
        if (sc.Count > 0 && dc.Count > 0)
        {
            foreach (var b in sc) map[b] = -1;
            for (int k = 0; k < sc.Count; k++)
                map[sc[k]] = dc[sc.Count == 1 ? dc.Count - 1 : Mathf.RoundToInt(k * (dc.Count - 1f) / (sc.Count - 1f))];
        }
        return map;
    }

    // ------------------------------------------------------------------ the wearer's own body

    /// Per wearer model: its shirt hem and ankle heights, the body mesh with each vertex's rest height
    /// in UV2 (so the hiding shader can split by height), and each skin bind's body group.
    public sealed class Wearer
    {
        public float Hem, Belt, Ankle, Neck, NeckColumn, Collar, H;
        public ArrayMesh Mesh;       // the body mesh plus UV2.x = rest height
        public int[] Group;          // per skin bind: 1 trunk, 2 hips, 3 thigh, 4 shin, 5 foot, 6 arms, 7 neck, 0 other
    }

    static readonly Dictionary<string, Wearer> wearers = new();

    /// Forget fitted meshes and wearer measurements (after equipment.json changes).
    public static void ClearCaches() { wearers.Clear(); cache.Clear(); }

    static Wearer WearerOf(string key, Skeleton3D skel, MeshInstance3D body)
    {
        if (wearers.TryGetValue(key, out var w)) return w;
        var D = RigOf(skel, body.Owner);
        var (bone, xf) = Binds(body.Skin, skel);
        var A = EquipTuning.T.Armour;
        EquipTuning.T.Wearers.TryGetValue(key, out var over);
        w = new Wearer { H = D.Height, Belt = over?.Belt is float bf ? D.FootUp + bf * D.Height : D.HipsUp + A.Belt * D.Height, Ankle = D.FootUp + A.Ankle * D.Lower, Group = new int[bone.Length] };
        // The neck and collar stay: armour draws over them, and hiding them opens holes under open collars.
        w.Neck = ShoulderUp(skel, D) + A.NeckKeep * D.Height;
        w.NeckColumn = ShoulderUp(skel, D) + A.NeckColumnKeep * D.Height;
        // Body pieces keep their collar (gorget, cowl) up to just above the base of the neck.
        int neckBone = Array.IndexOf(D.Parts, Part.Neck);
        w.Collar = (neckBone >= 0 ? D.Rest[neckBone].Origin.Dot(D.Up) : w.Neck) + A.CollarRise * D.Height;
        for (int i = 0; i < bone.Length; i++)
            w.Group[i] = bone[i] < 0 ? 0 : D.Parts[bone[i]] switch
            {
                Part.Spine or Part.Shoulder => 1,
                Part.UpperArm or Part.ForeArm => 6,
                Part.Neck => 7,
                Part.Hips => 2, Part.UpLeg => 3, Part.Leg => 4, Part.Foot => 5, _ => 0,
            };
        // The shirt hem: the lowest light-coloured (shirt) vertex on the hips and thighs, read from the
        // body texture. Falls back to a little below the hips.
        // Height bands (1% of height) of hip/thigh vertices: how many, and how many are shirt-coloured.
        int bands = 100;
        var total = new int[bands]; var light = new int[bands];
        int Band(float h) => Mathf.Clamp((int)((h - D.FootUp) / D.Height * bands), 0, bands - 1);
        var am = (ArrayMesh)body.Mesh;
        var outMesh = new ArrayMesh();
        for (int s = 0; s < am.GetSurfaceCount(); s++)
        {
            var arr = am.SurfaceGetArrays(s);
            var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var uvs = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var bones = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var img = (am.SurfaceGetMaterial(s) as BaseMaterial3D)?.AlbedoTexture?.GetImage();
            if (img != null && img.IsCompressed()) { img = (Image)img.Duplicate(); img.Decompress(); }
            int per = verts.Length > 0 ? bones.Length / verts.Length : 0;
            var rest = new Vector2[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                int best = -1; float bw = 0;
                for (int j = 0; j < per; j++)
                {
                    int b = bones[v * per + j];
                    if (b >= 0 && b < bone.Length && weights[v * per + j] > bw) { bw = weights[v * per + j]; best = b; }
                }
                var p = best >= 0 ? xf[best] * verts[v] : verts[v];
                float h = p.Dot(D.Up);
                rest[v] = new Vector2(h, 0);
                // Shirt vertices (pale and unsaturated, unlike skin) on the neck, trunk and arms are
                // flagged in UV2.y so a body piece hides the whole shirt, collar included.
                if (best >= 0 && bone[best] >= 0 && img != null && uvs.Length == verts.Length
                    && D.Parts[bone[best]] is Part.Neck or Part.Spine or Part.Shoulder or Part.UpperArm or Part.ForeArm or Part.Hips or Part.UpLeg)
                {
                    var uv0 = uvs[v];
                    var c0 = img.GetPixel(Mathf.Clamp((int)(uv0.X * img.GetWidth()), 0, img.GetWidth() - 1), Mathf.Clamp((int)(uv0.Y * img.GetHeight()), 0, img.GetHeight() - 1));
                    if (c0.Luminance > A.ShirtLuminance && c0.S < A.ShirtSaturation) rest[v].Y = 1;
                }
                if (best >= 0 && w.Group[best] is 2 or 3 && img != null && uvs.Length == verts.Length)
                {
                    var uv = uvs[v];
                    var c = img.GetPixel(Mathf.Clamp((int)(uv.X * img.GetWidth()), 0, img.GetWidth() - 1), Mathf.Clamp((int)(uv.Y * img.GetHeight()), 0, img.GetHeight() - 1));
                    total[Band(h)]++;
                    if (c.Luminance > A.HemLuminance) light[Band(h)]++;
                }
            }
            arr[(int)Mesh.ArrayType.TexUV2] = rest;
            outMesh.AddSurfaceFromArrays(am.SurfaceGetPrimitiveType(s), arr);
            outMesh.SurfaceSetMaterial(s, am.SurfaceGetMaterial(s));
        }
        // Walk down from the hips while most of each band is shirt: the hem is where that stops.
        float hem = float.MaxValue;
        for (int bnd = Band(D.HipsUp); bnd >= 0; bnd--)
        {
            if (total[bnd] < 5) continue;
            if (light[bnd] < total[bnd] * A.HemShirtShare) break;   // too little shirt: past the hem
            hem = D.FootUp + bnd * D.Height / bands;
        }
        // Plus a little margin for the hem's own rim.
        w.Hem = over?.Hem is float hf ? D.FootUp + hf * D.Height
            : hem < float.MaxValue ? hem - A.HemMargin * D.Height : D.HipsUp - A.HemFallback * D.Height;
        w.Mesh = outMesh;
        wearers[key] = w;
        return w;
    }

    /// Skin texels: warm, fairly saturated and light (armour, cloth and leather are greyer or darker).
    static bool IsSkin(Image img, Vector2[] uvs, int v)
    {
        if (img == null || uvs.Length <= v) return false;
        var uv = uvs[v];
        var c = img.GetPixel(Mathf.Clamp((int)(uv.X * img.GetWidth()), 0, img.GetWidth() - 1), Mathf.Clamp((int)(uv.Y * img.GetHeight()), 0, img.GetHeight() - 1));
        return c.S > 0.2f && c.Luminance > 0.4f && (c.H < 0.12f || c.H > 0.95f);
    }

    static float ShoulderUp(Skeleton3D sk, Rig r)
    {
        var ys = Enumerable.Range(0, r.Parts.Length).Where(i => r.Parts[i] == Part.Shoulder).Select(i => r.Rest[i].Origin.Dot(r.Up)).ToList();
        return ys.Count > 0 ? ys.Average() : r.HipsUp + 0.3f * r.Height;
    }

    const string SkinCode = @"
shader_type spatial;
uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 col : source_color = vec4(1.0);
uniform float rough = 0.85;
uniform int group[64];
uniform int group_count = 0;
uniform bool torso = false;
uniform bool legs = false;
uniform bool feet = false;
uniform bool skirt = false;
uniform float belt = 0.0;
uniform float hem = 0.0;
uniform float ankle = 0.0;
uniform float neck = 1e9;
uniform float neck_column = 1e9;
uniform float shirt_lum = 0.3;
uniform float shirt_sat = 0.25;
varying vec4 g;      // weights: upper body, hips, thigh, shin
varying float g5;    // foot
varying float g6;    // arms
varying float g7;    // neck
varying float rest_h;
varying float shirt;
void vertex() {
    g = vec4(0.0); g5 = 0.0; g6 = 0.0; g7 = 0.0;
    for (int i = 0; i < 4; i++) {
        int b = int(BONE_INDICES[i]);
        if (b < group_count) {
            int k = group[b];
            if (k == 1) g.x += BONE_WEIGHTS[i];
            else if (k == 2) g.y += BONE_WEIGHTS[i];
            else if (k == 3) g.z += BONE_WEIGHTS[i];
            else if (k == 4) g.w += BONE_WEIGHTS[i];
            else if (k == 5) g5 += BONE_WEIGHTS[i];
            else if (k == 6) g6 += BONE_WEIGHTS[i];
            else if (k == 7) g7 += BONE_WEIGHTS[i];
        }
    }
    rest_h = UV2.x;
    shirt = UV2.y;
}
void fragment() {
    vec3 tc = texture(tex, UV).rgb;
    float waist = g.y + g.z;
    // Shirt, decided per pixel from the texture (pale and unsaturated) near flagged shirt vertices,
    // so no sliver survives where a triangle straddles the collar.
    // (thresholds are in sRGB, like the texture file; the sampler hands us linear colour)
    vec3 sc = pow(tc, vec3(1.0 / 2.2));
    float mx = max(sc.r, max(sc.g, sc.b)), mn = min(sc.r, min(sc.g, sc.b));
    float sat = mx > 0.0 ? (mx - mn) / mx : 0.0;
    float lum = dot(sc, vec3(0.2126, 0.7152, 0.0722));
    bool shirt_px = (shirt > 0.0 || g.x + g6 + g7 > 0.3) && lum > shirt_lum && sat < shirt_sat;
    bool hide = false;
    // Shoulders and trunk go under the collar up to `neck`; the neck column itself (inside the
    // collar ring) only below `neck_column`, so no gap opens under the chin.
    if (torso && ((g.x + g6 > 0.5 && rest_h < neck) || (g7 > 0.5 && rest_h < neck_column) || waist > 0.5 && rest_h > hem || shirt_px && rest_h > hem)) hide = true;
    if (legs && (waist > 0.5 && rest_h <= hem || g.w > 0.5 && rest_h > ankle)) hide = true;
    if (skirt && (waist + g.x > 0.5 && rest_h <= belt || g.w > 0.5 && rest_h > ankle)) hide = true;
    if (feet && (g5 > 0.5 || g.w > 0.5 && rest_h <= ankle)) hide = true;
    if (hide) discard;
    ALBEDO = tc * col.rgb;
    ROUGHNESS = rough;
}";
    static Shader skinShader;

    /// Hides the wearer's own clothes and skin under whatever armour regions are worn: the whole
    /// shirt under a body piece, the legs under leggings, the feet under boots.
    public static void HideCovered(string key, Skeleton3D skel, MeshInstance3D body, HashSet<Region> covered)
    {
        if (key == null || body?.Skin == null || body.Mesh is not ArrayMesh) return;
        var w = WearerOf(key, skel, body);
        bool torso = covered.Contains(Region.Torso) || covered.Contains(Region.Robe);
        bool legs = covered.Contains(Region.Legs) || covered.Contains(Region.Robe);
        bool feet = covered.Contains(Region.Feet);
        bool skirt = covered.Contains(Region.Skirt);
        if (body.Mesh != w.Mesh) body.Mesh = w.Mesh;
        skinShader ??= new Shader { Code = SkinCode };
        for (int s = 0; s < w.Mesh.GetSurfaceCount(); s++)
        {
            if (!torso && !legs && !feet && !skirt) { body.SetSurfaceOverrideMaterial(s, null); continue; }
            var src = w.Mesh.SurfaceGetMaterial(s) as BaseMaterial3D;
            if (src == null) continue;
            var m = new ShaderMaterial { Shader = skinShader };
            m.SetShaderParameter("tex", src.AlbedoTexture);
            m.SetShaderParameter("col", src.AlbedoColor);
            m.SetShaderParameter("rough", src.Roughness);
            m.SetShaderParameter("group", w.Group.Take(64).ToArray());
            m.SetShaderParameter("group_count", Math.Min(64, w.Group.Length));
            m.SetShaderParameter("torso", torso);
            m.SetShaderParameter("legs", legs);
            m.SetShaderParameter("feet", feet);
            m.SetShaderParameter("hem", w.Hem);
            m.SetShaderParameter("ankle", w.Ankle);
            m.SetShaderParameter("neck", w.Neck);
            m.SetShaderParameter("neck_column", w.NeckColumn);
            m.SetShaderParameter("shirt_lum", EquipTuning.T.Armour.ShirtLuminance);
            m.SetShaderParameter("shirt_sat", EquipTuning.T.Armour.ShirtSaturation);
            m.SetShaderParameter("skirt", skirt);
            m.SetShaderParameter("belt", w.Belt);
            body.SetSurfaceOverrideMaterial(s, m);
        }
    }

    // ------------------------------------------------------------------ building

    static readonly Dictionary<string, ArrayMesh> cache = new();

    /// Dresses the wearer (its model root and skeleton) in the item's outfit part. Null if it can't.
    public static MeshInstance3D MainBody(Node model) => MainMesh(model);

    public static MeshInstance3D Attach(string wearerKey, Node3D model, Skeleton3D skel, ItemDef d, Region? regionOverride = null)
    {
        var src = SourceOf(d); var region = regionOverride ?? RegionOf(d);
        if (src == null || region == null || wearerKey == null || !Assets.HasModel(src)) return null;
        var body = MainMesh(model);
        if (body == null) return null;
        string key = $"{wearerKey}|{src}|{region}";
        if (!cache.TryGetValue(key, out var mesh))
        {
            try { mesh = Build(src, region.Value, skel, body, WearerOf(wearerKey, skel, body)); }
            catch (Exception e) { GD.PrintErr($"[ArmourFit] {key}: {e}"); mesh = null; }
            cache[key] = mesh;
        }
        if (mesh == null) return null;
        var mi = new MeshInstance3D { Name = "Armour_" + d.Slot, Mesh = mesh, Skin = body.Skin, Transform = body.Transform };
        body.GetParent().AddChild(mi);
        mi.Skeleton = mi.GetPathTo(skel);
        Recolour(mi, d);
        return mi;
    }

    static ArrayMesh Build(string srcKey, Region region, Skeleton3D dstSkel, MeshInstance3D dstBody, Wearer W)
    {
        var root = Assets.Model(srcKey, 0f);
        try
        {
            var srcSkel = Assets.Find<Skeleton3D>(root);
            var srcMi = MainMesh(root);
            if (srcSkel == null || srcMi == null) return null;
            var S = RigOf(srcSkel, null); var D = RigOf(dstSkel, dstBody.Owner);
            var (sBone, sXf) = Binds(srcMi.Skin, srcSkel);
            var (dBone, dXf) = Binds(dstBody.Skin, dstSkel);
            // Source skeleton bone -> wearer bone -> wearer bind index. Limbs match by name; the spine
            // chain between hips and head is matched by position, since rigs label it inconsistently.
            var toDst = MapBones(srcSkel, dstSkel);
            for (int i = 0; i < toDst.Length; i++) if (toDst[i] >= 0) S.Parts[i] = D.Parts[toDst[i]];
            var dstBind = new Dictionary<int, int>();
            for (int i = 0; i < dBone.Length; i++) if (dBone[i] >= 0 && !dstBind.ContainsKey(dBone[i])) dstBind[dBone[i]] = i;
            // Each source bone is carried onto its wearer bone: the whole source is first turned to face
            // the way the wearer faces, then each bone is swung so it points along the wearer's bone, and
            // offsets are scaled by how much bigger the wearer is (torso and legs separately). This works
            // whatever axis conventions each rig's bones use.
            // Scale by overall height (legs by leg length): a torso-length ratio would inflate the chest.
            float kUp = D.Height / Mathf.Max(S.Height, 1e-4f), kLo = D.Lower / Mathf.Max(S.Lower, 1e-4f);
            var face = Facing(dstSkel, D) * Facing(srcSkel, S).Inverse();
            var cS = Centroids(srcMi, sBone, sXf, S.Rest.Length);
            var cD = Centroids(dstBody, dBone, dXf, D.Rest.Length);
            var carry = new Transform3D[S.Rest.Length];
            var swing = new Basis[S.Rest.Length];
            for (int i = 0; i < carry.Length; i++)
            {
                int j = toDst[i];
                int par = srcSkel.GetBoneParent(i);
                swing[i] = par >= 0 ? swing[par] : Basis.Identity;
                if (j < 0) { carry[i] = par >= 0 ? carry[par] : new Transform3D(face, Vector3.Zero); continue; }
                var ds = face * (Tip(srcSkel, S, i) - S.Rest[i].Origin);
                var dd = Tip(dstSkel, D, j) - D.Rest[j].Origin;
                if (ds.LengthSquared() > 1e-8f && dd.LengthSquared() > 1e-8f)
                {
                    ds = ds.Normalized(); dd = dd.Normalized();
                    var axis = ds.Cross(dd);
                    float ang = Mathf.Acos(Mathf.Clamp(ds.Dot(dd), -1f, 1f));
                    swing[i] = axis.LengthSquared() > 1e-10f ? new Basis(axis.Normalized(), ang) : Basis.Identity;
                }
                float k = S.Parts[i] is Part.UpLeg or Part.Leg or Part.Foot ? kLo : kUp;
                if (S.Parts[i] is Part.Hips or Part.Spine or Part.Neck or Part.Head)
                {
                    // Trunk: no swing (a hips bone's mesh hangs on both sides of its joint, so swinging it
                    // throws the fauld forward). Instead line up each segment's mesh centre with the body's:
                    // horizontally at the centroid of the vertices the bone drives, vertically at the joint.
                    swing[i] = Basis.Identity;
                    var bt = face.Scaled(Vector3.One * k);
                    var aS = Anchor(S.Rest[i].Origin, cS[i], S.Up);
                    var aD = Anchor(D.Rest[j].Origin, cD[j], D.Up);
                    carry[i] = new Transform3D(bt, aD - bt * aS);
                    continue;
                }
                var b = (swing[i] * face).Scaled(Vector3.One * k);
                carry[i] = new Transform3D(b, D.Rest[j].Origin - b * S.Rest[i].Origin);
            }

            var am = (ArrayMesh)srcMi.Mesh;
            var outMesh = new ArrayMesh();
            for (int s = 0; s < am.GetSurfaceCount(); s++)
            {
                if (am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
                var arr = am.SurfaceGetArrays(s);
                var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var norms = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var uvs = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var bones = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var idx = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (idx.Length == 0) idx = Enumerable.Range(0, verts.Length).ToArray();
                if (verts.Length == 0 || bones.Length < verts.Length) continue;
                int per = bones.Length / verts.Length;

                // The outfit's own texture, to tell its collar from the model's bare neck.
                var srcImg = ((srcMi.GetSurfaceOverrideMaterial(s) ?? srcMi.GetActiveMaterial(s) ?? am.SurfaceGetMaterial(s)) as BaseMaterial3D)?.AlbedoTexture?.GetImage();
                if (srcImg != null && srcImg.IsCompressed()) { srcImg = (Image)srcImg.Duplicate(); srcImg.Decompress(); }
                var inRegion = new bool[verts.Length];
                var outV = new Vector3[verts.Length]; var outN = new Vector3[verts.Length];
                var outB = new int[verts.Length * 4]; var outW = new float[verts.Length * 4];
                for (int v = 0; v < verts.Length; v++)
                {
                    var infl = Enumerable.Range(0, per).Select(j => (b: bones[v * per + j], w: weights[v * per + j]))
                        .Where(x => x.b >= 0 && x.b < sBone.Length && sBone[x.b] >= 0 && x.w > 0).OrderByDescending(x => x.w).Take(4).ToArray();
                    if (infl.Length == 0) continue;
                    var p = sXf[infl[0].b] * verts[v];
                    var part = S.Parts[sBone[infl[0].b]];
                    // Carry over: blend of each influencing bone's source->wearer transform.
                    var q = Vector3.Zero; var nq = Vector3.Zero; float sum = 0;
                    var acc = new List<(int bind, float w)>();
                    var n0 = norms.Length > v ? sXf[infl[0].b].Basis * norms[v] : Vector3.Up;
                    foreach (var (b, w) in infl)
                    {
                        int sb = sBone[b];
                        q += (carry[sb] * p) * w;
                        nq += (carry[sb].Basis * n0) * w;
                        sum += w;
                        if (toDst[sb] >= 0 && dstBind.TryGetValue(toDst[sb], out var db)) acc.Add((db, w));
                    }
                    q /= sum;
                    if (acc.Count == 0) continue;
                    // Split by height on the wearer: the torso piece reaches down to the wearer's shirt hem
                    // (so the whole shirt can be hidden), leggings start there, boots take the ankles.
                    float hq = q.Dot(D.Up);
                    inRegion[v] = region switch
                    {
                        Region.Torso => part is Part.Spine or Part.Shoulder or Part.UpperArm or Part.ForeArm
                            || part == Part.Neck && hq < W.Collar && !IsSkin(srcImg, uvs, v)
                            || part is Part.Hips or Part.UpLeg && hq > W.Hem - EquipTuning.T.Armour.TorsoOverlap * W.H,
                        Region.Legs => part is Part.Hips or Part.UpLeg && hq <= W.Hem + EquipTuning.T.Armour.LegsOverlap * W.H || part == Part.Leg && hq > W.Ankle,
                        Region.Skirt => part is Part.Hips or Part.UpLeg or Part.Spine && hq <= W.Belt || part == Part.Leg && hq > W.Ankle,
                        Region.Robe => part is Part.Spine or Part.Shoulder or Part.UpperArm or Part.ForeArm or Part.Hips or Part.UpLeg || part == Part.Leg && hq > W.Ankle,
                        _ => part == Part.Foot || part == Part.Leg && hq <= W.Ankle + EquipTuning.T.Armour.LegsOverlap * W.H,
                    };
                    var merged = acc.GroupBy(x => x.bind).Select(g => (bind: g.Key, w: g.Sum(x => x.w))).OrderByDescending(x => x.w).Take(4).ToArray();
                    float ws = merged.Sum(x => x.w);
                    // Into the wearer's mesh space: undo the blend of its bind transforms.
                    var blend = new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero);
                    for (int j = 0; j < merged.Length; j++)
                    {
                        float w = merged[j].w / ws;
                        outB[v * 4 + j] = merged[j].bind; outW[v * 4 + j] = w;
                        var bx = dXf[merged[j].bind];
                        blend = new Transform3D(new Basis(blend.Basis.Column0 + bx.Basis.Column0 * w, blend.Basis.Column1 + bx.Basis.Column1 * w, blend.Basis.Column2 + bx.Basis.Column2 * w), blend.Origin + bx.Origin * w);
                    }
                    var inv = blend.AffineInverse();
                    outV[v] = inv * q;
                    outN[v] = (inv.Basis * nq).Normalized();
                }

                // Keep triangles mostly in the region, then compact the vertex list.
                var remap = new Dictionary<int, int>();
                var keepIdx = new List<int>();
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                    int votes = (inRegion[a] ? 1 : 0) + (inRegion[b] ? 1 : 0) + (inRegion[c] ? 1 : 0);
                    if (votes < 2) continue;
                    foreach (var x in new[] { a, b, c })
                    {
                        if (!remap.TryGetValue(x, out var nx)) { nx = remap.Count; remap[x] = nx; }
                        keepIdx.Add(nx);
                    }
                }
                if (keepIdx.Count == 0) continue;
                var order = remap.OrderBy(x => x.Value).Select(x => x.Key).ToArray();
                var na = new Godot.Collections.Array();
                na.Resize((int)Mesh.ArrayType.Max);
                na[(int)Mesh.ArrayType.Vertex] = order.Select(i => outV[i]).ToArray();
                na[(int)Mesh.ArrayType.Normal] = order.Select(i => outN[i]).ToArray();
                if (uvs.Length == verts.Length) na[(int)Mesh.ArrayType.TexUV] = order.Select(i => uvs[i]).ToArray();
                na[(int)Mesh.ArrayType.Bones] = order.SelectMany(i => Enumerable.Range(0, 4).Select(j => outB[i * 4 + j])).ToArray();
                na[(int)Mesh.ArrayType.Weights] = order.SelectMany(i => Enumerable.Range(0, 4).Select(j => outW[i * 4 + j])).ToArray();
                na[(int)Mesh.ArrayType.Index] = keepIdx.ToArray();
                outMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, na);
                var mat = srcMi.GetSurfaceOverrideMaterial(s) ?? srcMi.GetActiveMaterial(s) ?? am.SurfaceGetMaterial(s);
                if (mat != null) outMesh.SurfaceSetMaterial(outMesh.GetSurfaceCount() - 1, mat);
            }
            return outMesh.GetSurfaceCount() > 0 ? outMesh : null;
        }
        finally { root.Free(); }
    }

    // ------------------------------------------------------------------ colour

    /// Armour keeps its true shape but always wins over the clothes beneath it: each vertex is
    /// nudged a few centimetres towards the camera for the depth test only, so a flared shirt hem
    /// that bulges past the armour is still drawn under it.
    const string ArmourCode = @"
shader_type spatial;
render_mode cull_disabled;
uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float amount = 0.0;
uniform float metal = 0.0;
uniform float rough = 0.8;
uniform float pull = 0.04;
void vertex() {
    vec4 vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vp.xyz -= normalize(vp.xyz) * pull;
    POSITION = PROJECTION_MATRIX * vp;
}
void fragment() {
    vec3 c = texture(tex, UV).rgb;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    ALBEDO = mix(c, clamp(tint.rgb * (0.35 + l * 1.5), 0.0, 1.0), amount);
    METALLIC = metal;
    ROUGHNESS = rough;
}";
    static Shader armourShader;

    /// Metal and dyed gear take the item's colour over the outfit's own texture, keeping its detail.
    static void Recolour(MeshInstance3D mi, ItemDef d)
    {
        bool metalItem = EquipTuning.T.Outfits.TryGetValue(d.Icon, out var o) && o.Metal;
        var rc = metalItem ? EquipTuning.T.Metal : EquipTuning.T.Cloth;
        bool dyed = d.Tint != Colors.White;
        armourShader ??= new Shader { Code = ArmourCode };
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            var src = mi.Mesh.SurfaceGetMaterial(s) as BaseMaterial3D;
            var m = new ShaderMaterial { Shader = armourShader };
            m.SetShaderParameter("tex", src?.AlbedoTexture);
            m.SetShaderParameter("tint", d.Tint);
            m.SetShaderParameter("amount", dyed ? rc.Amount : 0f);
            m.SetShaderParameter("metal", rc.Metallic);
            m.SetShaderParameter("rough", rc.Roughness >= 0 ? rc.Roughness : src?.Roughness ?? 0.8f);
            m.SetShaderParameter("pull", EquipTuning.T.Armour.Pull);
            mi.SetSurfaceOverrideMaterial(s, m);
        }
    }
}
