#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoxLab.Editor
{
    /// <summary>Bakes the official KayKit mage into an empty-handed, source-independent static pusher.</summary>
    public static class MageArtSetup
    {
        private const string SourceRoot = "Assets/BoxLabArt/ThirdParty/KayKitAdventurers/";
        private const string ModelPath = SourceRoot + "Models/Mage.fbx";
        private const string TexturePath = SourceRoot + "Textures/mage_texture.png";
        private const string MeshRoot = "Assets/BoxLabArt/Meshes/MagePusher/";
        private const string MaterialPath = "Assets/BoxLabArt/Materials/KayKitMage.mat";
        private const string PrefabPath = "Assets/BoxLabArt/Resources/BoxLabArt/Pusher.prefab";
        private const float TargetHeight = .90f, MaximumFootprint = .65f, GroundHeight = .047f;
        private static readonly HashSet<string> Accessories = new HashSet<string>(StringComparer.Ordinal)
        { "Spellbook", "Spellbook_open", "1H_Wand", "2H_Staff" };
        private static readonly HashSet<string> CharacterParts = new HashSet<string>(StringComparer.Ordinal)
        { "Mage_ArmLeft", "Mage_ArmRight", "Mage_Body", "Mage_Head", "Mage_LegLeft", "Mage_LegRight", "Mage_Hat", "Mage_Cape" };

        public static string[] SourceAssetPaths => new[] { ModelPath, TexturePath };

        private sealed class BakedPart
        {
            public string name;
            public Mesh mesh;
        }

        [MenuItem("Tools/BoxLab/生成空手法师人物", priority = 42)]
        public static void Build()
        {
            if (!File.Exists(ModelPath) || !File.Exists(TexturePath))
                throw new InvalidOperationException("KayKit mage source or atlas is missing. Restore the documented KayKitAdventurers source files.");
            Directory.CreateDirectory(MeshRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            ConfigureModelImport();
            var sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!sourceAsset) throw new InvalidOperationException("Could not import the KayKit Mage FBX.");
            Material material = CreateMaterial();
            var parts = new List<BakedPart>();
            GameObject source = null, result = null;
            try
            {
                source = Object.Instantiate(sourceAsset);
                source.name = "KayKit Mage temporary pose bake";
                source.hideFlags = HideFlags.HideAndDontSave;
                AnimationClip idle = null;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                    if (asset is AnimationClip clip && IsPlainIdle(clip.name)) { idle = clip; break; }
                if (!idle) throw new InvalidOperationException("Expected the source's ordinary Idle clip; Unarmed_Idle is a raised-fist combat pose and is intentionally not used.");
                idle.SampleAnimation(source, 0);

                int removed = 0;
                foreach (Transform node in source.GetComponentsInChildren<Transform>(true))
                    if (node && Accessories.Contains(PartName(node.name))) { Object.DestroyImmediate(node.gameObject); removed++; }
                if (removed != 4) throw new InvalidOperationException("Mage accessory layout changed: expected the four inspected book/wand/staff objects, found " + removed + ".");
                Transform hat = null;
                foreach (Transform node in source.GetComponentsInChildren<Transform>(true))
                    if (PartName(node.name) == "Mage_Hat") { hat = node; break; }
                if (!hat) throw new InvalidOperationException("Mage_Hat is missing from the supplied model.");
                // A uniform 10% reduction of this separate hat keeps its shape while allowing
                // the complete figure to be about .90 cells tall within a .65-cell footprint.
                hat.localScale *= .90f;

                var found = new HashSet<string>(StringComparer.Ordinal);
                foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    string name = PartName(renderer.name);
                    if (!CharacterParts.Contains(name) || !found.Add(name))
                        throw new InvalidOperationException("Unexpected or duplicate mage mesh: " + renderer.name);
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        mesh = new Mesh { name = name + " empty handed idle" };
                        skinned.BakeMesh(mesh);
                    }
                    else
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Missing mesh on " + name);
                        mesh = Object.Instantiate(filter.sharedMesh);
                        mesh.name = name + " empty handed idle";
                    }
                    parts.Add(new BakedPart { name = name, mesh = mesh });
                    BakeTransform(mesh, renderer.localToWorldMatrix);
                    mesh.bindposes = new Matrix4x4[0];
                    mesh.boneWeights = new BoneWeight[0];
                }
                if (found.Count != CharacterParts.Count) throw new InvalidOperationException("The mage must retain all eight inspected character parts, including the hat and cape.");

                // The cape is behind the body. Use that inspected relationship to determine
                // the imported forward axis, avoiding assumptions about FBX handedness.
                Bounds body = parts.Find(p => p.name == "Mage_Body").mesh.bounds;
                Bounds cape = parts.Find(p => p.name == "Mage_Cape").mesh.bounds;
                Vector3 forward = Vector3.ProjectOnPlane(body.center - cape.center, Vector3.up);
                if (forward.sqrMagnitude < .00001f) throw new InvalidOperationException("Cannot determine the mage's facing from its body and cape.");
                forward = Mathf.Abs(forward.z) >= Mathf.Abs(forward.x)
                    ? Vector3.forward * Mathf.Sign(forward.z) : Vector3.right * Mathf.Sign(forward.x);
                // FromToRotation is ambiguous for opposite vectors and can choose a half
                // turn around X, making the complete figure upside down. Facing is yaw only.
                Quaternion faceSouth = Quaternion.AngleAxis(Vector3.SignedAngle(forward, Vector3.back, Vector3.up), Vector3.up);
                foreach (BakedPart part in parts) BakeTransform(part.mesh, Matrix4x4.Rotate(faceSouth));

                Bounds bounds = BoundsOf(parts);
                float fit = Mathf.Min(TargetHeight / bounds.size.y, MaximumFootprint / Mathf.Max(bounds.size.x, bounds.size.z));
                Vector3 offset = new Vector3(-bounds.center.x * fit, GroundHeight - bounds.min.y * fit, -bounds.center.z * fit);
                Matrix4x4 fitting = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * fit);
                foreach (BakedPart part in parts) BakeTransform(part.mesh, fitting);
                bounds = BoundsOf(parts);
                if (Mathf.Max(bounds.size.x, bounds.size.z) > MaximumFootprint + .001f || bounds.size.y < .85f || bounds.size.y > .951f)
                    throw new InvalidOperationException("Mage fitted bounds are outside the intended single-cell size: " + bounds.size.ToString("F3"));
                float legY = (parts.Find(p => p.name == "Mage_LegLeft").mesh.bounds.center.y + parts.Find(p => p.name == "Mage_LegRight").mesh.bounds.center.y) * .5f;
                float bodyY = parts.Find(p => p.name == "Mage_Body").mesh.bounds.center.y;
                float headY = parts.Find(p => p.name == "Mage_Head").mesh.bounds.center.y;
                float hatY = parts.Find(p => p.name == "Mage_Hat").mesh.bounds.center.y;
                if (!(legY < bodyY && bodyY < headY && headY < hatY))
                    throw new InvalidOperationException("Mage pose is not upright: expected legs < body < head < hat, got " + legY + ", " + bodyY + ", " + headY + ", " + hatY);
                Debug.Log("BOXLAB_MAGE_UPRIGHT: legs=" + legY.ToString("F3") + ", body=" + bodyY.ToString("F3") + ", head=" + headY.ToString("F3") + ", hat=" + hatY.ToString("F3") + "; yaw=" + Vector3.SignedAngle(forward, Vector3.back, Vector3.up).ToString("F0"));

                result = new GameObject("Pusher");
                foreach (BakedPart part in parts)
                {
                    string meshPath = MeshRoot + part.name + ".asset";
                    var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (saved)
                    {
                        EditorUtility.CopySerialized(part.mesh, saved);
                        Object.DestroyImmediate(part.mesh);
                    }
                    else { AssetDatabase.CreateAsset(part.mesh, meshPath); saved = part.mesh; }
                    part.mesh = saved;
                    EditorUtility.SetDirty(saved);
                    var child = new GameObject(part.name);
                    child.transform.SetParent(result.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = saved;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                if (result.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0 || result.GetComponentsInChildren<Animator>(true).Length != 0 || result.GetComponentsInChildren<Animation>(true).Length != 0)
                    throw new InvalidOperationException("The final mage must be static and independent of its source rig.");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(result, PrefabPath);
                if (!prefab) throw new InvalidOperationException("Could not save the mage Pusher prefab.");
                AssetDatabase.SaveAssets();
                foreach (string dependency in AssetDatabase.GetDependencies(PrefabPath, true))
                    if (dependency.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The generated pusher unexpectedly depends on a source FBX: " + dependency);
                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (!AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(MeshRoot, StringComparison.Ordinal))
                        throw new InvalidOperationException("The generated pusher contains a mesh outside its own baked output folder.");
                Debug.Log("BOXLAB_MAGE_READY: empty-handed Idle, 4 accessories removed, 8 independent static meshes; bounds=" + bounds.size.ToString("F3") + "; faces South; no source FBX runtime dependency.");
            }
            finally
            {
                if (result) Object.DestroyImmediate(result);
                if (source) Object.DestroyImmediate(source);
                foreach (BakedPart part in parts) if (part.mesh && !EditorUtility.IsPersistent(part.mesh)) Object.DestroyImmediate(part.mesh);
            }
        }

        private static void ConfigureModelImport()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Mage source was not recognized as a model.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.optimizeGameObjects = false;
            importer.importBlendShapes = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            ModelImporterClipAnimation idle = null;
            foreach (var clip in importer.defaultClipAnimations)
                if (IsPlainIdle(clip.name) || IsPlainIdle(clip.takeName)) { idle = clip; break; }
            if (idle == null) throw new InvalidOperationException("The official mage source has no ordinary Idle animation take.");
            idle.name = "Idle";
            idle.loopTime = true;
            importer.clipAnimations = new[] { idle };
            importer.SaveAndReimport();
        }

        private static bool IsPlainIdle(string name) => string.Equals(PartName(name), "Idle", StringComparison.OrdinalIgnoreCase);
        private static string PartName(string name)
        {
            name = name ?? "";
            int separator = Mathf.Max(name.LastIndexOf(':'), name.LastIndexOf('|'));
            return separator >= 0 ? name.Substring(separator + 1) : name;
        }

        private static Material CreateMaterial()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Mage palette was not recognized as a texture.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Shader shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("The Built-in Standard shader is unavailable.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            else material.shader = shader;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            material.color = Color.white;
            material.mainTexture = texture;
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Glossiness", .18f);
            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", Color.white * .012f);
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BakeTransform(Mesh mesh, Matrix4x4 matrix)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            Vector3[] normals = mesh.normals;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            Vector4[] tangents = mesh.tangents;
            float handedness = matrix.determinant < 0 ? -1 : 1;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 direction = matrix.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w * handedness);
            }
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            if (handedness < 0)
            {
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3) { int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap; }
                mesh.triangles = triangles;
            }
            if (normals.Length != vertices.Length) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static Bounds BoundsOf(List<BakedPart> parts)
        {
            Bounds bounds = parts[0].mesh.bounds;
            for (int i = 1; i < parts.Count; i++) bounds.Encapsulate(parts[i].mesh.bounds);
            return bounds;
        }
    }
}
#endif
