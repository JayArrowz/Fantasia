using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia.Client;

/// Loads generated art (assets/models/*.glb, assets/textures/*.png, assets/anims/*.glb).
/// Everything is optional: callers fall back to procedural meshes when a key is missing.
public static class Assets
{
    static readonly Dictionary<string, PackedScene> Scenes = new();
    static readonly Dictionary<string, Texture2D> Textures = new();
    static readonly Dictionary<string, Aabb> Bounds = new();

    public static bool HasModel(string key) => key != null && LoadScene(key) != null;

    static PackedScene LoadScene(string key)
    {
        if (key == null) return null;
        if (Scenes.TryGetValue(key, out var s)) return s;
        string path = $"res://assets/models/{key}.glb";
        s = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
        Scenes[key] = s;
        return s;
    }

    public static Texture2D Tex(string name)
    {
        if (Textures.TryGetValue(name, out var t)) return t;
        string path = $"res://assets/textures/{name}.png";
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        Textures[name] = t;
        return t;
    }

    /// Instantiates a model scaled so its height is `height` (or its largest horizontal extent is
    /// `footprint` if height <= 0), centred on X/Z with its base at y=0. Returns null if missing.
    public static Node3D Model(string key, float height, float footprint = 0, Color? tint = null)
    {
        var scene = LoadScene(key);
        if (scene == null) return null;
        var inst = scene.Instantiate<Node3D>();
        var root = new Node3D { Name = key };
        root.AddChild(inst);
        var skel = Find<Skeleton3D>(inst);
        if (skel != null)
        {
            // Skinned meshes render through the skeleton, so mesh AABBs are meaningless here.
            // Rigged characters are authored at real-world size with their origin at the feet.
            if (!Bounds.TryGetValue(key, out var sb))
            {
                sb = SkeletonBounds(inst, skel);
                Bounds[key] = sb;
            }
            float ks = height > 0 && sb.Size.Y > 0.01f ? height / sb.Size.Y : 1f;
            inst.Scale = Vector3.One * ks;
            inst.Position = new Vector3(0, -sb.Position.Y * ks, 0);
            if (tint.HasValue) ApplyTint(inst, tint.Value);
            return root;
        }
        if (!Bounds.TryGetValue(key, out var box))
        {
            box = ComputeAabb(inst, Transform3D.Identity);
            Bounds[key] = box;
        }
        float s = 1f;
        if (height > 0 && box.Size.Y > 0.0001f) s = height / box.Size.Y;
        else if (footprint > 0) s = footprint / Mathf.Max(0.0001f, Mathf.Max(box.Size.X, box.Size.Z));
        inst.Scale = Vector3.One * s;
        var c = box.GetCenter();
        inst.Position = new Vector3(-c.X * s, -box.Position.Y * s, -c.Z * s);
        if (tint.HasValue) ApplyTint(inst, tint.Value);
        return root;
    }

    public static Aabb ModelBounds(string key) => Bounds.TryGetValue(key, out var b) ? b : new Aabb();

    /// For flat items (rugs): rotates the model so its thinnest axis points up, then scales its
    /// largest horizontal extent to `footprint`. Lies on y=0.
    public static Node3D ModelFlat(string key, float footprint)
    {
        var scene = LoadScene(key);
        if (scene == null) return null;
        var inst = scene.Instantiate<Node3D>();
        var root = new Node3D { Name = key };
        var pivot = new Node3D();
        root.AddChild(pivot);
        pivot.AddChild(inst);
        if (!Bounds.TryGetValue(key, out var box))
        {
            box = ComputeAabb(inst, Transform3D.Identity);
            Bounds[key] = box;
        }
        var sz = box.Size;
        var rot = Vector3.Zero;
        float a = sz.X, b = sz.Z, thin = sz.Y;
        if (sz.Z < sz.Y && sz.Z <= sz.X) { rot = new Vector3(90, 0, 0); a = sz.X; b = sz.Y; thin = sz.Z; }
        else if (sz.X < sz.Y && sz.X < sz.Z) { rot = new Vector3(0, 0, 90); a = sz.Y; b = sz.Z; thin = sz.X; }
        float s = footprint / Mathf.Max(0.0001f, Mathf.Max(a, b));
        inst.Scale = Vector3.One * s;
        inst.Position = -box.GetCenter() * s;
        pivot.RotationDegrees = rot;
        pivot.Position = new Vector3(0, thin * s * 0.5f + 0.01f, 0);
        return root;
    }

