#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoxLab.Editor
{
    /// <summary>Optional self-contained art package. Deleting Assets/BoxLabArt restores geometric fallback.</summary>
    public static class ArtSetup
    {
        private const string ArtRoot = "Assets/BoxLabArt";
        private const string KayKitRoot = ArtRoot + "/ThirdParty/KayKitDungeon/";
        private const string ModelRoot = KayKitRoot + "Models/";
        private const string DungeonTexture = KayKitRoot + "Textures/dungeon_texture.png";
        private const string PrefabRoot = ArtRoot + "/Resources/BoxLabArt/";
        private const string MaterialRoot = ArtRoot + "/Materials/";

        /// <summary>Generator inputs which must be retained even before their prefabs exist.</summary>
        public static string[] SourceAssetPaths
        {
            get
            {
                var paths = new List<string>
                {
                    ModelRoot + "floor_tile_small.fbx",
                    ModelRoot + "floor_tile_small_broken_A.fbx",
                    ModelRoot + "barrier_half.fbx",
                    ModelRoot + "box_small.fbx",
                    ModelRoot + "wall_doorway.fbx",
                    DungeonTexture
                };
                paths.AddRange(MageArtSetup.SourceAssetPaths);
                return paths.ToArray();
            }
        }

        [MenuItem("Tools/BoxLab/生成可选 CC0 美术预设", priority = 40)]
        public static void Build()
        {
            if (!Directory.Exists(ModelRoot))
            {
                Debug.Log("BoxLabArt is absent. Original geometric fallback remains available.");
                return;
            }
            Directory.CreateDirectory(PrefabRoot);
            Directory.CreateDirectory(MaterialRoot);
            AssetDatabase.Refresh();
            // All KayKit dungeon meshes keep their original UVs and one shared gradient atlas.
            // Stone and wood therefore retain the author's distinct colours and baked shading.
            var dungeon = PaletteMaterial("KayKitDungeon", DungeonTexture, Color.white);
            // The source slab is 2 x .15 x 2. Uniform .43 scaling leaves its original bevels
            // intact, a .14 cell gap, and a top at .043 for the separate functional inlays.
            BuildModel("Floor", "floor_tile_small.fbx", new Vector3(.86f, .0645f, .86f), -.0215f, dungeon);
            BuildModel("SurfaceFloor", "floor_tile_small.fbx", new Vector3(.86f, .0645f, .86f), -.0215f, dungeon);
            BuildModel("FragileFloor", "floor_tile_small_broken_A.fbx", new Vector3(.86f, .0645f, .86f), -.0215f, dungeon);
            // barrier_half is a genuinely low stone barrier (2 x 1.1 x .5), unlike wall_half,
            // whose four-unit height would require flattening the original stonework.
            BuildModel("LowWall", "barrier_half.fbx", new Vector3(.95f, .46f, .23f), .025f, dungeon);
            BuildModel("Crate", "box_small.fbx", new Vector3(.64f, .64f, .64f), .047f, dungeon);
            BuildModel("PortalFrame", "wall_doorway.fbx", new Vector3(.86f, 1.10f, .215f), .043f, dungeon, true);
            MageArtSetup.Build();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PaletteThumbnailBuilder.Build();
            foreach (var view in Object.FindObjectsOfType<BoardView>()) view.RefreshPreview();
            Debug.Log("BOXLAB_ART_READY: CC0 KayKit stone slabs, cracked slabs, low barriers, closed crates, open portal frames and mage generated.");
        }

        private static void BuildModel(string name, string source, Vector3 dimensions, float bottom, Material material, bool portalAperture = false)
        {
            string sourcePath = source.StartsWith("Assets/", System.StringComparison.Ordinal) ? source : ModelRoot + source;
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer != null)
            {
                importer.importAnimation = false;
                importer.isReadable = portalAperture;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!asset) { Debug.LogWarning("Optional source model not found: " + sourcePath); return; }
            var root = new GameObject(name);
            try
            {
                var model = Object.Instantiate(asset, root.transform);
                model.name = "KayKit CC0 " + Path.GetFileName(source);
                if (portalAperture) RemovePortalDoorLeaf(model);
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new System.InvalidOperationException("Source model has no renderers: " + sourcePath);
                Bounds bounds = BoundsOf(renderers);
                Vector3 size = bounds.size;
                Vector3 scale = new Vector3(dimensions.x / Mathf.Max(.001f, size.x), dimensions.y / Mathf.Max(.001f, size.y), dimensions.z / Mathf.Max(.001f, size.z));
                model.transform.localScale = Vector3.Scale(model.transform.localScale, scale);
                bounds = BoundsOf(renderers);
                model.transform.position += new Vector3(-bounds.center.x, bottom - bounds.min.y, -bounds.center.z);
                if (portalAperture)
                {
                    WidenPortalAperture(model, dimensions.x);
                    ValidatePortalOpening(model);
                }
                foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = name == "LowWall" || name == "Crate" || portalAperture
                        ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void RemovePortalDoorLeaf(GameObject model)
        {
            // Unlike the OBJ preview, this pinned FBX includes a separate wooden door child.
            // It shares the stone atlas, so filtering a material slot would remove valid stone.
            int removed = 0;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (part == model.transform || part.name != "wall_doorway_door") continue;
                Object.DestroyImmediate(part.gameObject);
                removed++;
            }
            if (removed != 1 || model.GetComponentsInChildren<MeshFilter>(true).Length != 1)
                throw new System.InvalidOperationException("The pinned portal FBX must contain one stone frame and one wall_doorway_door child.");
            Debug.Log("BOXLAB_ART_PORTAL_DOOR_REMOVED: wall_doorway_door removed; only the original stone frame remains.");
        }

        private static void WidenPortalAperture(GameObject model, float width)
        {
            // wall_doorway has a real opening, but its original inner width is only half its
            // outside width. Keep the outside footprint fixed and slim the jambs so a .64 box
            // fits a .70 base opening. At the .64 box's top, the arch still clears .665.
            // Only generated meshes change; source FBX and UVs stay intact.
            string meshRoot = ArtRoot + "/Meshes/";
            Directory.CreateDirectory(meshRoot);
            AssetDatabase.Refresh();
            int index = 0;
            float outer = width * .5f, originalInner = width * .25f, targetInner = .35f;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                var widened = Object.Instantiate(filter.sharedMesh);
                widened.name = "KayKit portal widened aperture " + index;
                Vector3[] vertices = widened.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = filter.transform.TransformPoint(vertices[i]);
                    float x = Mathf.Abs(point.x);
                    float mapped = x <= originalInner ? x * targetInner / originalInner
                        : targetInner + (x - originalInner) * (outer - targetInner) / (outer - originalInner);
                    point.x = Mathf.Sign(point.x) * mapped;
                    vertices[i] = filter.transform.InverseTransformPoint(point);
                }
                widened.vertices = vertices;
                widened.RecalculateNormals();
                widened.RecalculateBounds();
                string path = meshRoot + "PortalFrame-aperture-" + index++ + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved) { EditorUtility.CopySerialized(widened, saved); Object.DestroyImmediate(widened); }
                else { AssetDatabase.CreateAsset(widened, path); saved = widened; }
                EditorUtility.SetDirty(saved);
                filter.sharedMesh = saved;
            }
            Debug.Log("BOXLAB_ART_PORTAL_FRAME: " + index + " derived meshes; .70 base opening and .665 clearance at box top inside .86 footprint; original UV atlas retained.");
        }

        private static void ValidatePortalOpening(GameObject model)
        {
            // Check the imported and derived FBX itself, not the separate OBJ preview. These
            // two-sided triangle tests cover the box's centre, sides, and upper corners without
            // adding physics components or modifying any gameplay rule.
            float[] xs = { -.32f, 0, .32f };
            float[] ys = { .05f, .37f, .687f };
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                Vector3[] vertices = filter.sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
                int[] triangles = filter.sharedMesh.triangles;
                foreach (float x in xs)
                    foreach (float y in ys)
                    {
                        Vector3 origin = new Vector3(x, y, -1);
                        for (int i = 0; i < triangles.Length; i += 3)
                            if (PortalRayHitsTriangle(origin, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]))
                                throw new System.InvalidOperationException("PortalFrame blocks its box aperture at " + origin.ToString("F3") + ".");
                    }
            }
            Debug.Log("BOXLAB_ART_PORTAL_OPENING_CLEAR: 9 two-sided mesh rays clear the .64 box and the luminous field.");
        }

        private static bool PortalRayHitsTriangle(Vector3 origin, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 first = b - a, second = c - a;
            Vector3 cross = Vector3.Cross(Vector3.forward, second);
            float determinant = Vector3.Dot(first, cross);
            if (Mathf.Abs(determinant) < .0000001f) return false;
            float inverse = 1 / determinant;
            Vector3 relative = origin - a;
            float u = Vector3.Dot(relative, cross) * inverse;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(relative, first);
            float v = Vector3.Dot(Vector3.forward, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            float distance = Vector3.Dot(second, q) * inverse;
            return distance >= 0 && distance <= 2;
        }

        private static Bounds BoundsOf(Renderer[] renderers)
        {
            Bounds bounds = new Bounds();
            bool initialized = false;
            foreach (var renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh)
                {
                    Bounds local = filter.sharedMesh.bounds;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                Vector3 point = renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                                if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                                else bounds.Encapsulate(point);
                            }
                }
                else if (!initialized) { bounds = renderer.bounds; initialized = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static Material Material(string name, Color color)
        {
            string path = MaterialRoot + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            material.SetFloat("_Glossiness", .22f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * .025f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material PaletteMaterial(string name, string texturePath, Color tint)
        {
            var texture = PaletteTexture(texturePath, true);
            var material = Material(name, tint);
            material.mainTexture = texture;
            material.SetFloat("_Glossiness", .19f);
            material.SetFloat("_Metallic", 0);
            // Emission must use the same atlas or it would wash out the material boundaries.
            material.SetTexture("_EmissionMap", material.mainTexture);
            material.SetColor("_EmissionColor", tint * .015f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D PaletteTexture(string texturePath, bool smooth = false)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }

    }
}
#endif
