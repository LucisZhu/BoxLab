using UnityEngine;

namespace BoxLab
{
    public sealed partial class BoardView
    {
        private const string PermissionShaderPath = "BoxLabArt/PermissionPortrait";
        /// <summary>A passage shows only the objects it accepts, using the actual model portraits.</summary>
        private void DrawPassagePermissions(Transform parent, ActorMask allowed)
        {
            bool player = (allowed & ActorMask.Player) != 0;
            bool box = (allowed & ActorMask.Box) != 0;
            var symbols = new GameObject(player && box ? "人物与箱子通行标记" : player ? "人物通行标记" : box ? "箱子通行标记" : "无通行对象标记").transform;
            symbols.SetParent(parent, false);
            bool both = player && box;
            if (player) DrawPermissionIcon(symbols, true, both ? -.165f : 0, both ? 1 : 1.7f);
            if (box) DrawPermissionIcon(symbols, false, both ? .165f : 0, both ? 1 : 1.7f);
        }

        private void DrawPermissionIcon(Transform parent, bool player, float x, float scale)
        {
            var icon = new GameObject(player ? "人物图标" : "箱子图标").transform;
            icon.SetParent(parent, false);
            icon.localPosition = new Vector3(x, 0, 0);
            icon.localScale = new Vector3(scale, 1, scale);
            Cube(icon, player ? "浅色人物底牌" : "浅色箱子底牌", new Vector3(0, .068f, 0), new Vector3(.315f, .008f, .335f), new Color(.85f, .83f, .73f));
            if (TryPermissionPortrait(icon, player ? "player" : "box", new Vector3(0, .075f, 0), new Vector2(.31f, .31f))) return;

            // Keep the selected object's silhouette if the optional art folder is removed.
            Color chalk = new Color(.94f, .93f, .81f);
            Color timber = new Color(.91f, .68f, .38f);
            if (player)
            {
                Sphere(icon, "Head", new Vector3(0, .083f, .12f), new Vector3(.086f, .012f, .086f), chalk);
                PermissionStroke(icon, "Body", new Vector2(0, .057f), new Vector2(0, -.048f), chalk, .049f);
                PermissionStroke(icon, "Arm left", new Vector2(0, .036f), new Vector2(-.086f, -.032f), chalk, .034f);
                PermissionStroke(icon, "Arm right", new Vector2(0, .036f), new Vector2(.086f, -.032f), chalk, .034f);
                PermissionStroke(icon, "Leg left", new Vector2(0, -.048f), new Vector2(-.060f, -.140f), chalk, .038f);
                PermissionStroke(icon, "Leg right", new Vector2(0, -.048f), new Vector2(.060f, -.140f), chalk, .038f);
            }
            else
            {
                Cube(icon, "Wooden crate face", new Vector3(0, .076f, 0), new Vector3(.216f, .009f, .216f), timber * .58f);
                SquareFrame(icon, "Wood crate rim", .222f, .083f, .026f, timber);
                PermissionStroke(icon, "Crate diagonal brace", new Vector2(-.082f, -.082f), new Vector2(.082f, .082f), timber, .027f);
            }
        }

        private bool TryPermissionPortrait(Transform parent, string resource, Vector3 position, Vector2 size)
        {
            string key = "permission-portrait-" + resource;
            Material material;
            if (!materials.TryGetValue(key, out material) || !material)
            {
                var texture = Resources.Load<Texture2D>("BoxLabArt/Thumbnails/" + resource);
                if (!texture) return false;
                Shader shader = Resources.Load<Shader>(PermissionShaderPath);
                if (!shader || !shader.isSupported) return false;
                material = new Material(shader) { name = key, hideFlags = HideFlags.HideAndDontSave, mainTexture = texture };
                material.color = Color.white;
                material.SetFloat("_Brightness", 1.5f); material.SetFloat("_Cutoff", .025f);
                material.SetInt("_ZWrite", 1); material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                // Both generated portraits have sufficient transparent margins. Cropping these
                // margins enlarges the figures by 10% without enlarging or distorting the plaque.
                material.mainTextureScale = Vector2.one / 1.1f;
                material.mainTextureOffset = (Vector2.one - material.mainTextureScale) * .5f;
                materials[key] = material;
            }
            var portrait = GameObject.CreatePrimitive(PrimitiveType.Quad);
            portrait.name = resource == "player" ? "人物模型缩略图" : "箱子模型缩略图";
            portrait.transform.SetParent(parent, false); portrait.transform.localPosition = position;
            portrait.transform.localRotation = Quaternion.Euler(90, 0, 0);
            portrait.transform.localScale = new Vector3(size.x, size.y, 1);
            var collider = portrait.GetComponent<Collider>();
            if (collider) { collider.enabled = false; DisposeObject(collider); }
            var renderer = portrait.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return true;
        }