    /// For held items: reorients the model so its longest axis is +Y and scales that axis to `length`.
    /// Base at y=0, centred on X/Z.
    static readonly Dictionary<string, float> WidestAt = new();

    /// Models whose blade is broader than their hilt, which fools the automatic hilt detection.
    static readonly HashSet<string> FlipGrip = new() { "item_scimitar" };

    /// Position (0..1 along the longest axis, from its minimum end) of the widest cross-section.
    /// Used to tell a sword's hilt end (crossguard) or a hammer's head end from the mesh itself.
    static readonly Dictionary<string, List<Vector3>> SamplePoints = new();

    /// Up to ~4000 mesh vertices per model in the instance's own space (cached per key).
    static List<Vector3> Samples(string key, Node3D inst)
    {
        if (SamplePoints.TryGetValue(key, out var cached)) return cached;
        var pts = new List<Vector3>();
        void Walk(Node n, Transform3D xf)
        {
            var local = n is Node3D n3 && n != inst ? xf * n3.Transform : xf;
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int sfc = 0; sfc < mi.Mesh.GetSurfaceCount(); sfc++)
                {
                    var verts = mi.Mesh.SurfaceGetArrays(sfc)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    for (int v = 0; v < verts.Length; v += Math.Max(1, verts.Length / 4000)) pts.Add(local * verts[v]);
                }
            foreach (var c in n.GetChildren()) Walk(c, local);
        }
        Walk(inst, Transform3D.Identity);
        SamplePoints[key] = pts;
        return pts;
    }

    static float WidestFraction(string key, Node3D inst, int axis)
    {
        if (WidestAt.TryGetValue(key, out var f)) return f;
        const int bins = 24;
        var min = new float[bins]; var max = new float[bins];
        var min2 = new float[bins]; var max2 = new float[bins];
        for (int i = 0; i < bins; i++) { min[i] = min2[i] = float.MaxValue; max[i] = max2[i] = float.MinValue; }
        var pts = new List<Vector3>();
        void Walk(Node n, Transform3D xf)
        {
            var local = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int sfc = 0; sfc < mi.Mesh.GetSurfaceCount(); sfc++)
                {
                    var arr = mi.Mesh.SurfaceGetArrays(sfc);
                    var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    for (int v = 0; v < verts.Length; v += Math.Max(1, verts.Length / 4000)) pts.Add(local * verts[v]);
                }
            foreach (var c in n.GetChildren()) Walk(c, local);
        }
        Walk(inst, Transform3D.Identity);
        if (pts.Count == 0) { WidestAt[key] = 0.5f; return 0.5f; }
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in pts) { lo = Mathf.Min(lo, p[axis]); hi = Mathf.Max(hi, p[axis]); }
        int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
        foreach (var p in pts)
        {
            int b = Math.Clamp((int)((p[axis] - lo) / Mathf.Max(1e-6f, hi - lo) * bins), 0, bins - 1);
            min[b] = Mathf.Min(min[b], p[a1]); max[b] = Mathf.Max(max[b], p[a1]);
            min2[b] = Mathf.Min(min2[b], p[a2]); max2[b] = Mathf.Max(max2[b], p[a2]);
        }
        // Compare only the two end thirds: the crossguard/head end is wider than the tip end.
        // (Ignoring the middle stops a curved blade's bulge from being mistaken for a guard.)
        float Width(int b) => max[b] < min[b] ? 0 : Mathf.Max(max[b] - min[b], max2[b] - min2[b]);
        float lowEnd = 0, highEnd = 0;
        int third = bins / 3;
        for (int b = 0; b < third; b++) lowEnd = Mathf.Max(lowEnd, Width(b));
        for (int b = bins - third; b < bins; b++) highEnd = Mathf.Max(highEnd, Width(b));
        f = lowEnd >= highEnd ? 0.15f : 0.85f;
        WidestAt[key] = f;
        return f;
    }

    /// wideEndUp: true = the widest part (hammer head, staff orb) should be at the top, away from
    /// the grip; false = the widest part (crossguard) is near the grip at the bottom; null = leave as is.
    /// centerAt: fraction of the length (from the bottom) whose cross-section is centred on the Y axis,
    /// so a curved blade or hooked staff still has its handle where the hand is.
    public static Node3D ModelLongest(string key, float length, Color? tint = null, bool? wideEndUp = null, float? centerAt = null)
    {
        var scene = LoadScene(key);
        if (scene == null) return null;
        var inst = scene.Instantiate<Node3D>();
        var root = new Node3D { Name = key };
        var pivot = new Node3D();
        root.AddChild(pivot);
        pivot.AddChild(inst);
        if (!Bounds.TryGetValue(key, out var box))
        {
            box = ComputeAabb(inst, Transform3D.Identity);
            Bounds[key] = box;
        }
        var sz = box.Size;
        // Rotate so the longest axis maps to +Y.
        var rot = Vector3.Zero;
        float along = sz.Y;
        if (sz.X > sz.Y && sz.X >= sz.Z) { rot = new Vector3(0, 0, 90); along = sz.X; }
        else if (sz.Z > sz.Y && sz.Z > sz.X) { rot = new Vector3(90, 0, 0); along = sz.Z; }
        float s = along > 0.0001f ? length / along : 1f;
        inst.Scale = Vector3.One * s;
        inst.Position = -box.GetCenter() * s;
        bool flip = false;
        if (wideEndUp.HasValue)
        {
            int axis = rot.Z != 0 ? 0 : rot.X != 0 ? 2 : 1;
            float wf = WidestFraction(key, inst, axis);
            // After the pivot rotation, where does the model's "min" end of that axis land?
            // Rotating +90 about Z maps +X to +Y; +90 about X maps +Z to -Y; identity keeps +Y.
            bool minEndIsBottom = axis != 2;
            float widestFromBottom = minEndIsBottom ? wf : 1f - wf;
            bool wideIsUp = widestFromBottom > 0.5f;
            if (FlipGrip.Contains(key)) wideIsUp = !wideIsUp;
            flip = wideIsUp != wideEndUp.Value;
        }
        // Align the long axis to +Y first, then (optionally) turn it end-over-end about Z.
        var align = Basis.FromEuler(rot * (Mathf.Pi / 180f));
        pivot.Basis = flip ? new Basis(Vector3.Back, Mathf.Pi) * align : align;
        pivot.Position = new Vector3(0, length * 0.5f, 0);
        if (centerAt.HasValue)
        {
            // Local points are in the unscaled instance space; apply inst scale/offset then the pivot.
            float y0 = centerAt.Value * length, band = 0.06f * length;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            var toRoot = new Transform3D(pivot.Basis, pivot.Position) * new Transform3D(Basis.Identity.Scaled(Vector3.One * s), inst.Position);
            foreach (var p in Samples(key, inst))
            {
                var q = toRoot * p;
                if (Mathf.Abs(q.Y - y0) > band) continue;
                x0 = Mathf.Min(x0, q.X); x1 = Mathf.Max(x1, q.X); z0 = Mathf.Min(z0, q.Z); z1 = Mathf.Max(z1, q.Z);
            }
            if (x1 >= x0) pivot.Position -= new Vector3((x0 + x1) * 0.5f, 0, (z0 + z1) * 0.5f);
        }
        if (tint.HasValue) ApplyTint(inst, tint.Value);
        return root;
    }

    /// Bounds of a rig from its bone rest positions (feet to head end), in the model root's space.
    static Aabb SkeletonBounds(Node3D inst, Skeleton3D skel)
    {
        var xf = Transform3D.Identity;
        for (Node n = skel; n != null && n != inst.GetParent(); n = n.GetParent())
            if (n is Node3D n3) xf = n3.Transform * xf;
        Aabb b = new();
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            var p = xf * skel.GetBoneGlobalRest(i).Origin;
            b = i == 0 ? new Aabb(p, Vector3.Zero) : b.Expand(p);
        }
        // Bones stop at the scalp and ankles; pad slightly so height matches the mesh.
        var size = b.Size;
        float feet = Mathf.Min(b.Position.Y, 0f);
        float top = b.End.Y + size.Y * 0.04f;
        return new Aabb(new Vector3(b.Position.X, feet, b.Position.Z), new Vector3(size.X, top - feet, size.Z));
    }

    public static Aabb ComputeAabb(Node node, Transform3D parent)
    {
        var xf = node is Node3D n3 ? parent * n3.Transform : parent;
        Aabb total = new();
        bool any = false;
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            total = xf * mi.Mesh.GetAabb();
            any = true;
        }
        foreach (var child in node.GetChildren())
        {
            var b = ComputeAabb(child, xf);
            if (b.Size == Vector3.Zero && b.Position == Vector3.Zero) continue;
            total = any ? total.Merge(b) : b;
            any = true;
        }
        return total;
    }

    /// Untextured metal items get a tinted metallic material; textured models get multiplied.
    public static void ApplyTint(Node node, Color tint, bool metal = true)
    {
        foreach (var mi in AllMeshes(node))
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Metallic = metal ? 0.75f : 0.05f,
                Roughness = metal ? 0.35f : 0.8f,
            };
            mi.MaterialOverride = mat;
        }
    }

    public static void Modulate(Node node, Color tint)
    {
        if (tint == Colors.White) return;
        foreach (var mi in AllMeshes(node))
        {
            for (int s = 0; s < mi.GetSurfaceOverrideMaterialCount(); s++)
            {
                var src = mi.GetActiveMaterial(s) as BaseMaterial3D;
                if (src == null) continue;
                var copy = (BaseMaterial3D)src.Duplicate();
                copy.AlbedoColor = copy.AlbedoColor * tint;
                mi.SetSurfaceOverrideMaterial(s, copy);
            }
        }
    }

    public static IEnumerable<MeshInstance3D> AllMeshes(Node node)
    {
        if (node is MeshInstance3D mi) yield return mi;
        foreach (var c in node.GetChildren())
            foreach (var m in AllMeshes(c)) yield return m;
    }

    public static T Find<T>(Node node) where T : class
    {
        if (node is T t) return t;
        foreach (var c in node.GetChildren())
        {
            var r = Find<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    public static StandardMaterial3D TexturedMaterial(string tex, Color fallback, float uvScale = 1f, bool triplanar = true)
    {
        var mat = new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 0.9f };
        var t = Tex(tex);
        if (t != null)
        {
            mat.AlbedoTexture = t;
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
            if (triplanar)
            {
                mat.Uv1Triplanar = true;
                mat.Uv1WorldTriplanar = true;
                mat.Uv1Scale = Vector3.One * uvScale;
            }
        }
        else mat.AlbedoColor = fallback;
        return mat;
    }
}

