using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoxLab
{
    /// <summary>Runtime-only decoration. Reads board snapshots; never changes rules or board data.</summary>
    [DisallowMultipleComponent]
    public sealed class BoardAtmosphere : MonoBehaviour
    {
        public static bool EffectsEnabled = true;
        public static bool VerificationPassed { get; private set; }

        const int MistLimit = 128, SparkLimit = 160;
        readonly List<int> ice = new List<int>(), goals = new List<int>();
        readonly List<int> nextIce = new List<int>(), nextGoals = new List<int>();
        readonly HashSet<int> filled = new HashSet<int>(), nextFilled = new HashSet<int>();
        readonly System.Random random = new System.Random(0xB04);
        BoardView board;
        LevelData level;
        ParticleSystem mist, sparks;
        Texture2D mistTexture, sparkTexture;
        Material mistMaterial, sparkMaterial;
        int width, height, lastMoves, lastPushes, iceCursor, goalCursor;
        float mistBudget, sparkBudget;
        bool playing, unavailable, verificationMode, ownsMistTexture;

        void Awake()
        { verificationMode = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-boxlabWorkshopVerify") >= 0; }

        /// <summary>Call after rendering a snapshot or completing its actor animation.</summary>
        public static void Sync(BoardView view, LevelData map, RuleBookData book, BoardState state)
        {
            // No component, object, texture or material is created in edit mode / preview rendering.
            if (!Application.isPlaying || !view) return;
            var effect = view.GetComponent<BoardAtmosphere>();
            if (map == null || map.cells == null || book == null || state == null)
            { if (effect) effect.Forget(); return; }
            if (!effect && !EffectsEnabled) return;
            if (!effect)
            {
                effect = view.gameObject.AddComponent<BoardAtmosphere>();
                effect.hideFlags = HideFlags.DontSave;
            }
            effect.ReadSnapshot(view, map, book, state);
        }

        void ReadSnapshot(BoardView view, LevelData map, RuleBookData book, BoardState state)
        {
            board = view;
            nextIce.Clear(); nextGoals.Clear(); nextFilled.Clear();
            for (int cell = 0; cell < map.cells.Count; cell++)
            {
                if (map.cells[cell] == null) continue;
                string id = state.tiles != null && cell < state.tiles.Length ? state.tiles[cell] : map.cells[cell].ruleId;
                var rule = book.Find(id);
                if (rule == null) continue;
                if (rule.visual == TerrainVisual.Ice) nextIce.Add(cell);
                if (rule.visual == TerrainVisual.Goal) nextGoals.Add(cell);
            }
            if (state.boxes != null)
                foreach (int cell in state.boxes)
                    if (nextGoals.Contains(cell)) nextFilled.Add(cell);

            bool reset = !ReferenceEquals(level, map) || width != map.width || height != map.height ||
                state.moves < lastMoves || state.pushes < lastPushes || !Same(ice, nextIce) || !Same(goals, nextGoals);
            if (reset)
            {
                ClearParticles(); iceCursor = goalCursor = 0;
                mistBudget = sparkBudget = 1;
            }
            level = map; width = map.width; height = map.height;
            ice.Clear(); ice.AddRange(nextIce); goals.Clear(); goals.AddRange(nextGoals);
            if (!EffectsEnabled) ClearParticles();
            else if ((ice.Count > 0 || goals.Count > 0) && EnsureSystems())
            {
                BeginParticles();
                // Loading, changing maps, undo and restart establish a baseline without celebrations.
                if (!reset)
                {
                    int burstBudget = 36;
                    foreach (int cell in nextFilled)
                    {
                        if (filled.Contains(cell)) continue;
                        int count = Mathf.Min(9, burstBudget);
                        for (int i = 0; i < count; i++) EmitSpark(cell, true, true);
                        burstBudget -= count;
                        if (burstBudget <= 0) break;
                    }
                }
            }
            filled.Clear(); filled.UnionWith(nextFilled);
            lastMoves = state.moves; lastPushes = state.pushes;
        }

        void Update()
        {
            if (!CanRunEffects()) return;
            if (ice.Count == 0 && goals.Count == 0) return;
            if (!EnsureSystems()) return;
            BeginParticles();
            // A single pair of emitters serves every tile. Large maps are visited round-robin;
            // both the per-frame work and live particle totals remain bounded.
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            mistBudget = Mathf.Min(8, mistBudget + dt * Mathf.Min(ice.Count * 2.8f, 30));
            sparkBudget = Mathf.Min(8, sparkBudget + dt * Mathf.Min(goals.Count * 2f, 24));
            int mistCount = ice.Count == 0 ? 0 : Mathf.FloorToInt(mistBudget);
            int sparkCount = goals.Count == 0 ? 0 : Mathf.FloorToInt(sparkBudget);
            for (int i = 0; i < mistCount; i++)
            {
                if (iceCursor >= ice.Count) iceCursor = 0;
                EmitMist(ice[iceCursor++]);
            }
            for (int i = 0; i < sparkCount; i++)
            {
                if (goalCursor >= goals.Count) goalCursor = 0;
                int cell = goals[goalCursor++]; EmitSpark(cell, filled.Contains(cell), false);
            }
            mistBudget -= mistCount; sparkBudget -= sparkCount;
            VerifyWhenVisible();
        }

        bool CanRunEffects()
        {
            bool allowed = Application.isPlaying && EffectsEnabled && board && board.isActiveAndEnabled && level != null;
            if (!allowed && playing) ClearParticles();
            return allowed;
        }

        void VerifyWhenVisible()
        {
            if (!verificationMode || VerificationPassed || !mist || !sparks || mist.particleCount == 0 || sparks.particleCount == 0) return;
            int mistCount = mist.particleCount, goldCount = sparks.particleCount;
            bool previous = EffectsEnabled;
            try
            {
                EffectsEnabled = false;
                bool allowed = CanRunEffects(); // Exercise the same switch/clear path as normal Update.
                if (allowed || mist.particleCount != 0 || sparks.particleCount != 0)
                { Debug.LogError("BOXLAB_ATMOSPHERE_FAILURE: disabled effects retained live particles."); return; }
                VerificationPassed = true;
                Debug.Log("BOXLAB_ATMOSPHERE_SUCCESS: live ice mist=" + mistCount + ", goal sparks=" + goldCount + "; switch OFF cleared both to zero; previous setting restored.");
            }
            finally { EffectsEnabled = previous; mistBudget = sparkBudget = 1; }
        }

        bool EnsureSystems()
        {
            if (mist && sparks) return true;
            if (unavailable) return false;
            Shader shader = Shader.Find("Sprites/Default");
            if (!shader || !shader.isSupported) shader = Shader.Find("Particles/Standard Unlit");
            if (!shader || !shader.isSupported) { unavailable = true; return false; }
            mistTexture = Resources.Load<Texture2D>("BoxLabArt/Particles/smoke_01");
            ownsMistTexture = !mistTexture;
            if (ownsMistTexture) mistTexture = MakeTexture(false);
            sparkTexture = MakeTexture(true);
            Shader mistShader = Resources.Load<Shader>("BoxLabArt/SoftMist");
            if (!mistShader || !mistShader.isSupported) mistShader = shader;
            mistMaterial = MakeMaterial(mistShader, mistTexture, "BoxLab cold ice mist");
            sparkMaterial = MakeMaterial(shader, sparkTexture, "BoxLab procedural goal sparks");
            mist = MakeSystem("BoxLab ice mist (runtime only)", mistMaterial, MistLimit, true);
            sparks = MakeSystem("BoxLab goal sparks (runtime only)", sparkMaterial, SparkLimit, false);
            return true;
        }

        ParticleSystem MakeSystem(string label, Material material, int limit, bool fog)
        {
            var root = new GameObject(label) { hideFlags = HideFlags.DontSave };
            root.layer = gameObject.layer; root.transform.SetParent(transform, false);
            var system = root.AddComponent<ParticleSystem>();
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false; system.randomSeed = fog ? 4301u : 4302u;
            var main = system.main;
            main.loop = true; main.playOnAwake = false; main.duration = 10;
            main.maxParticles = limit; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape; main.useUnscaledTime = true;
            main.gravityModifier = 0; main.startSpeed = 0; main.startLifetime = 1;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = false;
            var color = system.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, fog ? .22f : .12f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0, 1) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, fog ? .65f : .75f), new Keyframe(.35f, 1), new Keyframe(1, fog ? 1.35f : .3f)));
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = fog ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .12f;
            return system;
        }

        void EmitMist(int cell)
        {
            if (!mist || mist.particleCount >= MistLimit) return;
            float angle = Between(0, Mathf.PI * 2), radius = Between(.18f, .30f);
            var particle = new ParticleSystem.EmitParams
            {
                position = Position(cell, new Vector3(Mathf.Cos(angle) * radius, Between(.115f, .16f), Mathf.Sin(angle) * radius)),
                velocity = board.transform.TransformDirection(new Vector3(Between(-.06f, .06f), .012f, Between(-.06f, .06f))),
                startLifetime = Between(2.2f, 3f), startSize = Between(.58f, .76f) * UnitScale(),
                startColor = new Color(.82f, .95f, 1, Between(.48f, .64f)), rotation = Between(0, 360), applyShapeToPosition = false
            };
            mist.Emit(particle, 1);
        }

        void EmitSpark(int cell, bool occupied, bool burst)
        {
            if (!sparks || sparks.particleCount >= SparkLimit) return;
            float angle = Between(0, Mathf.PI * 2), radius = occupied ? Between(.40f, .46f) : Between(.28f, .34f);
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var particle = new ParticleSystem.EmitParams
            {
                position = Position(cell, radial * radius + Vector3.up * .10f),
                velocity = board.transform.TransformDirection(radial * (burst ? .065f : .012f) + Vector3.up * (burst ? Between(.35f, .6f) : Between(.12f, .18f))),
                startLifetime = burst ? Between(.55f, .9f) : Between(1.05f, 1.6f),
                startSize = (burst ? Between(.055f, .085f) : Between(.05f, .075f)) * UnitScale(),
                startColor = burst ? new Color(1, .82f, .30f, .92f) : new Color(1, .77f, .20f, .72f),
                rotation = Between(-18, 18), applyShapeToPosition = false
            };
            sparks.Emit(particle, 1);
        }

        Vector3 Position(int cell, Vector3 offset) { return board.GetCellWorld(cell) + board.transform.TransformVector(offset); }
        float UnitScale() { var scale = board.transform.lossyScale; return Mathf.Max(.01f, (Mathf.Abs(scale.x) + Mathf.Abs(scale.z)) * .5f); }
        float Between(float min, float max) { return min + (max - min) * (float)random.NextDouble(); }
        static bool Same(List<int> a, List<int> b)
        { if (a.Count != b.Count) return false; for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false; return true; }

        static Texture2D MakeTexture(bool diamond)
        {
            const int side = 32;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false)
            { name = diamond ? "BoxLab procedural diamond" : "BoxLab procedural radial mist", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[side * side];
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    float dx = (x + .5f) / side * 2 - 1, dy = (y + .5f) / side * 2 - 1;
                    float distance = diamond ? Mathf.Abs(dx) + Mathf.Abs(dy) : Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = diamond ? Mathf.Clamp01((1 - distance) * 4) : Mathf.Pow(Mathf.Clamp01(1 - distance), 1.7f);
                    pixels[y * side + x] = new Color(1, 1, 1, alpha);
                }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }

        static Material MakeMaterial(Shader shader, Texture2D texture, string label)
        {
            var material = new Material(shader) { name = label, hideFlags = HideFlags.HideAndDontSave, mainTexture = texture, color = Color.white };
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 2);
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        void BeginParticles()
        {
            if (playing) return;
            if (mist) mist.Play(false); if (sparks) sparks.Play(false); playing = true;
        }
        void ClearParticles()
        {
            if (mist) mist.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (sparks) sparks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            playing = false;
        }
        void Forget()
        { ClearParticles(); level = null; ice.Clear(); goals.Clear(); filled.Clear(); }
        void OnDisable() { ClearParticles(); }
        void OnDestroy()
        {
            if (mist) Dispose(mist.gameObject); if (sparks) Dispose(sparks.gameObject);
            Dispose(mistMaterial); Dispose(sparkMaterial);
            if (ownsMistTexture) Dispose(mistTexture);
            Dispose(sparkTexture);
        }
        static void Dispose(Object item)
        { if (!item) return; if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }
}