        private Material PermissionGhostMaterial(Renderer renderer, Material tint, bool valid)
        {
            if (renderer.name != "人物模型缩略图" && renderer.name != "箱子模型缩略图") return tint;
            Texture texture = renderer.sharedMaterial ? renderer.sharedMaterial.mainTexture : null;
            if (!texture) return tint;
            string key = "ghost-permission-" + texture.GetInstanceID() + "-" + valid;
            Material material;
            if (materials.TryGetValue(key, out material) && material) return material;
            Shader shader = Resources.Load<Shader>(PermissionShaderPath);
            if (!shader || !shader.isSupported) return tint;
            material = new Material(shader) { name = key, mainTexture = texture, color = tint.color, hideFlags = HideFlags.HideAndDontSave };
            material.mainTextureScale = renderer.sharedMaterial.mainTextureScale;
            material.mainTextureOffset = renderer.sharedMaterial.mainTextureOffset;
            material.SetFloat("_Brightness", 1.5f); material.SetFloat("_Cutoff", .025f);
            material.SetInt("_ZWrite", 0); material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            materials[key] = material; return material;
        }

        /// <summary>Small runtime check for the real passage fixture; no GPU rendering or state mutation.</summary>
        internal bool ValidatePermissionPortraitMaterials(out string detail)
        {
            var shader = Resources.Load<Shader>(PermissionShaderPath);
            if (!shader || !shader.isSupported) { detail = "通道专用透明贴图shader缺失或不受支持。"; return false; }
            if (!generated) { detail = "当前没有可验证的棋盘。"; return false; }
            int count = 0, passageCount = 0;
            foreach (var sign in generated.GetComponentsInChildren<Transform>())
            {
                bool both = sign.name == "人物与箱子通行标记";
                bool wantsPlayer = both || sign.name == "人物通行标记";
                bool wantsBox = both || sign.name == "箱子通行标记";
                if (!wantsPlayer && !wantsBox) continue;
                passageCount++;
                int players = 0, boxes = 0;
                foreach (var renderer in sign.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.name == "人物模型缩略图") players++;
                    else if (renderer.name == "箱子模型缩略图") boxes++;
                    else continue;
                    var material = renderer.sharedMaterial;
                    var texture = material ? material.mainTexture as Texture2D : null;
                    if (!material || material.shader != shader || !texture || texture.width < 128 || texture.height < 128 ||
                        !material.HasProperty("_Brightness") || material.GetFloat("_Brightness") < 1 || material.GetFloat("_Cutoff") <= 0)
                    { detail = renderer.name + "未使用有效的专用透明材质/模型图片。"; return false; }
                    count++;
                }
                if (players != (wantsPlayer ? 1 : 0) || boxes != (wantsBox ? 1 : 0))
                { detail = sign.name + "的模型图标与允许通行对象不一致。"; return false; }
            }
            if (passageCount == 0) { detail = "当前棋盘没有可验证的通道模型图片。"; return false; }
            detail = passageCount + "个通道仅显示允许对象，共" + count + "张模型图，均使用随Resources入包的无光照透明shader和有效PNG。";
            return true;
        }

        private void PermissionStroke(Transform parent, string name, Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 middle = (a + b) * .5f, delta = b - a;
            Cube(parent, name, new Vector3(middle.x, .083f, middle.y), new Vector3(width, .013f, delta.magnitude), color,
                Quaternion.Euler(0, Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 0));
        }
    }
}