/// Shared skeletal animation clips (assets/anims/<name>.glb) retargeted onto any Meshy-rigged character.
/// Clips were authored on a separately rigged skeleton whose bone axes differ from each character's,
/// so rotations are retargeted in model space: delta-from-rest on the source, reapplied on the target rest.
public static class AnimLib
{
    public static readonly string[] Names = { "idle", "walk", "run", "slash", "stab", "heavy", "shoot", "cast", "hit", "death", "pickup", "combat_idle", "block", "chop" };
    const float Fps = 30f;

    sealed class Source
    {
        public Animation Anim;
        public Skeleton3D Skel;
        public Quaternion SceneRot;        // rotation of skeleton space in the scene
        public Transform3D SceneXf;
    }

    static readonly Dictionary<string, Source> Sources = new();
    static bool loaded;
    static readonly Dictionary<string, AnimationLibrary> Libraries = new();

    static Transform3D SceneTransform(Node node, Node stopAt)
    {
        var xf = Transform3D.Identity;
        for (var n = node; n != null && n != stopAt; n = n.GetParent())
            if (n is Node3D n3) xf = n3.Transform * xf;
        return xf;
    }

    static void LoadSources()
    {
        if (loaded) return;
        loaded = true;
        foreach (var name in Names)
        {
            string path = $"res://assets/anims/{name}.glb";
            if (!ResourceLoader.Exists(path)) continue;
            var inst = ResourceLoader.Load<PackedScene>(path)?.Instantiate();
            if (inst == null) continue;
            var ap = Assets.Find<AnimationPlayer>(inst);
            var sk = Assets.Find<Skeleton3D>(inst);
            var animName = ap?.GetAnimationList().FirstOrDefault(n => n != "RESET");
            if (sk == null || animName == null) { inst.Free(); continue; }
            var xf = SceneTransform(sk, inst);
            var src = new Source { Anim = (Animation)ap.GetAnimation(animName).Duplicate(true), SceneXf = xf, SceneRot = xf.Basis.GetRotationQuaternion() };
            sk.GetParent().RemoveChild(sk);
            inst.Free();
            src.Skel = sk;
            Sources[name] = src;
        }
        GD.Print($"[AnimLib] Loaded {Sources.Count} animation clips.");
    }

