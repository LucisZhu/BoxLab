#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BoxLab.Editor
{
    /// <summary>Authoring edits serialized data; scene preview and play both consume that same data.</summary>
    public sealed class BoxLabWindow : EditorWindow
    {
        enum Brush { Select, Floor, Player, Box, EraseActor, Edge, WallPreset, MoveCell }
        [SerializeField] BoxProject project;
        [SerializeField] int levelIndex;
        [SerializeField] string brushRule;
        [SerializeField] string definitionId;
        [SerializeField] Brush brush = Brush.Select;
        [SerializeField] EdgeKind edgeKind = EdgeKind.Wall;
        [SerializeField] int selectedCell = -1, edgeCell = -1;
        [SerializeField] Direction edgeDirection;
        [SerializeField] bool selectEdge, definitionTab;
        [SerializeField] float zoom = 48;
        [SerializeField] int preset = 1, rotation, resizeW = 5, resizeH = 5;
        [SerializeField] string currentPair = "P1";
        Vector2 canvasScroll, paletteScroll, inspectorScroll, issuesScroll;
        List<ValidationIssue> issues = new List<ValidationIssue>();
        SokobanSolver solver;
        bool playAfterSearch;
        string status = "选择地块，点击或拖动绘制。编辑结果即时同步到场景。";
        int movingCell = -1;
        string pickingKey;
        double lastPreview;
        bool previewQueued, stroke;
        int strokeGroup = -1;
        GUIStyle titleStyle, smallStyle, sectionStyle;
        LevelData Level => project && project.levels != null && levelIndex >= 0 && levelIndex < project.levels.Count ? project.levels[levelIndex] : null;
        RuleDefinition FindRule(string id) => project && project.book != null ? project.book.Find(id) : null;

        [MenuItem("Tools/BoxLab/地图与规则编辑器")]
        public static void Open()
        {
            var window = GetWindow<BoxLabWindow>("BoxLab · 地图与规则");
            window.minSize = new Vector2(1060, 650);
            window.Show();
        }

        void OnEnable()
        {
            // Unity's hot reload may restore private transient strings as empty instead of null.
            // Picking and drag operations are gestures, never persistent authoring state.
            pickingKey = null; movingCell = -1; stroke = false; strokeGroup = -1;
            solver = null; playAfterSearch = false;
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayState;
            if (!project)
            {
                var paths = AssetDatabase.FindAssets("t:BoxProject");
                if (paths.Length > 0) project = AssetDatabase.LoadAssetAtPath<BoxProject>(AssetDatabase.GUIDToAssetPath(paths[0]));
            }
            if (project) levelIndex = project.selectedLevel;
            SyncSize();
            previewQueued = true;
        }
        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayState;
            solver?.Cancel();
        }
        void PlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) { previewQueued = true; Repaint(); }
        }
        void Tick()
        {
            if (solver != null && solver.Status == SolveStatus.Searching) { solver.Step(150); Repaint(); }
            if (playAfterSearch && solver != null && solver.Status != SolveStatus.Searching)
            {
                playAfterSearch = false;
                if (solver.Status == SolveStatus.Solved) LaunchPlay();
                else if (solver.Status == SolveStatus.Unsolvable) status = "编译未通过：完整搜索已证明当前地图无解，请修改地图。";
                else if (solver.Status == SolveStatus.Unknown) status = "结构有效，但预算内尚未验证可解性；可查看诊断后点击“仍要试玩”。";
                else status = "编译搜索已取消或未通过，没有进入试玩。";
                Repaint();
            }
            if (previewQueued && !EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup - lastPreview > .08)
            { previewQueued = false; RefreshPreview(); lastPreview = EditorApplication.timeSinceStartup; }
        }
        void OnUndo()
        {
            CancelSearch(); issues.Clear();
            if (project && project.levels.Count > 0) levelIndex = Mathf.Clamp(project.selectedLevel, 0, project.levels.Count - 1);
            if (Level != null && selectedCell >= Level.cells.Count) selectedCell = -1;
            pickingKey = null; movingCell = -1; SyncSize(); previewQueued = true; Repaint();
        }
        void SyncSize() { if (Level != null) { resizeW = Level.width; resizeH = Level.height; } }
        void ResetSelection() { selectedCell = -1; selectEdge = false; movingCell = -1; pickingKey = null; }
        void Record(string label) { if (project) Undo.RecordObject(project, label); }
        void Changed(string message = null)
        {
            CancelSearch(); issues.Clear(); EditorUtility.SetDirty(project); previewQueued = true;
            if (message != null) status = message;
            Repaint();
        }
        void CancelSearch() { playAfterSearch = false; if (solver != null) { solver.Cancel(); solver = null; } }
        void RefreshPreview()
        {
            if (!project || Level == null) return;
            var views = FindObjectsOfType<BoardView>();
            foreach (var view in views) { view.project = project; view.levelIndex = levelIndex; view.RefreshPreview(); }
            SceneView.RepaintAll();
        }
        void Styles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            smallStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = false };
        }
        void OnGUI()
        {
            Styles();
            DrawHeader();
            if (!project)
            {
                GUILayout.Space(32); GUILayout.Label("BoxLab 关卡工坊", titleStyle);
                EditorGUILayout.HelpBox("初始化会创建示例关卡、规则库和可直接运行的场景。随后即可在本窗口绘制地图，不需要拖放 Prefab。", MessageType.Info);
                if (GUILayout.Button("初始化 / 打开示例工程", GUILayout.Height(38))) { project = ProjectSetup.EnsureProject(); levelIndex = project.selectedLevel; SyncSize(); }
                return;
            }
            if (Level == null) { if (GUILayout.Button("创建第一张地图")) NewLevel(); return; }
            if (!string.IsNullOrEmpty(pickingKey))
            {
                var bindings = CurrentBindings();
                if (string.IsNullOrWhiteSpace(pickingKey) || bindings == null || !bindings.Any(b => b != null && b.key == pickingKey)) pickingKey = null;
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawLevelBar();
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawPalette();
                    using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(340), GUILayout.ExpandWidth(true))) { DrawCanvas(); DrawDiagnostics(); }
                    DrawInspector();
                }
            }
            if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("正在试玩：运行时改变不会写回地图。按 Esc 或停止 Play 返回编辑。", MessageType.Info);
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }
        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("BOX LAB", EditorStyles.boldLabel, GUILayout.Width(75));
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    var next = (BoxProject)EditorGUILayout.ObjectField(project, typeof(BoxProject), false, GUILayout.Width(200));
                    if (next != project) { project = next; levelIndex = project ? project.selectedLevel : 0; ResetSelection(); SyncSize(); CancelSearch(); previewQueued = true; }
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(44)) && project) SaveProject();
                    if (GUILayout.Button("导出", EditorStyles.toolbarButton, GUILayout.Width(44))) Export();
                    if (GUILayout.Button("导入", EditorStyles.toolbarButton, GUILayout.Width(44))) Import();
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(50))) Validate();
                    if (GUILayout.Button("搜索解法", EditorStyles.toolbarButton, GUILayout.Width(70))) Search();
                }
                var old = GUI.backgroundColor; GUI.backgroundColor = new Color(.38f, .85f, .65f);
                string playLabel = EditorApplication.isPlaying ? "■ 返回编辑" : playAfterSearch ? "取消编译搜索" : solver != null && solver.Status == SolveStatus.Unknown ? "▶ 仍要试玩" : "▶ 编译并试玩";
                if (GUILayout.Button(playLabel, EditorStyles.toolbarButton, GUILayout.Width(112))) Play();
                GUI.backgroundColor = old;
            }
        }
        void DrawLevelBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var names = project.levels.Select((l, i) => (i + 1) + ". " + l.name).ToArray();
                int next = EditorGUILayout.Popup(levelIndex, names, GUILayout.Width(210));
                if (next != levelIndex) { Record("切换地图"); levelIndex = next; project.selectedLevel = next; ResetSelection(); SyncSize(); Changed(); }
                if (GUILayout.Button("新建", GUILayout.Width(46))) NewLevel();
                if (GUILayout.Button("复制", GUILayout.Width(46))) DuplicateLevel();
                using (new EditorGUI.DisabledScope(project.levels.Count < 2))
                    if (GUILayout.Button("删除", GUILayout.Width(46))) { Record("删除关卡"); project.levels.RemoveAt(levelIndex); levelIndex = Mathf.Clamp(levelIndex, 0, project.levels.Count - 1); project.selectedLevel = levelIndex; ResetSelection(); SyncSize(); Changed("关卡已删除，可 Ctrl+Z 撤销。"); }
                GUILayout.Space(10);
                Record("编辑关卡名称"); EditorGUI.BeginChangeCheck();
                Level.name = EditorGUILayout.TextField(Level.name, GUILayout.MinWidth(95));
                if (EditorGUI.EndChangeCheck()) Changed();
                GUILayout.Label("宽", GUILayout.Width(16)); resizeW = EditorGUILayout.IntField(resizeW, GUILayout.Width(38));
                GUILayout.Label("高", GUILayout.Width(16)); resizeH = EditorGUILayout.IntField(resizeH, GUILayout.Width(38));
                if (GUILayout.Button("调整大小", GUILayout.Width(70))) Resize();
            }
        }
        void DrawPalette()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(200)))
            {
                paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);
                GUILayout.Label("搭建工具", sectionStyle);
                ToolButton("选择 / 查看属性", Brush.Select);
                using (new EditorGUILayout.HorizontalScope()) { ToolButton("人物", Brush.Player); ToolButton("箱子", Brush.Box); }
                ToolButton("擦除人物 / 箱子", Brush.EraseActor);
                ToolButton("移动地块（保留关联）", Brush.MoveCell);
                GUILayout.Space(8); GUILayout.Label("地块定义", sectionStyle);
                foreach (var rule in project.book.rules.Where(r => !r.boundary))
                {
                    bool active = brush == Brush.Floor && brushRule == rule.id;
                    var old = GUI.backgroundColor; GUI.backgroundColor = active ? new Color(.4f, .85f, .75f) : RuleColor(rule);
                    if (GUILayout.Button(rule.name, GUILayout.Height(25))) { brush = Brush.Floor; brushRule = rule.id; definitionId = rule.id; movingCell = -1; pickingKey = null; }
                    GUI.backgroundColor = old;
                }
                GUILayout.Space(5);
                if (GUILayout.Button("＋ 新建地块定义")) CreateRule(false);
                GUILayout.Space(10); GUILayout.Label("边墙与通行门", sectionStyle);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool wall = brush == Brush.Edge && edgeKind == EdgeKind.Wall;
                    bool erase = brush == Brush.Edge && edgeKind == EdgeKind.Open;
                    if (GUILayout.Toggle(wall, "墙", "Button") && !wall) { brush = Brush.Edge; edgeKind = EdgeKind.Wall; pickingKey = null; }
                    if (GUILayout.Toggle(erase, "擦除边", "Button") && !erase) { brush = Brush.Edge; edgeKind = EdgeKind.Open; pickingKey = null; }
                }
                foreach (var rule in project.book.rules.Where(r => r.boundary))
                {
                    bool active = brush == Brush.Edge && edgeKind == EdgeKind.Gate && brushRule == rule.id;
                    if (GUILayout.Toggle(active, rule.name, "Button") && !active)
                    { brush = Brush.Edge; edgeKind = EdgeKind.Gate; brushRule = rule.id; definitionId = rule.id; pickingKey = null; }
                }
                if (GUILayout.Button("＋ 新建边界定义")) CreateRule(true);
                GUILayout.Space(4);
                preset = EditorGUILayout.Popup("墙形", preset, new[] { "无墙", "一面", "两面相邻", "两面相对", "三面", "四面" });
                rotation = EditorGUILayout.IntSlider("旋转", rotation, 0, 3);
                ToolButton("墙形笔刷", Brush.WallPreset);
                GUILayout.Space(10); GUILayout.Label("传送配对", sectionStyle);
                currentPair = EditorGUILayout.TextField("配对标识", currentPair);
                if (GUILayout.Button("新配对标识")) currentPair = "P" + Guid.NewGuid().ToString("N").Substring(0, 5);
                EditorGUILayout.LabelField("入口和出口使用同一标识。移动地块保留其身份及引用。", smallStyle);
                GUILayout.Space(10);
                EditorGUILayout.LabelField("拖动连续绘制 · 右键选择\nCtrl+Z 撤销 · Ctrl+S 保存\n地块与边界独立，放门覆写该边的墙。", smallStyle);
                EditorGUILayout.EndScrollView();
            }
        }
        void ToolButton(string label, Brush tool)
        {
            bool active = brush == tool;
            if (GUILayout.Toggle(active, label, "Button", GUILayout.Height(25)) && !active) { brush = tool; if (tool != Brush.MoveCell) movingCell = -1; pickingKey = null; }
        }
        void DrawCanvas()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("地图画布  " + Level.width + " × " + Level.height, EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace(); GUILayout.Label("缩放", GUILayout.Width(30)); zoom = GUILayout.HorizontalSlider(zoom, 28, 84, GUILayout.Width(100));
                if (GUILayout.Button("场景预览", EditorStyles.toolbarButton, GUILayout.Width(66))) { RefreshPreview(); SceneView.FocusWindowIfItsOpen<SceneView>(); }
            }
            if (!string.IsNullOrEmpty(pickingKey))
            {
                EditorGUILayout.HelpBox("点选地图，为关联「" + pickingKey + "」添加 / 移除格子。", MessageType.Info);
                if (GUILayout.Button("完成关联选择")) pickingKey = null;
            }
            canvasScroll = EditorGUILayout.BeginScrollView(canvasScroll, GUILayout.ExpandHeight(true));
            var map = GUILayoutUtility.GetRect(Level.width * zoom + 42, Level.height * zoom + 42, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            map.x += 26; map.y += 14; map.width = Level.width * zoom; map.height = Level.height * zoom;
            for (int y = 0; y < Level.height; y++)
            {
                GUI.Label(new Rect(map.x - 24, map.y + (Level.height - 1 - y) * zoom, 24, zoom), (y + 1).ToString(), EditorStyles.centeredGreyMiniLabel);
                for (int x = 0; x < Level.width; x++) DrawCell(new Rect(map.x + x * zoom, map.y + (Level.height - 1 - y) * zoom, zoom, zoom), y * Level.width + x);
            }
            for (int x = 0; x < Level.width; x++) GUI.Label(new Rect(map.x + x * zoom, map.yMax + 3, zoom, 18), Column(x), EditorStyles.centeredGreyMiniLabel);
            DrawLinks(map);
            foreach (var e in Level.edges)
            {
                if (e.cell < 0 || e.cell >= Level.cells.Count || e.kind == EdgeKind.Open) continue;
                var r = CellRect(map, e.cell); var color = e.kind == EdgeKind.Wall ? new Color(.8f, .83f, .86f) : new Color(1, .66f, .23f);
                DrawEdge(r, e.direction, color, 5);
                if (selectEdge && edgeCell == e.cell && edgeDirection == e.direction) DrawEdge(r, e.direction, Color.cyan, 2);
            }
            Handles.BeginGUI(); Handles.color = new Color(.65f, .72f, .79f); Handles.DrawAAPolyLine(3, new Vector3(map.x, map.y), new Vector3(map.xMax, map.y), new Vector3(map.xMax, map.yMax), new Vector3(map.x, map.yMax), new Vector3(map.x, map.y)); Handles.EndGUI();
            var ev = Event.current;
            if (map.Contains(ev.mousePosition) && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && (ev.button == 0 || ev.button == 1))
            {
                int x = Mathf.FloorToInt((ev.mousePosition.x - map.x) / zoom), y = Level.height - 1 - Mathf.FloorToInt((ev.mousePosition.y - map.y) / zoom);
                int index = y * Level.width + x;
                var rect = CellRect(map, index);
                var relative = ev.mousePosition - rect.position;
                Direction dir = NearestEdge(relative);
                bool nearEdge = Mathf.Min(relative.x, relative.y, zoom - relative.x, zoom - relative.y) < 8;
                if (ev.type == EventType.MouseDown) { Undo.IncrementCurrentGroup(); strokeGroup = Undo.GetCurrentGroup(); stroke = true; }
                HandleCanvas(index, dir, nearEdge, ev.button == 1, ev.type == EventType.MouseDrag);
                ev.Use();
            }
            if (ev.type == EventType.MouseUp && stroke) { stroke = false; if (strokeGroup >= 0) Undo.CollapseUndoOperations(strokeGroup); strokeGroup = -1; }
            if (ev.type == EventType.KeyDown && ev.control && ev.keyCode == KeyCode.S) { SaveProject(); ev.Use(); }
            EditorGUILayout.EndScrollView();
        }
        static string Column(int x) { return x < 26 ? ((char)('A' + x)).ToString() : (x + 1).ToString(); }
        Rect CellRect(Rect map, int index) => new Rect(map.x + index % Level.width * zoom, map.y + (Level.height - 1 - index / Level.width) * zoom, zoom, zoom);
        string PositionName(int index) => index < 0 || index >= Level.cells.Count ? "地图外" : Column(index % Level.width) + (index / Level.width + 1);
        static Color RuleColor(RuleDefinition rule)
        {
            Color c; return rule != null && ColorUtility.TryParseHtmlString(rule.colorHex, out c) ? c : new Color(.27f, .32f, .39f);
        }
        void DrawCell(Rect rect, int index)
        {
            if (index >= Level.cells.Count) return;
            var cell = Level.cells[index]; var rule = FindRule(cell.ruleId); var color = RuleColor(rule);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), color * .72f);
            if (index == selectedCell && !selectEdge) { Handles.BeginGUI(); Handles.DrawSolidRectangleWithOutline(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), Color.clear, Color.cyan); Handles.EndGUI(); }
            if (index == movingCell) EditorGUI.DrawRect(new Rect(rect.x + 4, rect.y + 4, 7, 7), Color.magenta);
            var labelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(.83f, .87f, .91f) } };
            string label = rule == null ? "缺失" : rule.name;
            if (label.Length > 5) label = label.Substring(0, 5);
            GUI.Label(new Rect(rect.x + 2, rect.y + 1, rect.width - 4, 17), label, labelStyle);
            string actor = Level.player == index ? "人" : Level.boxes.Contains(index) ? "箱" : "";
            if (actor.Length > 0)
            {
                var ar = new Rect(rect.x + rect.width * .28f, rect.y + rect.height * .4f, rect.width * .44f, rect.height * .44f);
                EditorGUI.DrawRect(ar, actor == "人" ? new Color(.2f, .85f, 1) : new Color(.95f, .64f, .22f));
                GUI.Label(ar, actor, new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } });
            }
            if (!string.IsNullOrEmpty(cell.portalPair)) GUI.Label(new Rect(rect.x, rect.yMax - 14, rect.width, 14), cell.portalPair, labelStyle);
            if (issues.Any(i => i.cellIndex == index && i.severity == ValidationSeverity.Error)) GUI.Label(new Rect(rect.xMax - 15, rect.y + 1, 15, 20), "!", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1, .35f, .3f) } });
        }
        void DrawLinks(Rect map)
        {
            if (selectedCell < 0 || selectedCell >= Level.cells.Count) return;
            var ids = new HashSet<string>();
            foreach (var b in CurrentBindings() ?? new List<BindingData>()) if (b.cellIds != null) foreach (var id in b.cellIds) ids.Add(id);
            var selected = Level.cells[selectedCell];
            if (!selectEdge && !string.IsNullOrEmpty(selected.portalPair)) foreach (var c in Level.cells) if (c.portalPair == selected.portalPair && c.id != selected.id) ids.Add(c.id);
            Handles.BeginGUI(); Handles.color = new Color(.3f, .95f, 1, .85f); var from = CellRect(map, selectedCell).center;
            foreach (var id in ids) { int index = Level.cells.FindIndex(c => c.id == id); if (index >= 0) Handles.DrawDottedLine(from, CellRect(map, index).center, 5); }
            Handles.EndGUI();
        }
        static void DrawEdge(Rect r, Direction d, Color c, float thickness)
        {
            if (d == Direction.North) EditorGUI.DrawRect(new Rect(r.x, r.y - thickness / 2, r.width, thickness), c);
            else if (d == Direction.South) EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness / 2, r.width, thickness), c);
            else if (d == Direction.East) EditorGUI.DrawRect(new Rect(r.xMax - thickness / 2, r.y, thickness, r.height), c);
            else EditorGUI.DrawRect(new Rect(r.x - thickness / 2, r.y, thickness, r.height), c);
        }
        Direction NearestEdge(Vector2 p)
        {
            float best = p.y; Direction d = Direction.North;
            if (zoom - p.x < best) { best = zoom - p.x; d = Direction.East; }
            if (zoom - p.y < best) { best = zoom - p.y; d = Direction.South; }
            if (p.x < best) d = Direction.West;
            return d;
        }
        bool NormalizeEdge(ref int cell, ref Direction dir)
        {
            int x = cell % Level.width, y = cell / Level.width;
            if (dir == Direction.West) { if (x == 0) return false; cell--; dir = Direction.East; }
            if (dir == Direction.South) { if (y == 0) return false; cell -= Level.width; dir = Direction.North; }
            if (dir == Direction.East && cell % Level.width == Level.width - 1) return false;
            if (dir == Direction.North && cell / Level.width == Level.height - 1) return false;
            return true;
        }
        EdgeData SelectedEdge => Level == null ? null : Level.edges.Find(e => e.cell == edgeCell && e.direction == edgeDirection);
        void SetEdge(int cell, Direction dir, EdgeKind kind, string rule)
        {
            if (!NormalizeEdge(ref cell, ref dir)) return;
            var existing = Level.edges.Find(e => e.cell == cell && e.direction == dir);
            if (kind == EdgeKind.Open) { if (existing != null) Level.edges.Remove(existing); return; }
            if (existing == null) { existing = new EdgeData { cell = cell, direction = dir, bindings = new List<BindingData>() }; Level.edges.Add(existing); }
            if (existing.kind != kind || existing.ruleId != rule) existing.bindings.Clear();
            existing.kind = kind; existing.ruleId = kind == EdgeKind.Gate ? rule : "";
        }
        void HandleCanvas(int index, Direction dir, bool nearEdge, bool right, bool drag)
        {
            if (!string.IsNullOrEmpty(pickingKey))
            {
                if (drag || right) return;
                var list = CurrentBindings(); if (list == null) return;
                Record("编辑关联格子"); var binding = list.Find(b => b.key == pickingKey);
                if (binding == null) { binding = new BindingData { key = pickingKey, cellIds = new List<string>() }; list.Add(binding); }
                string id = Level.cells[index].id;
                if (!binding.cellIds.Remove(id)) binding.cellIds.Add(id);
                Changed(); return;
            }
            if (right || brush == Brush.Select)
            {
                if (drag) return; selectedCell = index; selectEdge = false;
                if (nearEdge) { int c = index; var d = dir; if (NormalizeEdge(ref c, ref d) && Level.edges.Any(e => e.cell == c && e.direction == d)) { selectEdge = true; edgeCell = c; edgeDirection = d; } }
                definitionId = selectEdge ? SelectedEdge?.ruleId : Level.cells[index].ruleId; definitionTab = false; Repaint(); return;
            }
            Record("绘制地图");
            if (brush == Brush.Floor)
            {
                var cell = Level.cells[index]; if (cell.ruleId == brushRule && (!IsPortal(FindRule(brushRule)) || cell.portalPair == currentPair)) return;
                cell.ruleId = brushRule; cell.bindings = new List<BindingData>(); cell.portalPair = IsPortal(FindRule(brushRule)) ? currentPair : ""; cell.rotation = rotation;
            }
            else if (brush == Brush.Player) { if (Level.boxes.Contains(index)) { status = "人物和箱子不能重叠。"; return; } Level.player = index; }
            else if (brush == Brush.Box) { if (Level.player == index) { status = "人物和箱子不能重叠。"; return; } if (!Level.boxes.Contains(index)) Level.boxes.Add(index); }
            else if (brush == Brush.EraseActor) { if (Level.player == index) Level.player = -1; Level.boxes.Remove(index); }
            else if (brush == Brush.Edge) { SetEdge(index, dir, edgeKind, brushRule); int c = index; var d = dir; NormalizeEdge(ref c, ref d); edgeCell = c; edgeDirection = d; selectEdge = true; }
            else if (brush == Brush.WallPreset)
            {
                int[] masks = { 0, 1, 3, 5, 7, 15 }; int mask = masks[preset];
                for (int n = 0; n < 4; n++) SetEdge(index, (Direction)((n + rotation) % 4), (mask & (1 << n)) != 0 ? EdgeKind.Wall : EdgeKind.Open, "");
            }
            else if (brush == Brush.MoveCell)
            {
                if (drag) return;
                if (movingCell < 0) { movingCell = index; status = "选择目标格，与源地块交换；地块身份、配对和关联会保留。"; Repaint(); return; }
                var cell = Level.cells[index]; Level.cells[index] = Level.cells[movingCell]; Level.cells[movingCell] = cell; movingCell = -1;
            }
            selectedCell = index; if (brush != Brush.Edge) selectEdge = false;
            Changed();
        }
        static bool IsPortal(RuleDefinition r) => r != null && (r.visual.ToString().Contains("Portal"));
        void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(340)))
            {
                definitionTab = GUILayout.Toolbar(definitionTab ? 1 : 0, new[] { "当前实例", "规则定义" }) == 1;
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                if (definitionTab) DrawDefinition(); else DrawInstance();
                EditorGUILayout.EndScrollView();
            }
        }
        void DrawInstance()
        {
            if (selectedCell < 0 || selectedCell >= Level.cells.Count) { EditorGUILayout.HelpBox("选择地图中的格子或边界，编辑实例关联；在“规则定义”中创建和组合新规则。", MessageType.Info); return; }
            var cell = Level.cells[selectedCell]; var edge = selectEdge ? SelectedEdge : null;
            GUILayout.Label((selectEdge ? "边界 · " : "格子 · ") + PositionName(selectedCell), sectionStyle);
            if (selectEdge && edge == null) { EditorGUILayout.HelpBox("开放边界没有独立配置。", MessageType.None); return; }
            string ruleId = selectEdge ? edge.ruleId : cell.ruleId; var rule = FindRule(ruleId);
            EditorGUILayout.LabelField("定义", rule != null ? rule.name : selectEdge ? edge.kind.ToString() : "缺失定义");
            if (rule != null && GUILayout.Button("打开共享定义")) { definitionId = rule.id; definitionTab = true; }
            Record("编辑实例属性"); EditorGUI.BeginChangeCheck();
            if (!selectEdge)
            {
                EditorGUILayout.LabelField("身份", cell.id, EditorStyles.miniLabel);
                cell.rotation = EditorGUILayout.IntSlider(new GUIContent("方向旋转", "每档顺时针旋转 90 度，同时影响显示和固定方向移动效果。"), cell.rotation, 0, 3);
                if (IsPortal(rule))
                {
                    cell.portalPair = EditorGUILayout.TextField("传送配对", cell.portalPair);
                    EditorGUILayout.HelpBox("每组必须恰好一个入口和一个出口。人物可步行经过，但不传送。", MessageType.Info);
                    if (GUILayout.Button("选中另一端")) { int other = Level.cells.FindIndex(c => c != cell && c.portalPair == cell.portalPair); if (other >= 0) selectedCell = other; }
                }
            }
            if (EditorGUI.EndChangeCheck()) Changed();
            GUILayout.Space(8); GUILayout.Label("本实例的格子关联", sectionStyle);
            EditorGUILayout.LabelField("名称与规则中的“关联键”一致。一个关联可选择多个格子；也会列出运行中可能变为的地块所需的关联。", smallStyle);
            DrawBindings();
        }
        List<BindingData> CurrentBindings()
        {
            if (Level == null || selectedCell < 0 || selectedCell >= Level.cells.Count) return null;
            if (selectEdge) return SelectedEdge?.bindings;
            return Level.cells[selectedCell].bindings;
        }
        void DrawBindings()
        {
            var list = CurrentBindings(); if (list == null) return;
            var rule = FindRule(selectEdge ? SelectedEdge?.ruleId : Level.cells[selectedCell].ruleId);
            var expected = rule == null ? new List<string>() : selectEdge ? RuleForm.BindingKeys(rule) : RuleForm.BindingKeysForInstance(Level, project.book, selectedCell);
            foreach (var key in expected)
                if (!list.Any(b => b.key == key) && GUILayout.Button("＋ 添加规则需要的关联：" + key)) { Record("添加关联"); list.Add(new BindingData { key = key, cellIds = new List<string>() }); Changed(); }
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Record("修改关联名称"); EditorGUI.BeginChangeCheck(); b.key = EditorGUILayout.TextField(b.key);
                        if (EditorGUI.EndChangeCheck()) Changed();
                        if (GUILayout.Button("×", GUILayout.Width(23))) { Record("移除关联"); list.RemoveAt(i); pickingKey = null; Changed(); break; }
                    }
                    string label = b.cellIds.Count == 0 ? "尚未选择格子" : string.Join("、", b.cellIds.Select(id => { int c = Level.cells.FindIndex(t => t.id == id); return c < 0 ? "失效引用" : PositionName(c); }));
                    EditorGUILayout.LabelField(label, smallStyle);
                    bool pickingThis = !string.IsNullOrEmpty(pickingKey) && pickingKey == b.key;
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(b.key)))
                        if (GUILayout.Button(pickingThis ? "完成点选" : "在地图上添加 / 移除格子")) pickingKey = pickingThis ? null : b.key;
                }
            }
            if (GUILayout.Button("＋ 自定义关联")) { Record("添加关联"); list.Add(new BindingData { key = "targets" + (list.Count + 1), cellIds = new List<string>() }); Changed(); }
        }
        void DrawDefinition()
        {
            var rules = project.book.rules;
            int index = rules.FindIndex(r => r.id == definitionId);
            if (index < 0 && rules.Count > 0) { index = 0; definitionId = rules[0].id; }
            if (index < 0) return;
            int next = EditorGUILayout.Popup("编辑定义", index, rules.Select(r => (r.boundary ? "[边] " : "[格] ") + r.name).ToArray());
            if (next != index) { index = next; definitionId = rules[index].id; }
            var rule = rules[index];
            EditorGUILayout.HelpBox("共享定义：修改会影响所有使用它的实例。要独立变化，请先复制。条件、分支和操作均可编辑。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制为新的定义")) { DuplicateRule(rule); return; }
                using (new EditorGUI.DisabledScope(rule.builtIn)) if (GUILayout.Button("删除", GUILayout.Width(45))) { DeleteRule(rule); return; }
            }
            Record("编辑规则定义");
            if (RuleForm.Draw(rule, project.book)) Changed();
        }
        void CreateRule(bool boundary)
        {
            Record("新建规则定义"); var template = project.book.rules.FirstOrDefault(r => r.boundary == boundary);
            var r = template == null ? new RuleDefinition() : JsonUtility.FromJson<RuleDefinition>(JsonUtility.ToJson(template));
            r.id = Guid.NewGuid().ToString("N"); r.name = boundary ? "自定义边界" : "自定义地块"; r.builtIn = false; r.boundary = boundary;
            if (!boundary) { r.visual = (TerrainVisual)Enum.Parse(typeof(TerrainVisual), "Plain"); r.onEnter = new List<RuleBranch>(); r.onLeave = new List<RuleBranch>(); }
            project.book.rules.Add(r); definitionId = r.id; definitionTab = true; Changed("已创建定义，可编辑条件、分支和操作。");
        }
        void DuplicateRule(RuleDefinition source)
        {
            Record("复制规则定义"); var copy = JsonUtility.FromJson<RuleDefinition>(JsonUtility.ToJson(source)); copy.id = Guid.NewGuid().ToString("N"); copy.name += " · 副本"; copy.builtIn = false;
            project.book.rules.Add(copy); definitionId = copy.id; Changed();
        }
        void DeleteRule(RuleDefinition rule)
        {
            bool used = project.levels.Any(l => l.cells.Any(c => c.ruleId == rule.id) || l.edges.Any(e => e.kind == EdgeKind.Gate && e.ruleId == rule.id));
            used |= project.book.rules.Any(r => r != rule && (r.onEnter ?? new List<RuleBranch>()).Concat(r.onLeave ?? new List<RuleBranch>()).Any(b => b != null && (b.actions ?? new List<RuleAction>()).Any(a => a != null && a.kind == ActionKind.ChangeTile && a.targetRuleId == rule.id)));
            if (used) { status = "定义仍被关卡或其他规则引用，请先替换引用。"; return; }
            Record("删除规则定义"); project.book.rules.Remove(rule); definitionId = project.book.rules.Count > 0 ? project.book.rules[0].id : "";
            if (brushRule == rule.id) { brush = Brush.Select; brushRule = ""; }
            Changed("未被引用的定义已删除，可撤销。");
        }
        CellData NewCell() => new CellData { id = Guid.NewGuid().ToString("N"), ruleId = project.book.rules.First(r => !r.boundary && r.visual.ToString() == "Plain").id, bindings = new List<BindingData>() };
        void NewLevel()
        {
            Record("新建地图"); var l = new LevelData { id = Guid.NewGuid().ToString("N"), name = "新关卡", width = 5, height = 5, player = -1, boxes = new List<int>(), cells = new List<CellData>(), edges = new List<EdgeData>() };
            for (int i = 0; i < 25; i++) l.cells.Add(NewCell());
            project.levels.Add(l); levelIndex = project.levels.Count - 1; project.selectedLevel = levelIndex; ResetSelection(); SyncSize(); Changed("新建 5×5 地图。请放置人物和目标，箱子数量不能少于目标。");
        }
        void DuplicateLevel()
        {
            Record("复制地图"); var l = JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(Level)); l.id = Guid.NewGuid().ToString("N"); l.name += " · 副本";
            project.levels.Add(l); levelIndex = project.levels.Count - 1; project.selectedLevel = levelIndex; ResetSelection(); SyncSize(); Changed();
        }
        void Resize()
        {
            int w = Mathf.Clamp(resizeW, 2, 32), h = Mathf.Clamp(resizeH, 2, 32); var l = Level;
            if (w == l.width && h == l.height) return;
            Record("调整地图大小"); int oldW = l.width, oldH = l.height;
            Func<int, int> map = index => index < 0 || index % oldW >= w || index / oldW >= h ? -1 : index / oldW * w + index % oldW;
            var cells = new List<CellData>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) cells.Add(x < oldW && y < oldH ? l.cells[y * oldW + x] : NewCell());
            l.player = map(l.player); l.boxes = l.boxes.Select(map).Where(i => i >= 0).ToList();
            var edges = new List<EdgeData>();
            foreach (var e in l.edges) { int c = map(e.cell); if (c < 0 || (e.direction == Direction.East && c % w == w - 1) || (e.direction == Direction.North && c / w == h - 1)) continue; e.cell = c; edges.Add(e); }
            l.cells = cells; l.edges = edges; l.width = w; l.height = h; selectedCell = -1; movingCell = -1; pickingKey = null; SyncSize(); Changed("地图大小已调整。裁掉的格子引用会在编译校验时定位。可撤销。"); Validate();
        }
        void Validate()
        {
            if (Level == null) return;
            issues = LevelValidator.Validate(Level, project.book); status = LevelValidator.HasErrors(issues) ? "编译发现错误，请修复下方标记。" : "结构校验通过。可继续搜索解法或试玩。"; Repaint();
        }
        void Search()
        {
            if (Level == null) return; Validate(); if (LevelValidator.HasErrors(issues)) return;
            CancelSearch(); solver = new SokobanSolver(Level, project.book, 100000, 5000); status = "正在搜索，超出预算只会标记“尚未验证”，不会误报无解。";
        }
        void DrawDiagnostics()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(135)))
            {
                if (solver != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("求解：" + SolveLabel(solver.Status) + "  · 已访问 " + solver.Visited + "  · " + solver.ElapsedMilliseconds.ToString("F0") + " ms", EditorStyles.miniBoldLabel);
                        if (solver.Status == SolveStatus.Searching && GUILayout.Button("取消", GUILayout.Width(46))) { solver.Cancel(); playAfterSearch = false; status = "搜索已取消，没有进入试玩。"; }
                    }
                    EditorGUILayout.LabelField(solver.Message, smallStyle);
                    if (solver.Status == SolveStatus.Solved)
                    {
                        string path = string.Concat(solver.Solution.Select(d => d == Direction.North ? "↑" : d == Direction.East ? "→" : d == Direction.South ? "↓" : "←"));
                        EditorGUILayout.SelectableLabel("解法 " + solver.Solution.Count + " 步：" + path, smallStyle, GUILayout.Height(28));
                    }
                }
                issuesScroll = EditorGUILayout.BeginScrollView(issuesScroll);
                if (issues.Count == 0 && solver == null) EditorGUILayout.LabelField("编译面板：检查人数、目标、配对、引用与规则结构。搜索使用同一套运行逻辑。", smallStyle);
                foreach (var issue in issues)
                {
                    if (GUILayout.Button((issue.severity == ValidationSeverity.Error ? "错误" : "提示") + (issue.cellIndex >= 0 ? " · " + PositionName(issue.cellIndex) : "") + "  " + issue.message, EditorStyles.wordWrappedMiniLabel))
                    { if (issue.cellIndex >= 0) { selectedCell = issue.cellIndex; selectEdge = false; definitionTab = false; } else if (!string.IsNullOrEmpty(issue.ruleId)) { definitionId = issue.ruleId; definitionTab = true; } }
                }
                EditorGUILayout.EndScrollView();
            }
        }
        static string SolveLabel(SolveStatus s)
        {
            switch (s) { case SolveStatus.Solved: return "已找到解法"; case SolveStatus.Unsolvable: return "已证明无解"; case SolveStatus.Unknown: return "尚未验证"; case SolveStatus.Cancelled: return "已取消"; case SolveStatus.Invalid: return "配置错误"; default: return "搜索中"; }
        }
        void Play()
        {
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
            if (playAfterSearch) { solver?.Cancel(); playAfterSearch = false; status = "编译搜索已取消，没有进入试玩。"; return; }
            if (Level == null) return; Validate(); if (LevelValidator.HasErrors(issues)) return;
            if (solver != null && solver.Status == SolveStatus.Unsolvable) { status = "当前地图已被完整搜索证明无解，请修改后试玩。"; return; }
            if (solver != null && (solver.Status == SolveStatus.Solved || solver.Status == SolveStatus.Unknown)) { LaunchPlay(); return; }
            if (solver == null || solver.Status != SolveStatus.Searching) Search();
            if (solver != null) { playAfterSearch = true; status = "编译结构通过，正在验证可解性；已知无解会阻止试玩。可随时取消。"; }
        }
        void LaunchPlay()
        {
            if (!project || Level == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            project.selectedLevel = levelIndex; EditorUtility.SetDirty(project); if (!SaveProject()) return; RefreshPreview();
            var game = FindObjectOfType<GameController>();
            if (!game) { status = "当前场景缺少 GameController，请打开 Assets/Scenes/BoxLab.unity 或初始化示例工程。"; return; }
            game.project = project; game.levelIndex = levelIndex; EditorUtility.SetDirty(game); EditorApplication.isPlaying = true;
        }
        [Serializable] sealed class Exchange { public int version = 1; public LevelData level; public RuleBookData book; }
        bool SaveProject()
        {
            if (!project) return false;
            try
            {
                AssetDatabase.SaveAssets();
                if (!EditorUtility.IsPersistent(project) || EditorUtility.IsDirty(project))
                { status = "保存未完成：请检查工程文件写入权限及 Console。当前编辑仍保留在内存中。"; return false; }
                status = "地图与规则库已保存。"; return true;
            }
            catch (Exception ex) { status = "保存失败：" + ex.Message + "。当前编辑仍保留，可重试。"; Debug.LogError("BoxLab " + status); return false; }
        }
        void Export()
        {
            if (Level == null) return; string path = EditorUtility.SaveFilePanel("导出地图及规则库", "", Level.name + ".json", "json"); if (string.IsNullOrEmpty(path)) return;
            try { System.IO.File.WriteAllText(path, JsonUtility.ToJson(new Exchange { level = Level, book = project.book }, true)); status = "已导出地图和完整规则库。"; }
            catch (Exception ex) { EditorUtility.DisplayDialog("导出失败", ex.Message, "确定"); }
        }
        void Import()
        {
            if (!project) return; string path = EditorUtility.OpenFilePanel("导入地图及规则库", "", "json"); if (string.IsNullOrEmpty(path)) return;
            try { ImportJson(System.IO.File.ReadAllText(path)); }
            catch (Exception ex) { EditorUtility.DisplayDialog("导入失败", ex.Message, "确定"); }
        }
        void ImportJson(string json)
        {
                var data = JsonUtility.FromJson<Exchange>(json);
                if (data == null || data.version != 1 || data.level == null || data.book == null || data.book.rules == null || data.level.cells == null || data.level.width < 2 || data.level.height < 2 || data.level.width > 32 || data.level.height > 32 || data.level.cells.Count != data.level.width * data.level.height) throw new Exception("不是受支持的 BoxLab 地图文件，或尺寸 / 格子数据不完整。");
                if (data.level.boxes == null) data.level.boxes = new List<int>();
                if (data.level.edges == null) data.level.edges = new List<EdgeData>();
                Action<List<BindingData>> checkBindings = bindings =>
                {
                    foreach (var binding in bindings)
                    {
                        if (binding == null) throw new Exception("地图文件包含空关联项，请修复后导入。");
                        if (binding.cellIds == null) binding.cellIds = new List<string>();
                    }
                };
                foreach (var cell in data.level.cells)
                {
                    if (cell == null) throw new Exception("地图文件包含空格子数据。");
                    if (cell.bindings == null) cell.bindings = new List<BindingData>();
                    checkBindings(cell.bindings);
                }
                foreach (var edge in data.level.edges)
                {
                    if (edge == null) throw new Exception("地图文件包含空边界数据。");
                    if (edge.bindings == null) edge.bindings = new List<BindingData>();
                    checkBindings(edge.bindings);
                }
                var incomingIssues = LevelValidator.Validate(data.level, data.book);
                if (LevelValidator.HasErrors(incomingIssues))
                    throw new Exception("导入前校验未通过，现有工程未更改：\n" + string.Join("\n", incomingIssues.Where(i => i.severity == ValidationSeverity.Error).Take(6).Select(i => i.message)));
                // Remap all imported definitions so existing levels never change when an ID collides.
                var mapping = new Dictionary<string, string>(); foreach (var r in data.book.rules) { if (r == null || string.IsNullOrEmpty(r.id) || string.IsNullOrWhiteSpace(r.name) || mapping.ContainsKey(r.id)) throw new Exception("规则名称 / 标识无效或重复。"); mapping[r.id] = Guid.NewGuid().ToString("N"); }
                foreach (var r in data.book.rules)
                {
                    r.id = mapping[r.id]; r.builtIn = false;
                    foreach (var b in (r.onEnter ?? new List<RuleBranch>()).Concat(r.onLeave ?? new List<RuleBranch>())) foreach (var a in b.actions ?? new List<RuleAction>()) if (!string.IsNullOrEmpty(a.targetRuleId) && mapping.ContainsKey(a.targetRuleId)) a.targetRuleId = mapping[a.targetRuleId];
                }
                foreach (var c in data.level.cells) if (mapping.ContainsKey(c.ruleId)) c.ruleId = mapping[c.ruleId];
                foreach (var e in data.level.edges ?? new List<EdgeData>()) if (!string.IsNullOrEmpty(e.ruleId) && mapping.ContainsKey(e.ruleId)) e.ruleId = mapping[e.ruleId];
                Record("导入关卡"); project.book.rules.AddRange(data.book.rules); data.level.id = Guid.NewGuid().ToString("N"); project.levels.Add(data.level); levelIndex = project.levels.Count - 1; project.selectedLevel = levelIndex; ResetSelection(); SyncSize(); Changed("导入为新关卡。原有关卡及定义不受影响。"); Validate();
        }
    }
}
#endif
