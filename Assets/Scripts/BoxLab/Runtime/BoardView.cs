using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Presentation only: rendering never writes level data or determines game rules.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed partial class BoardView : MonoBehaviour
    {
        public BoxProject project;
        public int levelIndex;
        public Camera boardCamera;
        public Material surfaceMaterial;
        [Range(45, 85)] public float cameraElevation = 58;
        [Range(0.03f, 0.3f)] public float animationSeconds = 0.10f;

        private Transform generated;
        private Transform[] cellRoots;
        private string[] cellKeys;
        private readonly Dictionary<int, Transform> actorRoots = new Dictionary<int, Transform>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<Transform> edgeRoots = new List<Transform>();
        private readonly List<string> edgeKeys = new List<string>();
        private LevelData currentLevel;
        private RuleBookData currentBook;
        private BoardState currentState;
        private Font labelFont;
        private bool refreshing;
        private int geometryWidth, geometryHeight;
        private readonly Dictionary<string, GameObject> optionalArt = new Dictionary<string, GameObject>();
        private Camera backdropCamera;
        private Transform editGhost;
        private string editGhostShape;
        private Renderer[] editGhostRenderers;
        private Material validGhostMaterial, invalidGhostMaterial;
        private bool lastGhostValidity;
        private readonly Dictionary<int, Transform> buttonHeads = new Dictionary<int, Transform>();
        private readonly List<ButtonTravel> buttonTravels = new List<ButtonTravel>();
        private readonly Dictionary<string, Mesh> mechanismMeshes = new Dictionary<string, Mesh>();
        private const float ButtonRaisedY = .19f, ButtonPressedY = .012f, ButtonTravelSeconds = .18f;
        private sealed class ButtonTravel { public Transform head; public Vector3 from, to; }
        // Functional modules share the stone/iron family of the optional dungeon kit.
        // SignalColor remains separate: linked switches, lamps and editor lines keep their identity.
        private static readonly Color ModuleStone = new Color(.57f, .60f, .59f);
        private static readonly Color ModuleCap = new Color(.74f, .76f, .72f);
        private static readonly Color ModuleIron = new Color(.25f, .28f, .29f);
        private static readonly Color ModuleMetalFace = new Color(.38f, .42f, .42f);
        private static readonly Color ModuleBrass = new Color(.70f, .65f, .48f);
        private static readonly Color ModuleChalk = new Color(.88f, .88f, .80f);

        /// <summary>Builds the same artwork used on the board, for isolated editor thumbnails.</summary>
        public GameObject CreatePalettePreview(string materialId, Transform parent)
        {
            var root = new GameObject("Palette " + materialId);
            root.transform.SetParent(parent, false);
            if (materialId == "$player" || materialId == "$box") DrawActorVisual(root.transform, materialId == "$player");
            else if (materialId == "$wall") DrawWall(root.transform);
            else
            {
                var book = PlayerRules.CreateBook();
                var rule = book.Find(materialId);
                if (rule != null && rule.boundary) DrawGateBody(root.transform, false);
                else
                {
                    var level = LevelData.Create(2, 2);
                    level.cells[0].ruleId = materialId;
                    level.cells[0].signalStyle = 0;
                    if (materialId == PlayerRules.PortalEntrance || materialId == PlayerRules.PortalExit) level.cells[0].rotation = 2;
                    DrawCell(root.transform, rule, rule == null ? "Plain" : rule.visual.ToString(), false, 0, level, level.cells[0], Direction.North);
                }
            }
            return root;
        }

        public void ShowEditGhost(string materialId, int cell, Direction side, bool edge, int rotation, bool valid)
        {
            if (currentLevel == null || !currentLevel.Contains(cell) || string.IsNullOrEmpty(materialId)) { HideEditGhost(); return; }
            if (!editGhost || editGhostShape != materialId)
            {
                if (editGhost) DisposeObject(editGhost.gameObject);
                var root = new GameObject("BoxLab Placement Ghost (preview only)");
                root.hideFlags = HideFlags.DontSave;
                editGhost = root.transform;
                editGhost.SetParent(transform, false);
                editGhostShape = materialId;
                DrawGhostShape(editGhost, materialId);
                editGhostRenderers = editGhost.GetComponentsInChildren<Renderer>();
                lastGhostValidity = !valid; // Apply a material to a newly built shape even if state is unchanged.
            }
            editGhost.gameObject.SetActive(true);
            Vector3 location = CellPosition(currentLevel, cell) + Vector3.up * .055f;
            if (edge) location += DirectionVector(side) * .5f;
            editGhost.localPosition = location;
            editGhost.localScale = Vector3.one * 1.045f;
            editGhost.localRotation = edge
                ? Quaternion.Euler(0, side == Direction.North || side == Direction.South ? 0 : 90, 0)
                : Quaternion.Euler(0, ((rotation % 4) + 4) % 4 * 90, 0);
            if (materialId == PlayerRules.PortalEntrance || materialId == PlayerRules.PortalExit)
            {
                Transform portal = editGhost.Find(materialId == PlayerRules.PortalEntrance ? "Directional portal entrance" : "Directional portal exit");
                if (portal) ApplyPortalDoorwayYaw(portal, rotation);
            }
            if (lastGhostValidity != valid)
            {
                Material tint = GhostMaterial(valid);
                foreach (var renderer in editGhostRenderers)
                {
                    Material rendererTint = GoalGhostMaterial(renderer, PermissionGhostMaterial(renderer, tint, valid));
                    var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (int i = 0; i < slots.Length; i++) slots[i] = rendererTint;
                    renderer.sharedMaterials = slots;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                lastGhostValidity = valid;
            }
        }

        public void HideEditGhost()
        { if (editGhost) editGhost.gameObject.SetActive(false); }

        private Material GhostMaterial(bool valid)
        {
            Material cached = valid ? validGhostMaterial : invalidGhostMaterial;
            if (cached) return cached;
            Shader shader = surfaceMaterial ? surfaceMaterial.shader : Shader.Find("Standard");
            cached = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Color color = valid ? new Color(.10f, .95f, .48f, .47f) : new Color(1f, .13f, .20f, .48f);
            cached.color = color;
            cached.SetFloat("_Mode", 2); // Standard Fade: translucent without changing any source material.
            cached.SetOverrideTag("RenderType", "Transparent");
            cached.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cached.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cached.SetInt("_ZWrite", 0);
            cached.DisableKeyword("_ALPHATEST_ON");
            cached.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            cached.EnableKeyword("_ALPHABLEND_ON");
            cached.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            cached.EnableKeyword("_EMISSION");
            cached.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * .35f);
            if (valid) validGhostMaterial = cached; else invalidGhostMaterial = cached;
            return cached;
        }

        private void DrawGhostShape(Transform root, string materialId)
        {
            Color tint = Color.white;
            if (materialId == "$box")
            {
                if (!TryArt(root, "Crate", Vector3.zero, null))
                {
                    Cube(root, "Cargo", new Vector3(0, .32f, 0), new Vector3(.64f, .58f, .64f), tint);
                    Cube(root, "Band X", new Vector3(0, .625f, 0), new Vector3(.67f, .03f, .10f), tint);
                    Cube(root, "Band Z", new Vector3(0, .625f, 0), new Vector3(.10f, .03f, .67f), tint);
                }
                return;
            }
            if (materialId == "$player")
            {
                if (!TryArt(root, "Pusher", Vector3.zero, null))
                {
                    Cylinder(root, "Player base", new Vector3(0, .22f, 0), new Vector3(.53f, .18f, .53f), tint);
                    Sphere(root, "Player top", new Vector3(0, .43f, 0), new Vector3(.40f, .40f, .40f), tint);
                }
                return;
            }
            if (materialId == "$wall")
            {
                DrawWall(root);
                return;
            }
            var rule = currentBook == null ? null : currentBook.Find(materialId);
            string visual = rule == null ? "Plain" : rule.visual.ToString();
            if (visual == "Gate" || (rule != null && rule.boundary))
            {
                DrawGateBody(root, false);
                return;
            }
            if (visual == "Fragile") { DrawFragileStone(root); return; }
            if (!TryArt(root, visual == "Plain" ? "Floor" : "SurfaceFloor", Vector3.zero, null)) Cube(root, "Floor", new Vector3(0, -.025f, 0), new Vector3(.86f, .12f, .86f), tint);
            switch (visual)
            {
                case "Turn": Arrow(root, Vector3.zero, Direction.North, tint, 1); break;
                case "Goal": DrawGoalMarker(root, tint); break;
                case "Ice": DrawSnowflake(root, .061f, tint); break;
                case "Plate": DrawButton(root, false, false, 0, false); break;
                case "RotationPlate": DrawButton(root, true, false, 0, false); break;
                case "Selective": DrawPassagePermissions(root, rule == null ? ActorMask.Player : rule.allowedActors); break;
                case "PortalEntrance": case "PortalExit": DrawPortalVisual(root, visual == "PortalEntrance", 0, false); break;
            }
        }

        public static Color SignalColor(int style)
        {
            switch (Mathf.Max(0, style) % 5)
            {
                case 0: return new Color(.22f, .75f, .90f);
                case 1: return new Color(.98f, .66f, .20f);
                case 2: return new Color(.80f, .43f, .84f);
                case 3: return new Color(.32f, .79f, .52f);
                default: return new Color(.96f, .43f, .38f);
            }
        }

        public static string SignalGlyph(int style)
        { return new[] { "●", "◆", "+" }[(Mathf.Max(0, style) / 5) % 3]; }

        public Vector3 GetCellWorld(int cell)
        { return currentLevel == null || !currentLevel.Contains(cell) ? transform.position : transform.TransformPoint(CellPosition(currentLevel, cell)); }

        public void SetViewport(Rect normalizedRect)
        {
            EnsureCamera();
            if (!boardCamera) return;
            boardCamera.rect = normalizedRect;
            boardCamera.ResetAspect();
            FitCamera();
        }

        /// <summary>screenPos uses the same bottom-left pixel origin as Input.mousePosition.</summary>
        public bool TryGetGridPoint(Vector2 screenPos, out Vector2 gridPosition)
        {
            gridPosition = Vector2.zero;
            if (!boardCamera || currentLevel == null || !boardCamera.pixelRect.Contains(screenPos)) return false;
            Ray ray = boardCamera.ScreenPointToRay(screenPos);
            float distance;
            if (!new Plane(transform.up, transform.position).Raycast(ray, out distance)) return false;
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
            gridPosition = new Vector2(local.x, local.z);
            return local.x >= -.5f && local.z >= -.5f && local.x <= currentLevel.width - .5f && local.z <= currentLevel.height - .5f;
        }

        public bool PickGround(Vector2 screenPos, out int cell, out Direction side, out bool onEdge)
        {
            cell = -1; side = Direction.North; onEdge = false;
            Vector2 point;
            if (!TryGetGridPoint(screenPos, out point)) return false;
            int x = Mathf.Clamp(Mathf.FloorToInt(point.x + .5f), 0, currentLevel.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(point.y + .5f), 0, currentLevel.height - 1);
            cell = y * currentLevel.width + x;
            float dx = point.x - x, dy = point.y - y;
            side = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx > 0 ? Direction.East : Direction.West) : (dy > 0 ? Direction.North : Direction.South);
            onEdge = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) >= .34f;
            return true;
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= DelayedPreview;
                UnityEditor.EditorApplication.delayCall += DelayedPreview;
            }
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall -= DelayedPreview;
            UnityEditor.EditorApplication.delayCall += DelayedPreview;
        }

        private void DelayedPreview()
        {
            if (this && isActiveAndEnabled && !Application.isPlaying) RefreshPreview();
        }