    public static bool Available { get { LoadSources(); return Sources.Count > 0; } }

    /// Adds an AnimationPlayer with every shared clip retargeted to `skel`. Cached per model key.
    public static AnimationPlayer Attach(Node3D modelRoot, Skeleton3D skel, string cacheKey)
    {
        LoadSources();
        if (Sources.Count == 0 || skel == null) return null;
        var player = new AnimationPlayer { Name = "SharedAnims" };
        modelRoot.AddChild(player);
        player.RootNode = player.GetPathTo(modelRoot);
        if (!Libraries.TryGetValue(cacheKey, out var lib))
        {
            string skelPath = modelRoot.GetPathTo(skel).ToString();
            var dstXf = SceneTransform(skel, modelRoot);
            lib = new AnimationLibrary();
            foreach (var (name, src) in Sources)
            {
                var a = Retarget(src, skel, dstXf, skelPath);
                a.LoopMode = name is "idle" or "walk" or "run" or "combat_idle" ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                lib.AddAnimation(name, a);
            }
            Libraries[cacheKey] = lib;
        }
        player.AddAnimationLibrary("", lib);
        return player;
    }

    static Quaternion Rot(Basis b) => b.Orthonormalized().GetRotationQuaternion();

    static Animation Retarget(Source src, Skeleton3D dst, Transform3D dstXf, string skelPath)
    {
        var sa = src.Anim;
        var ss = src.Skel;
        int n = dst.GetBoneCount();
        var dstRot = Rot(dstXf.Basis);

        // Map target bones to source bones by name and locate source tracks.
        var srcIndex = new int[n];
        for (int b = 0; b < n; b++) srcIndex[b] = ss.FindBone(dst.GetBoneName(b));
        var rotTrack = new Dictionary<int, int>();
        int hipsPosTrack = -1;
        int srcHips = FindHips(ss), dstHips = FindHips(dst);
        for (int t = 0; t < sa.GetTrackCount(); t++)
        {
            int sb = ss.FindBone(sa.TrackGetPath(t).GetConcatenatedSubNames());
            if (sb < 0) continue;
            if (sa.TrackGetType(t) == Animation.TrackType.Rotation3D) rotTrack[sb] = t;
            else if (sa.TrackGetType(t) == Animation.TrackType.Position3D && sb == srcHips) hipsPosTrack = t;
        }

        // Rest globals in scene space.
        var srcRestG = new Quaternion[ss.GetBoneCount()];
        for (int b = 0; b < ss.GetBoneCount(); b++) srcRestG[b] = src.SceneRot * Rot(ss.GetBoneGlobalRest(b).Basis);
        var dstRestG = new Quaternion[n];
        for (int b = 0; b < n; b++) dstRestG[b] = dstRot * Rot(dst.GetBoneGlobalRest(b).Basis);

        // Vertical hips motion scale (scene units).
        float srcHipH = srcHips >= 0 ? (src.SceneXf * ss.GetBoneGlobalRest(srcHips).Origin).Y : 1f;
        float dstHipH = dstHips >= 0 ? (dstXf * dst.GetBoneGlobalRest(dstHips).Origin).Y : 1f;
        float ratio = Mathf.Abs(srcHipH) > 1e-4f ? dstHipH / srcHipH : 1f;

        var outA = new Animation { Length = sa.Length };
        var outTracks = new int[n];
        for (int b = 0; b < n; b++)
        {
            outTracks[b] = -1;
            if (srcIndex[b] < 0) continue;
            int tr = outA.AddTrack(Animation.TrackType.Rotation3D);
            outA.TrackSetPath(tr, new NodePath($"{skelPath}:{dst.GetBoneName(b)}"));
            outTracks[b] = tr;
        }
        int hipsOut = -1;
        if (dstHips >= 0 && hipsPosTrack >= 0)
        {
            hipsOut = outA.AddTrack(Animation.TrackType.Position3D);
            outA.TrackSetPath(hipsOut, new NodePath($"{skelPath}:{dst.GetBoneName(dstHips)}"));
        }

        var srcG = new Quaternion[ss.GetBoneCount()];
        var srcDone = new bool[ss.GetBoneCount()];
        var dstG = new Quaternion[n];
        var dstDone = new bool[n];
        int frames = Math.Max(2, (int)Mathf.Ceil(sa.Length * Fps) + 1);
        var dstInvXf = dstXf.AffineInverse();
        var srcRestHips = srcHips >= 0 ? ss.GetBoneRest(srcHips).Origin : Vector3.Zero;
        for (int f = 0; f < frames; f++)
        {
            double time = Math.Min(sa.Length, f / Fps);
            Array.Clear(srcDone);
            Array.Clear(dstDone);

            Quaternion SrcGlobal(int b)
            {
                if (srcDone[b]) return srcG[b];
                var local = rotTrack.TryGetValue(b, out int tr) ? sa.RotationTrackInterpolate(tr, time) : Rot(ss.GetBoneRest(b).Basis);
                int p = ss.GetBoneParent(b);
                srcG[b] = (p >= 0 ? SrcGlobal(p) : src.SceneRot) * local;
                srcDone[b] = true;
                return srcG[b];
            }

            Quaternion DstGlobal(int b)
            {
                if (dstDone[b]) return dstG[b];
                int sb = srcIndex[b];
                if (sb >= 0)
                {
                    var delta = SrcGlobal(sb) * srcRestG[sb].Inverse();
                    dstG[b] = (delta * dstRestG[b]).Normalized();
                }
                else
                {
                    int p = dst.GetBoneParent(b);
                    dstG[b] = (p >= 0 ? DstGlobal(p) : dstRot) * Rot(dst.GetBoneRest(b).Basis);
                }
                dstDone[b] = true;
                return dstG[b];
            }

            for (int b = 0; b < n; b++)
            {
                if (outTracks[b] < 0) continue;
                int p = dst.GetBoneParent(b);
                var parentG = p >= 0 ? DstGlobal(p) : dstRot;
                var local = (parentG.Inverse() * DstGlobal(b)).Normalized();
                outA.RotationTrackInsertKey(outTracks[b], time, local);
            }
            if (hipsOut >= 0)
            {
                var sp = sa.PositionTrackInterpolate(hipsPosTrack, time);
                // Keep clips in place: only vertical hip motion survives (crouches, falls).
                float dy = ((src.SceneXf * sp) - (src.SceneXf * srcRestHips)).Y * ratio;
                var restPos = dst.GetBoneRest(dstHips).Origin;
                var sceneRest = dstXf * restPos;
                var local = dstInvXf * (sceneRest + new Vector3(0, dy, 0));
                // Convert from skeleton space to the hips' parent space (hips is a root bone).
                outA.PositionTrackInsertKey(hipsOut, time, local);
            }
        }
        return outA;
    }

    static int FindHips(Skeleton3D s)
    {
        if (s == null) return -1;
        for (int i = 0; i < s.GetBoneCount(); i++)
        {
            var nm = s.GetBoneName(i).ToLowerInvariant();
            if (nm.Contains("hips") || nm.Contains("pelvis")) return i;
        }
        return s.GetBoneCount() > 0 ? 0 : -1;
    }

    public static int FindBone(Skeleton3D s, params string[] patterns)
    {
        if (s == null) return -1;
        for (int i = 0; i < s.GetBoneCount(); i++)
        {
            var nm = s.GetBoneName(i).ToLowerInvariant().Replace("_", "").Replace(".", "").Replace(" ", "");
            foreach (var p in patterns)
                if (nm.Contains(p)) return i;
        }
        return -1;
    }
}
