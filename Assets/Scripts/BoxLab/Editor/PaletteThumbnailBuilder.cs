#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BoxLab.Editor
{
    /// <summary>Creates portable PNGs from the same models used in play, without touching the open scene.</summary>
    public static class PaletteThumbnailBuilder
    {
        private const string OutputRoot = "Assets/BoxLabArt/Resources/";
        private const int Resolution = 256;
        private static readonly string[] MaterialIds =
        {
            // Passage floor signs reuse these actual model portraits, including on a clean import.
            PlayerEditing.Player, PlayerEditing.Box,
            PlayerRules.Plain, PlayerRules.Goal, PlayerRules.Ice, PlayerRules.Arrow,
            PlayerRules.Plate, PlayerRules.RotationPlate, PlayerRules.Selective, PlayerRules.BoxOnly,
            PlayerRules.PortalEntrance, PlayerRules.PortalExit, PlayerRules.Hole,
            PlayerRules.Fragile,
            PlayerEditing.Wall, PlayerRules.Gate
        };

        [MenuItem("Tools/BoxLab/生成素材模型缩略图", priority = 41)]
        public static void Build()
        {
            Directory.CreateDirectory(OutputRoot + "BoxLabArt/Thumbnails");
            AssetDatabase.Refresh();
            foreach (string materialId in MaterialIds)
            {
                string path = OutputRoot + PlayerEditor.ThumbnailResourcePath(materialId) + ".png";
                byte[] png = Render(materialId);
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) throw new InvalidOperationException("Thumbnail import failed: " + path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Resolution;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("BOXLAB_THUMBNAILS_READY: " + MaterialIds.Length + " model thumbnails generated for the standalone player editor.");
        }

        private static byte[] Render(string materialId)
        {
            Scene originalScene = SceneManager.GetActiveScene();
            var preview = new PreviewRenderUtility();
            GameObject owner = null;
            Texture2D pixels = null;
            try
            {
                owner = new GameObject("BoxLab isolated palette thumbnail") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(owner);
                var board = owner.AddComponent<BoardView>();
                board.enabled = false; // Avoid its normal delayed scene-preview refresh.
                GameObject model = board.CreatePalettePreview(materialId, owner.transform);
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("No preview geometry for " + materialId);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Camera camera = preview.camera;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.aspect = 1;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 20;
                Vector3 eye = new Vector3(1.15f, 1.45f, -1.65f).normalized;
                camera.transform.SetPositionAndRotation(bounds.center + eye * 5, Quaternion.LookRotation(-eye, Vector3.up));
                // Fit projected bounds rather than rescaling the object: the chest and wall keep their proportions.
                float extent = .05f;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 delta = Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                            extent = Mathf.Max(extent, Mathf.Abs(Vector3.Dot(delta, camera.transform.right)), Mathf.Abs(Vector3.Dot(delta, camera.transform.up)));
                        }
                camera.orthographicSize = extent * 1.13f;
                preview.ambientColor = new Color(.55f, .57f, .61f);
                preview.lights[0].intensity = 1.15f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45, 30, 0);
                preview.lights[1].intensity = .65f;
                preview.lights[1].transform.rotation = Quaternion.Euler(35, 210, 0);
                // Render() installs a preview-scene lighting override. EndPreview() must restore
                // that context before Cleanup destroys the scene, including when rendering throws.
                // EndStaticPreview returns RGB24 in Unity 2022.3, so use the paired dynamic API
                // and read its returned texture to retain the transparent background.
                float size = Resolution / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
                preview.BeginPreview(new Rect(0, 0, size, size), GUIStyle.none);
                Texture rendered = null;
                try { preview.Render(false, false); }
                finally { rendered = preview.EndPreview(); }
                var target = rendered as RenderTexture;
                if (!target) throw new InvalidOperationException("No rendered thumbnail for " + materialId);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false)
                    { hideFlags = HideFlags.HideAndDontSave };
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                    pixels.Apply(false, false);
                }
                finally { RenderTexture.active = previous; }
                int visible = 0;
                foreach (Color32 pixel in pixels.GetPixels32()) if (pixel.a > 16) visible++;
                if (visible < 32) throw new InvalidOperationException("Empty model thumbnail: " + materialId);
                return pixels.EncodeToPNG();
            }
            finally
            {
                if (pixels) Object.DestroyImmediate(pixels);
                try
                {
                    if (originalScene.IsValid() && originalScene.isLoaded)
                        SceneManager.SetActiveScene(originalScene);
                }
                finally
                {
                    // The preview utility owns its render target and isolated scene.
                    // Its paired EndPreview above has already restored the lighting context.
                    preview.Cleanup();
                }
            }
        }
    }
}
#endif
