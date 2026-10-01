using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    public sealed partial class BoardView
    {
        internal const int PortalMaxParticles = 72;
        private PortalGlintPool portalGlints;
        internal int PortalParticleCount => portalGlints ? portalGlints.LiveCount : 0;
        internal const int PortalParticleLimit = PortalMaxParticles;

        /// <summary>
        /// The unrotated doorway faces North (+Z). Its entrance arrow points into the doorway;
        /// its exit arrow points out. Rotating this single root keeps art and direction aligned.
        /// </summary>
        private void DrawPortalVisual(Transform parent, bool entrance, int rotation, bool withEffects = true)
        {
            var portal = new GameObject(entrance ? "Directional portal entrance" : "Directional portal exit").transform;
            portal.SetParent(parent, false);
            portal.localRotation = Quaternion.Euler(0, ((rotation % 4) + 4) % 4 * 90, 0);
            var doorway = new GameObject("Directional portal doorway").transform;
            doorway.SetParent(portal, false);
            if (!TryArt(doorway, "PortalFrame", Vector3.zero, null))
            {
                // Deleting the optional artwork still leaves an unmistakable upright doorway.
                for (int side = -1; side <= 1; side += 2)
                {
                    Cube(doorway, "Stone portal jamb", new Vector3(side * .37f, .455f, 0), new Vector3(.12f, .82f, .20f), ModuleStone);
                    Cube(doorway, "Stone portal foot", new Vector3(side * .37f, .085f, 0), new Vector3(.15f, .085f, .23f), ModuleCap);
                    Cube(doorway, "Stone arch shoulder", new Vector3(side * .295f, .88f, 0), new Vector3(.13f, .24f, .20f), ModuleStone,
                        Quaternion.Euler(0, 0, side * 32));
                }
                Cube(doorway, "Stone arch crown", new Vector3(0, .995f, 0), new Vector3(.56f, .11f, .215f), ModuleCap);
            }
            SetShadows(doorway, true, true);

            // The original stone arch itself masks the rectangular transparent field. Two
            // opposite faces keep it visible from both sides without changing the rule-facing side.
            Material field = PortalFieldMaterial(entrance);
            for (int side = 0; side < 2; side++)
            {
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                face.name = entrance ? "Violet portal field" : "Green portal field";
                face.transform.SetParent(doorway, false);
                face.transform.localPosition = new Vector3(0, .447f, side == 0 ? -.001f : .001f);
                face.transform.localRotation = Quaternion.Euler(0, side * 180, 0);
                face.transform.localScale = new Vector3(.65f, .79f, 1);
                var collider = face.GetComponent<Collider>();
                if (collider) { collider.enabled = false; DisposeObject(collider); }
                var renderer = face.GetComponent<Renderer>();
                renderer.sharedMaterial = field;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Color accent = entrance ? new Color(.76f, .58f, 1f) : new Color(.46f, .96f, .75f);
            // A narrow luminous lip on both sides remains readable when an east/west
            // doorway is nearly edge-on. It does not change the opening or collision rules.
            Material rim = PortalRimMaterial(entrance);
            for (int face = -1; face <= 1; face += 2)
            {
                for (int edge = -1; edge <= 1; edge += 2)
                    DrawPortalGlowStrip(doorway, new Vector3(edge * .316f, .423f, face * .119f), new Vector3(.029f, .69f, .025f), rim);
                DrawPortalGlowStrip(doorway, new Vector3(0, .084f, face * .119f), new Vector3(.64f, .027f, .025f), rim);
            }
            // North is the front, so entry moves south and an exiting box moves north.
            Arrow(portal, new Vector3(0, .052f, .30f), entrance ? Direction.South : Direction.North, accent, .36f);
            ApplyPortalDoorwayYaw(portal, rotation);
            if (Application.isPlaying && withEffects)
            {
                if (!portalGlints) portalGlints = GetComponent<PortalGlintPool>();
                if (!portalGlints)
                {
                    portalGlints = gameObject.AddComponent<PortalGlintPool>();
                    portalGlints.hideFlags = HideFlags.DontSave;
                }
                portalGlints.Register(this, doorway, entrance);
            }
        }

        /// <summary>
        /// May also be called after a ghost's outer transform rotates. Only the visual doorway
        /// changes: the semantic root and its direct-child ground arrow remain exact grid axes.
        /// </summary>
        internal static void ApplyPortalDoorwayYaw(Transform portal, int rotation)
        {
            if (!portal) return;
            Transform doorway = portal.Find("Directional portal doorway");
            if (!doorway) return;
            int direction = ((rotation % 4) + 4) % 4;
            float yaw = direction == 1 ? 10 : direction == 3 ? -10 : 0;
            doorway.localRotation = Quaternion.Euler(0, yaw, 0);
        }

        private Material PortalFieldMaterial(bool entrance)
        {
            string key = entrance ? "portal-field-entrance" : "portal-field-exit";
            if (materials.TryGetValue(key, out Material cached) && cached) return cached;
            Shader shader = surfaceMaterial ? surfaceMaterial.shader : Shader.Find("Standard");
            var material = new Material(shader) { name = key, hideFlags = HideFlags.HideAndDontSave };
            Color color = entrance ? new Color(.57f, .23f, .98f, .72f) : new Color(.13f, .82f, .50f, .72f);
            material.color = color;
            material.SetFloat("_Mode", 2);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetFloat("_Glossiness", .12f);
            material.SetFloat("_Metallic", 0);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 1.15f);
            // The existing BoardView lifecycle releases every cached material on destruction.
            materials[key] = material;
            return material;
        }

        private Material PortalRimMaterial(bool entrance)
        {
            string key = entrance ? "portal-rim-entrance" : "portal-rim-exit";
            if (materials.TryGetValue(key, out Material cached) && cached) return cached;
            Shader shader = surfaceMaterial ? surfaceMaterial.shader : Shader.Find("Standard");
            var material = new Material(shader) { name = key, hideFlags = HideFlags.HideAndDontSave };
            Color color = entrance ? new Color(.77f, .40f, 1f) : new Color(.29f, 1f, .65f);
            material.color = color * .65f;
            material.SetFloat("_Glossiness", .08f); material.SetFloat("_Metallic", 0);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.05f);
            materials[key] = material; return material;
        }
        private void DrawPortalGlowStrip(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube); strip.name = "Portal luminous inner edge";
            strip.transform.SetParent(parent, false); strip.transform.localPosition = position; strip.transform.localScale = size;
            var collider = strip.GetComponent<Collider>();
            if (collider) { collider.enabled = false; DisposeObject(collider); }
            var renderer = strip.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
    }

    /// <summary>One bounded emitter per board, independent of portal rules and player input.</summary>
    [DisallowMultipleComponent]
    internal sealed class PortalGlintPool : MonoBehaviour
    {
        private struct Doorway
        {
            public Transform root;
            public bool entrance;
        }

        private readonly List<Doorway> doorways = new List<Doorway>();
        private readonly System.Random random = new System.Random(7419);
        private BoardView board;
        private ParticleSystem particles;
        private Material material;
        private Texture2D texture;
        private int cursor;
        private float budget;
        private bool running;
        internal int LiveCount => particles ? particles.particleCount : 0;

        internal void Register(BoardView owner, Transform doorway, bool entrance)
        {
            board = owner;
            doorways.Add(new Doorway { root = doorway, entrance = entrance });
            budget = Mathf.Max(budget, 1);
        }

        private void Update()
        {
            bool removed = false;
            for (int i = doorways.Count - 1; i >= 0; i--)
                if (!doorways[i].root || !doorways[i].root.gameObject.activeInHierarchy)
                { doorways.RemoveAt(i); removed = true; }
            // Clearing a map, deleting a portal or rebuilding its orientation must not leave
            // old world-space particles suspended over the new layout.
            if (removed) Clear();
            if (!Application.isPlaying || !BoardAtmosphere.EffectsEnabled || !board
                || !board.isActiveAndEnabled || doorways.Count == 0)
            { Clear(); return; }
            if (!EnsureEmitter()) return;
            if (!running) { particles.Play(false); running = true; }
            budget = Mathf.Min(6, budget + Mathf.Min(Time.unscaledDeltaTime, .1f) * Mathf.Min(48, doorways.Count * 12));
            int count = Mathf.FloorToInt(budget);
            for (int i = 0; i < count && particles.particleCount < BoardView.PortalMaxParticles; i++)
            {
                if (cursor >= doorways.Count) cursor = 0;
                Emit(doorways[cursor++]);
            }
            budget -= count;
        }

        private bool EnsureEmitter()
        {
            if (particles) return true;
            // This Resources shader already ships for the ice mist, so player shader stripping
            // cannot remove it. The built-in fallback is only for deleting the optional art pack.
            Shader shader = Resources.Load<Shader>("BoxLabArt/SoftMist");
            if (!shader || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            if (!shader || !shader.isSupported) return false;
            const int side = 16;
            texture = new Texture2D(side, side, TextureFormat.RGBA32, false)
            { name = "Portal soft glint", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[side * side];
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    float dx = Mathf.Abs((x + .5f) / side * 2 - 1), dy = Mathf.Abs((y + .5f) / side * 2 - 1);
                    float alpha = Mathf.Clamp01((1 - dx - dy) * 2.4f);
                    pixels[y * side + x] = new Color(1, 1, 1, alpha);
                }
            texture.SetPixels(pixels); texture.Apply(false, true);
            material = new Material(shader)
            { name = "Portal colored glints", hideFlags = HideFlags.HideAndDontSave, mainTexture = texture, color = Color.white };
            var root = new GameObject("BoxLab portal glints (runtime only)") { hideFlags = HideFlags.DontSave };
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            particles = root.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false; particles.randomSeed = 7419;
            var main = particles.main;
            main.playOnAwake = false; main.loop = true; main.duration = 10;
            main.maxParticles = BoardView.PortalMaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true; main.gravityModifier = 0; main.startSpeed = 0;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var colors = particles.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1f, .14f), new GradientAlphaKey(.90f, .70f), new GradientAlphaKey(0, 1) });
            colors.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .12f;
            return true;
        }

        private void Emit(Doorway doorway)
        {
            if (!doorway.root) return;
            float side = random.Next(2) == 0 ? -1 : 1, face = random.Next(2) == 0 ? -1 : 1;
            // The previous emitters sat inside the stone jambs. Move them outside both faces,
            // so glints remain visible without changing the accepted +/-10 degree visual yaw.
            float x = random.Next(3) == 0 ? Between(-.19f, .19f) : side * Between(.24f, .30f);
            Vector3 local = new Vector3(x, Between(.16f, .78f), face * Between(.135f, .19f));
            var scale = doorway.root.lossyScale;
            float unit = Mathf.Max(.01f, (Mathf.Abs(scale.x) + Mathf.Abs(scale.z)) * .5f);
            var particle = new ParticleSystem.EmitParams
            {
                position = doorway.root.TransformPoint(local),
                velocity = doorway.root.TransformDirection(new Vector3(Between(-.02f, .02f), Between(.09f, .17f), face * .025f)) * unit,
                startLifetime = Between(.8f, 1.25f), startSize = Between(.11f, .18f) * unit,
                startColor = doorway.entrance ? new Color(.87f, .65f, 1, .96f) : new Color(.47f, 1, .76f, .96f),
                rotation = Between(-20, 20), applyShapeToPosition = false
            };
            particles.Emit(particle, 1);
        }

        private float Between(float minimum, float maximum)
        { return minimum + (maximum - minimum) * (float)random.NextDouble(); }

        private void Clear()
        {
            if (particles && (running || particles.particleCount > 0))
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            running = false; budget = 0; cursor = 0;
        }

        private void OnDisable() { Clear(); }
        private void OnDestroy()
        {
            Clear();
            if (particles) Destroy(particles.gameObject);
            if (material) Destroy(material);
            if (texture) Destroy(texture);
            doorways.Clear();
        }
    }
}
