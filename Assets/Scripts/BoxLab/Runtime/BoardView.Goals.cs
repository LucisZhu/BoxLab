using UnityEngine;
using UnityEngine.Rendering;

namespace BoxLab
{
    public sealed partial class BoardView
    {
        private const string GoalShaderPath = "BoxLabArt/RadialGoal";
        private const string GoalMarkerName = "Radial gold goal";

        private void DrawGoalMarker(Transform parent, Color color)
        {
            Material material = GoalGradientMaterial(color);
            if (!material)
            {
                // Preserve a recognizable goal if the optional artwork folder is removed.
                Ring(parent, .35f, .061f, .039f, color);
                return;
            }
            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = GoalMarkerName;
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = new Vector3(0, .061f, 0);
            marker.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // The soft border extends past the crate's .64 footprint, but stays inside the tile.
            marker.transform.localScale = new Vector3(.84f, .84f, 1);
            var collider = marker.GetComponent<Collider>();
            if (collider) { collider.enabled = false; DisposeObject(collider); }
            var renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private Material GoalGradientMaterial(Color color)
        {
            string key = "radial-goal-" + ColorUtility.ToHtmlStringRGBA(color);
            Material material;
            if (materials.TryGetValue(key, out material) && material) return material;
            // A Resources reference keeps this shader available in the standalone build.
            var shader = Resources.Load<Shader>(GoalShaderPath);
            if (!shader || !shader.isSupported) return null;
            material = new Material(shader) { name = key, color = color, hideFlags = HideFlags.HideAndDontSave };
            material.renderQueue = (int)RenderQueue.Transparent;
            materials[key] = material;
            return material;
        }

        private Material GoalGhostMaterial(Renderer renderer, Material tint)
        {
            if (renderer.name != GoalMarkerName) return tint;
            // Keep the radial alpha mask when the placement preview turns green or red.
            return GoalGradientMaterial(tint.color) ?? tint;
        }

        /// <summary>Checks the actual rendered goal materials without changing gameplay state.</summary>
        internal bool ValidateGoalMarkerMaterials(out string detail)
        {
            var shader = Resources.Load<Shader>(GoalShaderPath);
            if (!shader || !shader.isSupported) { detail = "终点径向渐变shader缺失或不受支持。"; return false; }
            if (!generated) { detail = "当前没有可验证的棋盘。"; return false; }
            int count = 0;
            foreach (var renderer in generated.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name == "Solid gold goal inset") { detail = "终点仍残留旧的实心圆盘。"; return false; }
                if (renderer.name != GoalMarkerName) continue;
                var material = renderer.sharedMaterial;
                Vector3 scale = renderer.transform.localScale;
                if (!material || material.shader != shader || material.renderQueue != (int)RenderQueue.Transparent ||
                    material.color.a <= 0 || renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows ||
                    scale.x <= .64f || scale.x > .86f || Mathf.Abs(scale.x - scale.y) > .001f)
                { detail = "终点渐变材质或地面覆盖范围不正确。"; return false; }
                count++;
            }
            if (count == 0) { detail = "当前棋盘没有可验证的终点渐变面。"; return false; }
            detail = count + "个终点采用Resources径向渐变材质，外沿覆盖箱体边缘且不超出地块。";
            return true;
        }
    }
}