#endif

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= DelayedPreview;
#endif
            ClearGenerated();
            if (backdropCamera) DisposeObject(backdropCamera.gameObject);
            backdropCamera = null;
            if (editGhost) DisposeObject(editGhost.gameObject);
            editGhost = null; editGhostShape = null;
        }

        private void OnDestroy()
        {
            foreach (var material in materials.Values) DisposeObject(material);
            materials.Clear();
            if (labelFont) DisposeObject(labelFont);
            if (validGhostMaterial) DisposeObject(validGhostMaterial);
            if (invalidGhostMaterial) DisposeObject(invalidGhostMaterial);
            foreach (var mesh in mechanismMeshes.Values) DisposeObject(mesh);
            mechanismMeshes.Clear();
        }

        public void RefreshPreview()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                ClearGenerated();
                if (project == null || project.book == null || project.levels == null || project.levels.Count == 0) return;
                levelIndex = Mathf.Clamp(levelIndex, 0, project.levels.Count - 1);
                var level = project.levels[levelIndex];
                if (level == null || level.width <= 0 || level.height <= 0) return;
                Render(level, project.book, RuleEngine.CreateState(level));
                FitCamera();
#if UNITY_EDITOR
                UnityEditor.SceneView.RepaintAll();
#endif
            }
            finally { refreshing = false; }
        }

        public void Render(LevelData level, RuleBookData book, BoardState state)
        {
            HideEditGhost();
            if (level == null || book == null || state == null) return;
            if (level.width < 1 || level.height < 1 || level.cells == null) { ClearGenerated(); return; }
            EnsureGeometry(level, book);
            currentState = state;
            RenderGround(level, book, state);
            RenderEdges(level, book, state);
            RenderWallJunctions(level);
            SyncActors(level, state, true);
            BoardAtmosphere.Sync(this, level, book, state);
        }

        public IEnumerator AnimateState(LevelData level, RuleBookData book, BoardState state)
        {
            EnsureGeometry(level, book);
            var startingPositions = new Dictionary<int, Vector3>();
            foreach (var item in actorRoots) if (item.Value) startingPositions[item.Key] = item.Value.localPosition;
            // Update terrain immediately when a frame says it changed. Actors interpolate only visually.
            currentState = state;
            RenderGround(level, book, state, true);
            RenderEdges(level, book, state);
            RenderWallJunctions(level);
            SyncActors(level, state, false);
            float elapsed = 0;
            float duration = Mathf.Max(buttonTravels.Count > 0 ? ButtonTravelSeconds : .03f, animationSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
                foreach (var travel in buttonTravels)
                    if (travel.head) travel.head.localPosition = Vector3.Lerp(travel.from, travel.to, t);
                foreach (var item in actorRoots)
                {
                    if (!item.Value) continue;
                    int cell = item.Key == -1 ? state.player : (item.Key < state.boxes.Count ? state.boxes[item.Key] : -1);
                    if (cell < 0) continue;
                    Vector3 target = CellPosition(level, cell);
                    Vector3 from;
                    if (!startingPositions.TryGetValue(item.Key, out from)) from = target;
                    // Teleports use a brief shrink/grow, never sweep through intervening walls.
                    if ((from - target).sqrMagnitude > 2.1f)
                    {
                        item.Value.localPosition = t < .5f ? from : target;
                        item.Value.localScale = Vector3.one * Mathf.Max(.08f, Mathf.Abs(t * 2 - 1));
                    }
                    else item.Value.localPosition = Vector3.Lerp(from, target, t);
                }
                yield return null;
            }
            foreach (var travel in buttonTravels) if (travel.head) travel.head.localPosition = travel.to;
            buttonTravels.Clear();
            SyncActors(level, state, true);
            BoardAtmosphere.Sync(this, level, book, state);
        }

        public void FitCamera()
        {
            EnsureCamera();
            if (!boardCamera || currentLevel == null) return;
            float w = Mathf.Max(1, currentLevel.width);
            float h = Mathf.Max(1, currentLevel.height);
            Vector3 center = transform.TransformPoint(new Vector3((w - 1) * .5f, 0, (h - 1) * .5f));
            float angle = cameraElevation * Mathf.Deg2Rad;
            float distance = Mathf.Max(w, h) + 12;
            // Keep grid rows and columns aligned to the screen. Side-facing portal artwork
            // gets its own small visual offset; readability never rotates the whole board.
            boardCamera.transform.position = center + new Vector3(0, Mathf.Sin(angle), -Mathf.Cos(angle)) * distance;
            boardCamera.transform.rotation = Quaternion.LookRotation(center - boardCamera.transform.position, Vector3.up);
            boardCamera.orthographic = true;
            float aspect = Mathf.Max(.35f, boardCamera.aspect);
            boardCamera.orthographicSize = Mathf.Max((h * Mathf.Sin(angle) + 2.1f) * .5f, (w + 1.6f) * .5f / aspect);
            boardCamera.nearClipPlane = .1f;
            boardCamera.farClipPlane = distance + 40;
            boardCamera.clearFlags = CameraClearFlags.SolidColor;
            boardCamera.backgroundColor = new Color(.79f, .82f, .83f);
        }

        private void EnsureCamera()
        {
            if (!boardCamera) boardCamera = Camera.main;
            if (!boardCamera)
            {
                // A preview does not create scene cameras. Project setup supplies the saved camera.
                if (!Application.isPlaying) return;
                var cameraObject = new GameObject("BoxLab Camera");
                boardCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }
            if (!Application.isPlaying) return;
            // The board uses only part of the screen. Clear the full display first so switching
            // UI routes never leaves pixels from the previous viewport outside the new board.
            if (!backdropCamera)
            {
                var backdrop = new GameObject("BoxLab Backdrop (runtime)");
                backdrop.hideFlags = HideFlags.DontSave;
                backdrop.transform.SetParent(transform, false);
                backdropCamera = backdrop.AddComponent<Camera>();
                backdropCamera.cullingMask = 0;
                backdropCamera.clearFlags = CameraClearFlags.SolidColor;
                backdropCamera.backgroundColor = WorkshopTheme.Backdrop;
                backdropCamera.rect = new Rect(0, 0, 1, 1);
            }
            backdropCamera.depth = boardCamera.depth - 1;
            backdropCamera.targetDisplay = boardCamera.targetDisplay;
            backdropCamera.enabled = boardCamera.enabled;
        }

        private void EnsureGeometry(LevelData level, RuleBookData book)
        {
            if (generated && currentLevel == level && currentBook == book && cellRoots != null && geometryWidth == level.width && geometryHeight == level.height) return;
            ClearGenerated();
            currentLevel = level;
            currentBook = book;
            geometryWidth = level.width; geometryHeight = level.height;
            var root = new GameObject("BoxLab Preview (generated)");
            root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            generated = root.transform;
            generated.SetParent(transform, false);
            cellRoots = new Transform[level.width * level.height];
            cellKeys = new string[cellRoots.Length];
            for (int i = 0; i < cellRoots.Length; i++)
            {
                var cell = new GameObject("Cell " + (i % level.width) + "," + (i / level.width));
                cell.transform.SetParent(generated, false);
                cell.transform.localPosition = CellPosition(level, i);
                cellRoots[i] = cell.transform;
            }
            // The board boundary uses the same low stone model as authored walls. Authoring
            // an edge on this perimeter never draws a second wall through the permanent one.
            for (int x = 0; x < level.width; x++)
            {
                DrawBoundaryWall(new Vector3(x, 0, -.5f), false, 1);
                DrawBoundaryWall(new Vector3(x, 0, level.height - .5f), false, 1);
            }
            for (int y = 0; y < level.height; y++)
            {
                // Trim the ends at the north/south wall thickness to make clean butt joints.
                float start = y == 0 ? .115f : 0, end = y == level.height - 1 ? .115f : 0;
                float center = y + (start - end) * .5f;
                DrawBoundaryWall(new Vector3(-.5f, 0, center), true, 1 - start - end);
                DrawBoundaryWall(new Vector3(level.width - .5f, 0, center), true, 1 - start - end);
            }
        }

        private void DrawBoundaryWall(Vector3 position, bool vertical, float length)
        {
            var root = new GameObject("Permanent perimeter stone wall").transform;
            root.SetParent(generated, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0, vertical ? 90 : 0, 0);
            DrawWall(root);
            root.localScale = new Vector3(length / .95f, 1, 1);
        }

        private static bool IsExteriorEdge(LevelData level, EdgeData edge)
        {
            int x = edge.cell % level.width, y = edge.cell / level.width;
            return edge.direction == Direction.West && x == 0
                || edge.direction == Direction.East && x == level.width - 1
                || edge.direction == Direction.South && y == 0
                || edge.direction == Direction.North && y == level.height - 1;
        }

        private void RenderGround(LevelData level, RuleBookData book, BoardState state, bool animateButtons = false)
        {
            buttonTravels.Clear();
            for (int i = 0; i < cellRoots.Length; i++)
            {
                var cell = i < level.cells.Count ? level.cells[i] : null;
                string ruleId = state.tiles != null && i < state.tiles.Length ? state.tiles[i] : (cell != null ? cell.ruleId : "");
                var rule = book.Find(ruleId);
                string visual = rule == null ? "Plain" : rule.visual.ToString();
                bool active = rule != null && RuleEngine.IsSignalActive(level, book, state, i);
                string key = ruleId + "|" + ((visual == "Plate" || visual == "RotationPlate") && active) + "|" + RuleEngine.GetRotation(level, state, i) + "|" + (cell == null ? -1 : cell.signalStyle);
                if (visual == "Turn") key += "|" + EffectiveDirection(rule, i, level, state, cell);
                if (cellKeys[i] == key) continue;
                cellKeys[i] = key;
                Transform previousHead;
                bool hadButton = buttonHeads.TryGetValue(i, out previousHead) && previousHead;
                Vector3 previousPosition = hadButton ? previousHead.localPosition : Vector3.zero;
                ClearChildren(cellRoots[i]);
                Transform nextHead = DrawCell(cellRoots[i], rule, visual, active, i, level, cell);
                if (nextHead)
                {
                    buttonHeads[i] = nextHead;
                    if (animateButtons && hadButton && Mathf.Abs(previousPosition.y - nextHead.localPosition.y) > .001f)
                    {
                        buttonTravels.Add(new ButtonTravel { head = nextHead, from = previousPosition, to = nextHead.localPosition });
                        nextHead.localPosition = previousPosition;
                    }
                }
                else buttonHeads.Remove(i);
            }
        }

        private Transform DrawCell(Transform root, RuleDefinition rule, string visual, bool active, int index, LevelData level, CellData cell, Direction? previewDirection = null)
        {
            Transform buttonHead = null;
            Color stone = ModuleCap;
            Color color = stone;
            Color parsed;
            if (rule != null && ColorUtility.TryParseHtmlString(rule.colorHex, out parsed)) color = Color.Lerp(stone, parsed, .13f);
            if (visual == "Plain") color = stone * (((index % level.width + index / level.width) % 2 == 0) ? 1f : .97f);
            else if (visual == "Ice") color = new Color(.53f, .68f, .70f);
            bool hole = visual == "Hole";
            bool plain = visual == "Plain";
            // Keep the original atlas on every stone floor. Colored mechanism faces sit above
            // the tile, leaving its textured stone border visible instead of replacing its material.
            if (visual == "Fragile") DrawFragileStone(root);
            else if (hole || !TryArt(root, plain ? "Floor" : "SurfaceFloor", Vector3.zero, null))
                Cube(root, hole ? "Void" : "Floor", new Vector3(0, hole ? -.085f : -.025f, 0), new Vector3(.86f, hole ? .02f : .12f, .86f), hole ? new Color(.025f, .04f, .055f) : color);
            if (!hole) SetShadows(root, false, true); // Floor only: later functional symbols stay unshadowed and legible.
            if (visual == "Turn" || visual == "Selective")
            {
                Cube(root, "Inlaid iron plate", new Vector3(0, .047f, 0), new Vector3(.66f, .009f, .66f), ModuleIron);
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Sphere(root, "Brass rivet", new Vector3(x * .285f, .058f, z * .285f), new Vector3(.036f, .018f, .036f), ModuleBrass);
            }
            if (visual == "Goal")
            {
                DrawGoalMarker(root, new Color(1f, .78f, .20f));
            }
            else if (visual == "Ice")
            {
                Cube(root, "Ice inset", new Vector3(0, .047f, 0), new Vector3(.70f, .008f, .70f), color);
                SquareFrame(root, "Ice metal rim", .70f, .049f, .016f, ModuleMetalFace);
                DrawSnowflake(root, .061f, new Color(.83f, .94f, .96f));
            }
            else if (visual == "Plate" || visual == "RotationPlate")
            {
                buttonHead = DrawButton(root, visual == "RotationPlate", active, cell == null ? 0 : cell.signalStyle, true);
            }
            else if (visual == "PortalEntrance" || visual == "PortalExit")
            {
                DrawPortalVisual(root, visual == "PortalEntrance", cell == null ? 0 : cell.rotation);
            }
            else if (visual == "Turn")
            {
                Direction direction = previewDirection ?? EffectiveDirection(rule, index, level, currentState, cell);
                Arrow(root, Vector3.zero, direction, ModuleChalk, 1);
            }
            else if (visual == "Selective")
            {
                DrawPassagePermissions(root, rule == null ? ActorMask.Player : rule.allowedActors);
            }
            else if (hole)
            {
                Color rim = ModuleStone;
                Cube(root, "Void stone rim N", new Vector3(0, -.022f, .395f), new Vector3(.86f, .11f, .07f), rim);
                Cube(root, "Void stone rim S", new Vector3(0, -.022f, -.395f), new Vector3(.86f, .11f, .07f), rim);
                Cube(root, "Void stone rim E", new Vector3(.395f, -.022f, 0), new Vector3(.07f, .11f, .72f), rim);
                Cube(root, "Void stone rim W", new Vector3(-.395f, -.022f, 0), new Vector3(.07f, .11f, .72f), rim);
                for (int n = -1; n <= 1; n++)
                    Cube(root, "Hazard", new Vector3(n * .22f, -.065f, -.36f), new Vector3(.11f, .012f, .06f), ModuleBrass);
            }
            if (rule == null) GroundText(root, "?", 0, .075f, Color.magenta, .2f);
            return buttonHead;
        }

        private void DrawSnowflake(Transform root, float height, Color color)
        {
            var snowflake = new GameObject("Six arm snowflake").transform;
            snowflake.SetParent(root, false);
            for (int arm = 0; arm < 6; arm++)
            {
                float angle = arm * Mathf.PI / 3;
                Vector3 outward = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                Vector3 tangent = new Vector3(outward.z, 0, -outward.x);
                SymbolSegment(snowflake, "Snowflake arm", Vector3.zero, outward * .29f, height, .027f, color);
                for (int side = -1; side <= 1; side += 2)
                    SymbolSegment(snowflake, "Crystal branch", outward * .165f, outward * .225f + tangent * (.058f * side), height, .020f, color);
            }
        }

        private void SymbolSegment(Transform root, string name, Vector3 from, Vector3 to, float height, float width, Color color)
        {
            Vector3 delta = to - from, center = (from + to) * .5f; center.y = height;
            Cube(root, name, center, new Vector3(width, .012f, delta.magnitude + .003f), color, Quaternion.LookRotation(delta, Vector3.up));
        }

        private Transform DrawButton(Transform root, bool rotation, bool active, int signalStyle, bool showSignal)
        {
            // One moving head: its sidewall and outer edge remain visible around an occupying box.
            var head = new GameObject("Button moving head").transform;
            head.SetParent(root, false);
            head.localPosition = Vector3.up * (active ? ButtonPressedY : ButtonRaisedY);
            if (rotation)
            {
                Cube(head, "Rotation button cap", Vector3.zero, new Vector3(.74f, .10f, .74f), ModuleMetalFace);
                DrawClockwiseArrow(head, ModuleChalk);
            }
            else Cylinder(head, "Pressure button cap", Vector3.zero, new Vector3(.74f, .05f, .74f), ModuleCap);
            if (showSignal)
            {
                // A small corner indicator carries the connection identity, without replacing the button symbol.
                var badge = new GameObject("Button link indicator").transform;
                badge.SetParent(root, false);
                badge.localPosition = new Vector3(.355f, .060f, -.345f);
                Color signal = SignalColor(signalStyle);
                Cylinder(badge, active ? "Active link color" : "Link color", Vector3.zero, new Vector3(.105f, .008f, .105f), active ? Color.Lerp(signal, Color.white, .32f) : signal);
                GroundText(badge, SignalGlyph(signalStyle), 0, .012f, ModuleIron, .105f);
            }
            return head;
        }

        private void DrawClockwiseArrow(Transform root, Color color)
        {
            var symbol = new GameObject("Clockwise rotation arrow").transform;
            symbol.SetParent(root, false);
            const int segments = 24;
            const float radius = .225f;
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(35, 300, i / (float)segments) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(35, 300, (i + 1) / (float)segments) * Mathf.Deg2Rad;
                Vector3 from = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
                Vector3 to = new Vector3(Mathf.Sin(b), 0, Mathf.Cos(b)) * radius;
                SymbolSegment(symbol, "Clockwise arc", from, to, .060f, .034f, color);
            }
            float end = 300 * Mathf.Deg2Rad;
            Vector2 radial = new Vector2(Mathf.Sin(end), Mathf.Cos(end));
            Vector2 tangent = new Vector2(radial.y, -radial.x);
            Vector2 baseCenter = radial * radius - tangent * .006f;
            Mesh tip = PrismMesh("rotation arrow tip", new[] { baseCenter + radial * .073f, baseCenter - radial * .073f, radial * radius + tangent * .11f }, .052f, .070f, 0);
            MeshPart(symbol, "Clockwise arrowhead", tip, color);
        }

        private void DrawFragileStone(Transform root)
        {
            // The optional kit's broken slab replaces the complete floor, preserving its atlas.
            // These geometric shards are only a fallback when that removable resource is absent.
            if (TryArt(root, "FragileFloor", Vector3.zero, null)) return;
            // Separate closed stone prisms leave real narrow gaps down to a recessed foundation.
            // No dark stroke is painted on top of an otherwise solid floor.
            Cube(root, "Recessed fracture bed", new Vector3(0, -.095f, 0), new Vector3(.83f, .012f, .83f), new Color(.26f, .29f, .28f));
            Vector2 center = new Vector2(-.025f, .015f);
            Vector2 bottom = new Vector2(.10f, -.42f), left = new Vector2(-.42f, .075f);
            Vector2 top = new Vector2(.035f, .42f), right = new Vector2(.42f, -.09f);
            Vector2[][] pieces =
            {
                new[] { center, bottom, new Vector2(-.42f, -.42f), left },
                new[] { center, left, new Vector2(-.42f, .42f), top },
                new[] { center, top, new Vector2(.42f, .42f), right },
                new[] { center, right, new Vector2(.42f, -.42f), bottom }
            };
            float[] heights = { .043f, .038f, .052f, .047f };
            float[] shades = { .97f, .90f, 1f, .94f };
            for (int i = 0; i < pieces.Length; i++)
            {
                Vector2 midpoint = Vector2.zero;
                foreach (Vector2 p in pieces[i]) midpoint += p;
                midpoint /= pieces[i].Length;
                for (int p = 0; p < pieces[i].Length; p++) pieces[i][p] = Vector2.Lerp(midpoint, pieces[i][p], .967f);
                Mesh mesh = PrismMesh("fragile stone " + i, pieces[i], -.083f, heights[i], .06f);
                MeshPart(root, "Fractured stone slab " + (i + 1), mesh, ModuleCap * shades[i]);
            }
        }

        private void MeshPart(Transform root, string name, Mesh mesh, Color color)
        {
            var part = new GameObject(name);
            part.transform.SetParent(root, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private Mesh PrismMesh(string key, Vector2[] outline, float bottom, float top, float bevel)
        {
            Mesh cached;
            if (mechanismMeshes.TryGetValue(key, out cached) && cached) return cached;
            var polygon = new List<Vector2>(outline);
            float area = 0;
            Vector2 center = Vector2.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y; center += a;
            }
            if (area > 0) polygon.Reverse();
            center /= polygon.Count;
            var low = new Vector3[polygon.Count]; var shoulder = new Vector3[polygon.Count]; var surface = new Vector3[polygon.Count];
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 inset = Vector2.Lerp(polygon[i], center, bevel);
                low[i] = new Vector3(polygon[i].x, bottom, polygon[i].y);
                shoulder[i] = new Vector3(polygon[i].x, top - (bevel > 0 ? .012f : 0), polygon[i].y);
                surface[i] = new Vector3(inset.x, top, inset.y);
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            Action<Vector3, Vector3, Vector3> triangle = (a, b, c) =>
            {
                int index = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            };
            for (int i = 1; i < polygon.Count - 1; i++)
            { triangle(surface[0], surface[i], surface[i + 1]); triangle(low[0], low[i + 1], low[i]); }
            for (int i = 0; i < polygon.Count; i++)
            {
                int next = (i + 1) % polygon.Count;
                triangle(low[i], low[next], shoulder[next]); triangle(low[i], shoulder[next], shoulder[i]);
                if (bevel > 0)
                { triangle(shoulder[i], shoulder[next], surface[next]); triangle(shoulder[i], surface[next], surface[i]); }
            }
            cached = new Mesh { name = "BoxLab " + key, hideFlags = HideFlags.HideAndDontSave };
            cached.SetVertices(vertices); cached.SetTriangles(triangles, 0); cached.RecalculateNormals(); cached.RecalculateBounds();
            mechanismMeshes[key] = cached;
            return cached;
        }

        private void RenderEdges(LevelData level, RuleBookData book, BoardState state)
        {
            if (level.edges == null) return;
            while (edgeRoots.Count > level.edges.Count)
            {
                int last = edgeRoots.Count - 1;
                if (edgeRoots[last]) DisposeObject(edgeRoots[last].gameObject);
                edgeRoots.RemoveAt(last); edgeKeys.RemoveAt(last);
            }
            while (edgeRoots.Count < level.edges.Count) { edgeRoots.Add(null); edgeKeys.Add(null); }
            for (int i = 0; i < level.edges.Count; i++)
            {
                var edge = level.edges[i];
                if (edge == null || !level.Contains(edge.cell) || IsExteriorEdge(level, edge))
                {
                    if (edgeRoots[i]) DisposeObject(edgeRoots[i].gameObject);
                    edgeRoots[i] = null; edgeKeys[i] = null;
                    continue;
                }
                bool gate = edge.kind.ToString() == "Gate";
                bool open = edge.kind.ToString() == "Open" || (gate && GateOpen(level, book, state, edge));
                string key = edge.cell + "|" + edge.direction + "|" + edge.kind + "|" + open;
                List<int> signals = gate ? GateSignals(level, edge) : null;
                if (signals != null) foreach (int signal in signals)
                    key += "|" + signal + ":" + level.cells[signal].signalStyle + ":" + RuleEngine.IsSignalActive(level, book, state, signal);
                if (edgeKeys[i] == key && edgeRoots[i]) continue;
                edgeKeys[i] = key;
                if (edgeRoots[i]) DisposeObject(edgeRoots[i].gameObject);
                var go = new GameObject(gate ? "Gate" : "Wall");
                go.transform.SetParent(generated, false);
                edgeRoots[i] = go.transform;
                Vector3 offset = DirectionVector(edge.direction) * .5f;
                go.transform.localPosition = CellPosition(level, edge.cell) + offset;
                bool horizontal = edge.direction == Direction.North || edge.direction == Direction.South;
                if (!horizontal) go.transform.localRotation = Quaternion.Euler(0, 90, 0);
                if (!gate && open) continue;
                if (gate)
                {
                    DrawGateBody(go.transform, open);
                    if (signals != null)
                    {
                        // A small rear status rail stays visible without wires or a tall occluding gate.
                        Cube(go.transform, "Signal rail", new Vector3(0, .12f, .14f), new Vector3(.64f, .055f, .12f), ModuleIron);
                        for (int lamp = 0; lamp < signals.Count; lamp++)
                        {
                            int signalCell = signals[lamp];
                            int style = level.cells[signalCell].signalStyle;
                            bool lit = RuleEngine.IsSignalActive(level, book, state, signalCell);
                            Color lampColor = lit ? Color.Lerp(SignalColor(style), Color.white, .28f) : SignalColor(style) * .40f;
                            var indicator = new GameObject("Signal " + (lamp + 1)).transform;
                            indicator.SetParent(go.transform, false);
                            indicator.localPosition = new Vector3((lamp - (signals.Count - 1) * .5f) * .20f, .16f, .14f);
                            Sphere(indicator, lit ? "Lit" : "Unlit", Vector3.zero, Vector3.one * .15f, lampColor);
                            GroundText(indicator, SignalGlyph(style), 0, .088f, lit ? Color.white : new Color(.15f, .19f, .23f), .080f);
                        }
                    }
                }
                else
                {
                    DrawWall(go.transform);
                }
            }
        }

        private void DrawWall(Transform root)
        {
            if (!TryArt(root, "LowWall", Vector3.zero, null))
            {
                Cube(root, "Solid low wall", new Vector3(0, .225f, 0), new Vector3(.95f, .40f, .21f), ModuleStone);
                Cube(root, "Wall cap", new Vector3(0, .435f, 0), new Vector3(.95f, .04f, .23f), ModuleCap);
            }
            SetShadows(root, true, true);
        }

        private void DrawGateBody(Transform root, bool open)
        {
            Color stone = ModuleStone, cap = ModuleCap;
            Color iron = ModuleIron;
            Color indicator = open ? new Color(.27f, .96f, .62f) : new Color(.97f, .43f, .28f);
            for (int side = -1; side <= 1; side += 2)
            {
                Cube(root, "Stone gate post", new Vector3(side * .39f, .23f, 0), new Vector3(.16f, .42f, .24f), stone);
                Cube(root, "Post cap", new Vector3(side * .39f, .45f, 0), new Vector3(.20f, .045f, .27f), cap);
                Cube(root, "Post iron collar", new Vector3(side * .39f, .14f, 0), new Vector3(.172f, .05f, .252f), iron);
                Cube(root, "Stone post joint", new Vector3(side * .39f, .30f, 0), new Vector3(.163f, .012f, .243f), ModuleMetalFace);
                Sphere(root, "Collar rivet", new Vector3(side * .39f, .14f, -.133f), new Vector3(.036f, .036f, .016f), ModuleBrass);
                Cube(root, "Gate status", new Vector3(side * .39f, .479f, 0), new Vector3(.08f, .012f, .08f), indicator);
            }
            if (!open)
            {
                Cube(root, "Iron crossbar", new Vector3(0, .325f, 0), new Vector3(.67f, .065f, .07f), iron);
                Cube(root, "Iron lower bar", new Vector3(0, .10f, 0), new Vector3(.67f, .045f, .065f), iron);
                for (int bar = -2; bar <= 2; bar++)
                    Cube(root, "Iron gate bar", new Vector3(bar * .115f, .22f, 0), new Vector3(.032f, .29f, .043f), iron);
            }
            else
            {
                Cube(root, "Open iron threshold", new Vector3(0, .035f, 0), new Vector3(.67f, .02f, .10f), iron);
                Cube(root, "Open threshold", new Vector3(0, .047f, 0), new Vector3(.52f, .008f, .03f), indicator);
            }
        }

        private static bool GateOpen(LevelData level, RuleBookData book, BoardState state, EdgeData edge)
        {
            return RuleEngine.IsGateOpen(level, book, state, edge, ActorMask.Player);
        }

        private Direction EffectiveDirection(RuleDefinition rule, int index, LevelData level, BoardState state, CellData cell)
        {
            if (rule != null && rule.onEnter != null && state != null)
                foreach (var branch in rule.onEnter)
                {
                    if (branch == null || !RuleEngine.Evaluate(level, currentBook, state, branch.condition, index, ActorMask.Box, cell == null ? null : cell.bindings)) continue;
                    if (branch.actions != null)
                        foreach (var action in branch.actions)
                            if (action != null && action.kind == ActionKind.Move && action.directionMode == DirectionMode.Fixed)
                                return (Direction)(((int)action.direction + RuleEngine.GetRotation(level, state, index)) % 4);
                    break;
                }
            return Direction.East;
        }

        private static List<int> GateSignals(LevelData level, EdgeData edge)
        {
            var result = new List<int>();
            if (edge.bindings != null)
                foreach (var binding in edge.bindings)
                {
                    if (binding == null || binding.cellIds == null) continue;
                    foreach (string id in binding.cellIds)
                    {
                        int index = level.FindCellIndex(id);
                        if (index >= 0 && !result.Contains(index)) result.Add(index);
                        if (result.Count == 3) return result;
                    }
                }
            return result;
        }

        private void SyncActors(LevelData level, BoardState state, bool snap)
        {
            var wanted = new HashSet<int>();
            if (state.player >= 0 && state.player < level.width * level.height)
            {
                wanted.Add(-1);
                EnsureActor(-1, level, state.player, true, snap);
            }
            if (state.boxes != null)
                for (int i = 0; i < state.boxes.Count; i++)
                    if (level.Contains(state.boxes[i]))
                    {
                        wanted.Add(i);
                        EnsureActor(i, level, state.boxes[i], false, snap);
                    }
            var remove = new List<int>();
            foreach (var pair in actorRoots)
                if (!wanted.Contains(pair.Key)) { if (pair.Value) DisposeObject(pair.Value.gameObject); remove.Add(pair.Key); }
            foreach (int key in remove) actorRoots.Remove(key);
        }

        private void EnsureActor(int key, LevelData level, int cell, bool player, bool snap)
        {
            Transform root;
            bool created = !actorRoots.TryGetValue(key, out root) || !root;
            if (created)
            {
                root = new GameObject(player ? "Player pusher" : "Box " + (key + 1)).transform;
                root.SetParent(generated, false);
                actorRoots[key] = root;
                DrawActorVisual(root, player);
            }
            if (snap || created) root.localPosition = CellPosition(level, cell);
            root.localScale = Vector3.one;
        }

        private void DrawActorVisual(Transform root, bool player)
        {
                if (player)
                {
                    if (!TryArt(root, "Pusher", Vector3.zero, null))
                    {
                        Cylinder(root, "Player base", new Vector3(0, .22f, 0), new Vector3(.53f, .18f, .53f), new Color(.96f, .92f, .79f));
                        Sphere(root, "Player top", new Vector3(0, .43f, 0), new Vector3(.40f, .40f, .40f), new Color(.30f, .82f, 1));
                    }
                    SetShadows(root, true, true);
                    Ring(root, .32f, .075f, .03f, new Color(.3f, .84f, 1));
                }
                else
                {
                    if (!TryArt(root, "Crate", Vector3.zero, null))
                    {
                        Cube(root, "Cargo", new Vector3(0, .32f, 0), new Vector3(.64f, .58f, .64f), new Color(.83f, .52f, .23f));
                        Cube(root, "Band X", new Vector3(0, .62f, 0), new Vector3(.66f, .027f, .10f), new Color(1, .78f, .40f));
                        Cube(root, "Band Z", new Vector3(0, .62f, 0), new Vector3(.10f, .027f, .66f), new Color(1, .78f, .40f));
                        Cube(root, "Side band", new Vector3(0, .32f, -.329f), new Vector3(.10f, .56f, .02f), new Color(1, .75f, .37f));
                    }
                    SetShadows(root, true, true);
                }
        }

        private static void SetShadows(Transform root, bool cast, bool receive)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = receive;
            }
        }

        private void Arrow(Transform parent, Vector3 position, Direction direction, Color color, float scale)
        {
            var arrow = new GameObject("Direction").transform;
            arrow.SetParent(parent, false);
            arrow.localPosition = position;
            arrow.localScale = Vector3.one * scale;
            arrow.localRotation = Quaternion.LookRotation(DirectionVector(direction), Vector3.up);
            Cube(arrow, "Arrow stem", new Vector3(0, .06f, -.02f), new Vector3(.075f, .025f, .40f), color);
            Cube(arrow, "Arrow left", new Vector3(-.09f, .06f, .13f), new Vector3(.07f, .025f, .27f), color, Quaternion.Euler(0, 45, 0));
            Cube(arrow, "Arrow right", new Vector3(.09f, .06f, .13f), new Vector3(.07f, .025f, .27f), color, Quaternion.Euler(0, -45, 0));
        }

        private void GroundText(Transform parent, string text, float z, float height, Color color, float size)
        {
            if (!labelFont) labelFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Microsoft YaHei", "sans-serif" }, 48);
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, height, z);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 48;
            // Dynamic OS fonts have generous glyph metrics; keep labels inside a single floor tile.
            mesh.characterSize = Mathf.Min(size * .5f, .22f / Mathf.Max(1, text.Length));
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            if (labelFont) { mesh.font = labelFont; go.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material; }
        }

        private void SquareFrame(Transform parent, string name, float width, float height, float thickness, Color color)
        {
            float side = (width - thickness) * .5f;
            Cube(parent, name + " N", new Vector3(0, height, side), new Vector3(width, .014f, thickness), color);
            Cube(parent, name + " S", new Vector3(0, height, -side), new Vector3(width, .014f, thickness), color);
            Cube(parent, name + " E", new Vector3(side, height, 0), new Vector3(thickness, .014f, width - thickness * 2), color);
            Cube(parent, name + " W", new Vector3(-side, height, 0), new Vector3(thickness, .014f, width - thickness * 2), color);
        }

        private void Ring(Transform parent, float radius, float height, float width, Color color)
        {
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Cube(parent, "Ring", new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius),
                    new Vector3(radius * .34f, .023f, width), color, Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0));
            }
        }

        private void Cube(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Quaternion? rotation = null)
        { Primitive(parent, name, PrimitiveType.Cube, position, scale, color, rotation ?? Quaternion.identity); }
        private void Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        { Primitive(parent, name, PrimitiveType.Cylinder, position, scale, color, Quaternion.identity); }
        private void Sphere(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        { Primitive(parent, name, PrimitiveType.Sphere, position, scale, color, Quaternion.identity); }

        private void Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            if (collider) { collider.enabled = false; DisposeObject(collider); }
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = GetMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private Material GetMaterial(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            Material material;
            if (materials.TryGetValue(key, out material) && material) return material;
            if (surfaceMaterial) material = new Material(surfaceMaterial);
            else
            {
                var shader = Shader.Find("Standard");
                if (!shader) shader = Shader.Find("Unlit/Color");
                material = new Material(shader);
            }
            material.color = color;
            material.hideFlags = HideFlags.HideAndDontSave;
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .18f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .025f);
            }
            materials[key] = material;
            return material;
        }

        private bool TryArt(Transform parent, string key, Vector3 position, Material overrideMaterial)
        {
            GameObject prefab;
            if (!optionalArt.TryGetValue(key, out prefab) || !prefab)
            {
                prefab = Resources.Load<GameObject>("BoxLabArt/" + key);
                optionalArt[key] = prefab;
            }
            if (!prefab) return false;
            GameObject instance = Instantiate(prefab, parent, false);
            instance.name = key + " (optional art)";
            instance.transform.localPosition = position;
            foreach (var collider in instance.GetComponentsInChildren<Collider>())
            { collider.enabled = false; DisposeObject(collider); }
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (overrideMaterial && !((key == "Floor" || key == "SurfaceFloor") && renderer.name.StartsWith("Tile edge", StringComparison.Ordinal)))
                {
                    var replacement = renderer.sharedMaterials;
                    for (int i = 0; i < replacement.Length; i++) replacement[i] = overrideMaterial;
                    renderer.sharedMaterials = replacement;
                }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return true;
        }

        private void ClearGenerated()
        {
            ClearWallJunctions();
            HideEditGhost();
            if (generated) DisposeObject(generated.gameObject);
            // Clean any prior transient root left by a domain reload.
            for (int i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name == "BoxLab Preview (generated)") DisposeObject(transform.GetChild(i).gameObject);
            generated = null;
            cellRoots = null;
            cellKeys = null;
            buttonHeads.Clear();
            buttonTravels.Clear();
            actorRoots.Clear();
            edgeRoots.Clear();
            edgeKeys.Clear();
            currentLevel = null;
            currentBook = null;
            currentState = null;
        }

        private static void ClearChildren(Transform parent)
        { for (int i = parent.childCount - 1; i >= 0; i--) DisposeObject(parent.GetChild(i).gameObject); }

        private static void DisposeObject(UnityEngine.Object item)
        {
            if (!item) return;
            if (item is GameObject go) go.SetActive(false);
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }

        private static Vector3 CellPosition(LevelData level, int index)
        { return new Vector3(index % level.width, 0, index / level.width); }
        private static Vector3 DirectionVector(Direction direction)
        {
            switch (direction)
            {
                case Direction.North: return Vector3.forward;
                case Direction.East: return Vector3.right;
                case Direction.South: return Vector3.back;
                default: return Vector3.left;
            }
        }
        private static string Short(string value) { return value.Length > 7 ? value.Substring(0, 7) : value; }
    }
}
