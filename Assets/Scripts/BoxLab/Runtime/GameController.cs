using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Input, presentation sequencing, and the edit/play loop. Rules live exclusively in RuleEngine.</summary>
    [DisallowMultipleComponent]
    public sealed class GameController : MonoBehaviour
    {
        public BoxProject project;
        public int levelIndex;
        public BoardView view;
        public bool externalUI;
        public BoardState State { get { return state; } }
        public bool IsBusy { get { return busy; } }
        public string Message { get { return string.IsNullOrEmpty(startupError) ? message : startupError; } }
        public event Action StateChanged;

        private LevelData level;
        private RuleBookData book;
        private BoardState state;
        private BoardState initialState;
        private readonly Stack<BoardState> history = new Stack<BoardState>();
        private readonly List<string> log = new List<string>();
        private bool busy;
        private string message = "";
        private string startupError = "";
        private Vector2 logScroll;
        private GUIStyle titleStyle, subtitleStyle, normalStyle, smallStyle, buttonStyle, badgeStyle, resultStyle;
        private Font interfaceFont;
        private Texture2D panelTexture, buttonTexture, hoverTexture;
        private int previousWidth, previousHeight;

        private void Start()
        {
            if (!view) view = GetComponent<BoardView>();
            if (!view) view = FindObjectOfType<BoardView>();
            if (!project && view) project = view.project;
            if (!externalUI) LoadLevel(levelIndex);
        }

        private void Update()
        {
            if (!externalUI && (previousWidth != Screen.width || previousHeight != Screen.height)) ApplyCameraLayout();
            if (!externalUI && Input.GetKeyDown(KeyCode.Escape)) { ReturnToEditor(); return; }
            // All commands are ignored during the complete animation sequence, never buffered.
            if (busy || state == null) return;
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace)) { Undo(); return; }
            if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
            if (state.status != GameStatus.Playing) return;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) TryMove(Direction.North);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) TryMove(Direction.East);
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) TryMove(Direction.South);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) TryMove(Direction.West);
        }

        public void LoadLevel(int index)
        {
            StopAllCoroutines();
            busy = false;
            state = null;
            startupError = "";
            history.Clear();
            log.Clear();
            if (!project || project.levels == null || project.levels.Count == 0 || project.book == null)
            {
                startupError = "未找到关卡数据。请在 BoxLab 编辑器中新建或打开项目。";
                return;
            }
            levelIndex = Mathf.Clamp(index, 0, project.levels.Count - 1);
            if (project.levels[levelIndex] == null)
            {
                startupError = "当前关卡数据为空。请返回编辑器修复。";
                return;
            }
            Load(project.levels[levelIndex], project.book);
        }

        public void Load(LevelData source, RuleBookData sourceBook, BoardState resume = null, List<BoardState> history = null)
        {
            StopAllCoroutines();
            busy = false;
            state = null;
            startupError = "";
            this.history.Clear();
            log.Clear();
            if (source == null || sourceBook == null)
            {
                startupError = "缺少关卡或规则数据。";
                StateChanged?.Invoke();
                return;
            }
            if (!view) view = GetComponent<BoardView>();
            if (!view) view = FindObjectOfType<BoardView>();
            if (view) view.HideEditGhost();
            // Authored objects remain untouched throughout gameplay.
            level = source.Clone();
            book = sourceBook.Clone();
            var issues = LevelValidator.Validate(level, book);
            foreach (var issue in issues)
            {
                if (issue.severity != ValidationSeverity.Error) continue;
                startupError += (startupError.Length == 0 ? "" : "\n") + issue.message;
            }
            if (startupError.Length > 0)
            {
                startupError = "关卡未通过编译校验：\n" + startupError;
                StateChanged?.Invoke();
                return;
            }
            initialState = RuleEngine.CreateState(level, book);
            state = resume == null ? initialState.Clone() : resume.Clone();
            if (history != null) foreach (var snapshot in history) if (snapshot != null) this.history.Push(snapshot.Clone());
            message = "用方向键或 WASD 移动，让每个目标都有箱子。";
            log.Add("已载入关卡副本，编辑内容不会被试玩修改。");
            if (view)
            {
                view.Render(level, book, state);
                if (!externalUI) ApplyCameraLayout();
            }
            StateChanged?.Invoke();
        }

        /// <summary>Oldest first, suitable for saving and passing back into Load.</summary>
        public List<BoardState> ExportHistory()
        {
            var snapshots = history.ToArray();
            Array.Reverse(snapshots);
            var result = new List<BoardState>(snapshots.Length);
            foreach (var snapshot in snapshots) result.Add(snapshot.Clone());
            return result;
        }

        public void TryMove(Direction direction)
        {
            if (busy || state == null || state.status != GameStatus.Playing) return;
            StepResult result;
            try { result = RuleEngine.Step(level, book, state, direction, true); }
            catch (Exception exception)
            {
                message = "规则执行异常，当前局面保持不变。请返回编辑器检查。";
                AddLog(message + " " + exception.Message);
                Debug.LogException(exception, this);
                StateChanged?.Invoke();
                return;
            }
            if (result == null) return;
            message = string.IsNullOrEmpty(result.message) ? "" : result.message;
            if (result.trace != null) foreach (string line in result.trace) AddLog(line);
            if (!result.changed)
            {
                if (message.Length == 0) message = "此方向无法移动。";
                if (result.interrupted) AddLog("异常连锁已中止，本次操作已完整撤回。");
                WorkshopAudio.Play(WorkshopSound.Blocked);
                StateChanged?.Invoke();
                return;
            }
            GameControllerAudio.MoveStarted(state, result.state);
            busy = true;
            StartCoroutine(PresentStep(result, state.Clone()));
        }

        private IEnumerator PresentStep(StepResult result, BoardState before)
        {
            BoardState previousFrame = before;
            bool firstFrame = true;
            if (result.frames != null)
                foreach (var frame in result.frames)
                    if (frame != null)
                    {
                        GameControllerAudio.FrameChanged(level, book, previousFrame, frame, firstFrame);
                        if (view) yield return view.AnimateState(level, book, frame);
                        previousFrame = frame; firstFrame = false;
                    }
            GameControllerAudio.FrameChanged(level, book, previousFrame, result.state, false);
            history.Push(before);
            state = result.state;
            if (view) view.Render(level, book, state);
            busy = false;
            if (state.status == GameStatus.Won)
            {
                message = "所有目标都已占满，关卡完成。";
                AddLog(message);
            }
            else if (state.status == GameStatus.Lost)
            {
                message = "箱子掉入空洞，剩余箱子不足。可以撤销或重开。";
                AddLog(message);
            }
            else if (string.IsNullOrEmpty(message)) message = "操作完成。";
            GameControllerAudio.Completed(before, state);
            StateChanged?.Invoke();
        }

        public void Undo()
        {
            if (busy || history.Count == 0) return;
            state = history.Pop();
            if (view) view.Render(level, book, state);
            message = "已撤销整次操作，包括连锁效果和地面变化。";
            AddLog(message);
            WorkshopAudio.Play(WorkshopSound.Undo);
            StateChanged?.Invoke();
        }

        public void Restart()
        {
            if (busy || initialState == null || state == null) return;
            if (SameState(state, initialState))
            {
                message = "已经是初始局面。";
                return;
            }
            history.Push(state.Clone());
            state = initialState.Clone();
            message = "关卡已重开，按 Z 可撤销这次重开。";
            AddLog(message);
            if (view) view.Render(level, book, state);
            WorkshopAudio.Play(WorkshopSound.Undo);
            StateChanged?.Invoke();
        }

        private static bool SameState(BoardState a, BoardState b)
        {
            // Solver keys intentionally omit counters and sort boxes; restart needs exact
            // presentation/history identity, including the statistics the player can undo.
            return a.player == b.player && a.moves == b.moves && a.pushes == b.pushes && a.status == b.status
                && SameSequence(a.boxes, b.boxes) && SameSequence(a.tiles, b.tiles) && SameSequence(a.rotations, b.rotations);
        }

        private static bool SameSequence<T>(IList<T> a, IList<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Count; i++) if (!comparer.Equals(a[i], b[i])) return false;
            return true;
        }

        public void ReturnToEditor()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string ExitLabel(bool shortcut)
        {
#if UNITY_EDITOR
            return shortcut ? "返回编辑器  [Esc]" : "返回编辑";
#else
            return shortcut ? "退出游戏  [Esc]" : "退出游戏";
#endif
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            log.Add(line);
            if (log.Count > 160) log.RemoveRange(0, log.Count - 160);
            logScroll.y = float.MaxValue;
        }

        private void ApplyCameraLayout()
        {
            previousWidth = Screen.width;
            previousHeight = Screen.height;
            if (!view) return;
            if (!view.boardCamera) view.FitCamera();
            if (view.boardCamera)
            {
                float side = SideWidth();
                float header = 92 * UIScale();
                float footer = 65 * UIScale();
                view.boardCamera.rect = new Rect(0, footer / Mathf.Max(1, Screen.height),
                    Mathf.Clamp01((Screen.width - side) / Mathf.Max(1, Screen.width)),
                    Mathf.Clamp01((Screen.height - header - footer) / Mathf.Max(1, Screen.height)));
                view.FitCamera();
            }
        }

        private float UIScale() { return Mathf.Clamp(Screen.height / 800f, .72f, 1.25f); }
        private float SideWidth() { return Mathf.Min(300 * UIScale(), Screen.width * .32f); }

        private void OnGUI()
        {
            if (externalUI) return;
            EnsureStyles();
            float s = UIScale();
            float side = SideWidth();
            float pad = 20 * s;
            float contentWidth = Screen.width - side;
            GUI.Box(new Rect(0, 0, Screen.width, 88 * s), GUIContent.none, PanelStyle());
            GUI.Label(new Rect(pad, 7 * s, 220 * s, 40 * s), "BOX / LAB", titleStyle);
            GUI.Label(new Rect(pad, 47 * s, Mathf.Max(180, contentWidth - pad), 32 * s), level == null ? "规则驱动的格子实验室" : level.name, subtitleStyle);
            GUI.Label(new Rect(contentWidth + pad, 22 * s, side - pad * 2, 34 * s), busy ? "正在处理连锁…" : "关卡试玩", badgeStyle);

            if (!string.IsNullOrEmpty(startupError))
            {
                GUI.Box(new Rect(pad, 115 * s, Screen.width - pad * 2, 220 * s), GUIContent.none, PanelStyle());
                GUI.Label(new Rect(pad * 2, 130 * s, Screen.width - pad * 4, 140 * s), startupError, normalStyle);
                if (GUI.Button(new Rect(pad * 2, 282 * s, 180 * s, 34 * s), ExitLabel(false), buttonStyle)) ReturnToEditor();
                return;
            }
            if (state == null) return;

            float x = contentWidth + 14 * s;
            float width = Mathf.Max(100, side - 28 * s);
            GUI.Box(new Rect(contentWidth, 90 * s, side, Screen.height - 90 * s), GUIContent.none, PanelStyle());
            int goals = 0, complete = 0;
            for (int i = 0; i < state.tiles.Length; i++)
            {
                var rule = book.Find(state.tiles[i]);
                if (rule == null || rule.visual != TerrainVisual.Goal) continue;
                goals++;
                if (state.BoxAt(i) >= 0) complete++;
            }
            GUI.Label(new Rect(x, 108 * s, width, 32 * s), "归位  " + complete + " / " + goals, subtitleStyle);
            GUI.Label(new Rect(x, 144 * s, width, 27 * s), "步数 " + state.moves + "    推动 " + state.pushes, normalStyle);
            GUI.Label(new Rect(x, 184 * s, width, 60 * s), string.IsNullOrEmpty(level.description) ? "方向键 / WASD：移动\nZ：撤销    R：重开" : level.description, smallStyle);

            bool previousEnabled = GUI.enabled;
            GUI.enabled = !busy;
            GUI.enabled = !busy && history.Count > 0;
            if (GUI.Button(new Rect(x, 252 * s, width, 34 * s), "撤销整次操作  [Z]", buttonStyle)) Undo();
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(x, 294 * s, width, 34 * s), "重新开始  [R]", buttonStyle)) Restart();
            if (GUI.Button(new Rect(x, 336 * s, width, 34 * s), ExitLabel(true), buttonStyle)) ReturnToEditor();
            if (project && project.levels.Count > 1)
            {
                float half = (width - 8 * s) / 2;
                if (GUI.Button(new Rect(x, 382 * s, half, 30 * s), "上一关", buttonStyle)) LoadLevel((levelIndex - 1 + project.levels.Count) % project.levels.Count);
                if (GUI.Button(new Rect(x + half + 8 * s, 382 * s, half, 30 * s), "下一关", buttonStyle)) LoadLevel((levelIndex + 1) % project.levels.Count);
            }
            GUI.enabled = previousEnabled;
            float logTop = 436 * s;
            GUI.Label(new Rect(x, logTop, width, 25 * s), "规则执行记录", subtitleStyle);
            var area = new Rect(x, logTop + 32 * s, width, Mathf.Max(20, Screen.height - logTop - 125 * s));
            float rowWidth = Mathf.Max(50, width - 22);
            float totalHeight = 0;
            var rowHeights = new List<float>();
            foreach (string item in log)
            {
                float height = Mathf.Max(24 * s, smallStyle.CalcHeight(new GUIContent(item), rowWidth) + 8 * s);
                rowHeights.Add(height); totalHeight += height;
            }
            logScroll = GUI.BeginScrollView(area, logScroll, new Rect(0, 0, rowWidth, totalHeight));
            float y = 0;
            for (int i = 0; i < log.Count; i++)
            {
                GUI.Label(new Rect(0, y, rowWidth, rowHeights[i]), log[i], smallStyle);
                y += rowHeights[i];
            }
            GUI.EndScrollView();

            GUI.Box(new Rect(0, Screen.height - 65 * s, contentWidth, 65 * s), GUIContent.none, PanelStyle());
            GUI.Label(new Rect(pad, Screen.height - 55 * s, contentWidth - pad * 2, 28 * s), message, normalStyle);
            GUI.Label(new Rect(pad, Screen.height - 29 * s, contentWidth - pad * 2, 22 * s), "方向键 / WASD 移动 · Z 撤销 · R 重开 · 连锁期间不接收新指令", smallStyle);

            // Mouse controls keep the demo playable on touchpads without affecting the rule engine.
            if (Screen.height > 500 && state.status == GameStatus.Playing)
            {
                float d = 33 * s;
                float cx = contentWidth - 68 * s, cy = Screen.height - 119 * s;
                GUI.enabled = !busy;
                if (GUI.Button(new Rect(cx, cy - d, d, d), "↑", buttonStyle)) TryMove(Direction.North);
                if (GUI.Button(new Rect(cx - d, cy, d, d), "←", buttonStyle)) TryMove(Direction.West);
                if (GUI.Button(new Rect(cx, cy, d, d), "↓", buttonStyle)) TryMove(Direction.South);
                if (GUI.Button(new Rect(cx + d, cy, d, d), "→", buttonStyle)) TryMove(Direction.East);
                GUI.enabled = previousEnabled;
            }
            if (!busy && state.status != GameStatus.Playing) DrawResult(contentWidth, s);
        }

        private void DrawResult(float contentWidth, float scale)
        {
            bool won = state.status == GameStatus.Won;
            float width = Mathf.Min(430 * scale, contentWidth - 32);
            var panel = new Rect((contentWidth - width) * .5f, Screen.height * .38f, width, 166 * scale);
            GUI.Box(panel, GUIContent.none, PanelStyle());
            resultStyle.normal.textColor = won ? new Color(.45f, 1, .70f) : new Color(1, .64f, .42f);
            GUI.Label(new Rect(panel.x + 20 * scale, panel.y + 18 * scale, width - 40 * scale, 38 * scale), won ? "关卡完成" : "箱子掉入空洞", resultStyle);
            GUI.Label(new Rect(panel.x + 20 * scale, panel.y + 61 * scale, width - 40 * scale, 37 * scale), won ? "全部目标已占据。可以继续下一关或重玩。" : "剩余箱子不足。撤销即可恢复掉落前的局面。", smallStyle);
            float buttonWidth = (width - 50 * scale) * .5f;
            bool hasNext = project && levelIndex + 1 < project.levels.Count;
            if (GUI.Button(new Rect(panel.x + 20 * scale, panel.y + 112 * scale, buttonWidth, 34 * scale), won ? (hasNext ? "下一关" : "重玩本关") : "撤销恢复", buttonStyle))
            { if (won && hasNext) LoadLevel(levelIndex + 1); else if (won) Restart(); else Undo(); }
            if (GUI.Button(new Rect(panel.x + 30 * scale + buttonWidth, panel.y + 112 * scale, buttonWidth, 34 * scale), ExitLabel(false), buttonStyle)) ReturnToEditor();
        }

        private GUIStyle PanelStyle() { return new GUIStyle(GUI.skin.box) { normal = { background = panelTexture }, border = new RectOffset(0, 0, 0, 0) }; }

        private void EnsureStyles()
        {
            float scale = UIScale();
            if (!interfaceFont) interfaceFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
            if (!panelTexture) panelTexture = Texture(new Color(.075f, .10f, .15f, .97f));
            if (!buttonTexture) buttonTexture = Texture(new Color(.15f, .20f, .28f));
            if (!hoverTexture) hoverTexture = Texture(new Color(.22f, .31f, .42f));
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { font = interfaceFont, fontStyle = FontStyle.Bold };
                subtitleStyle = new GUIStyle(GUI.skin.label) { font = interfaceFont, fontStyle = FontStyle.Bold };
                normalStyle = new GUIStyle(GUI.skin.label) { font = interfaceFont, wordWrap = true };
                smallStyle = new GUIStyle(normalStyle);
                badgeStyle = new GUIStyle(normalStyle) { alignment = TextAnchor.MiddleLeft };
                resultStyle = new GUIStyle(titleStyle);
                buttonStyle = new GUIStyle(GUI.skin.button) { font = interfaceFont, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2) };
                titleStyle.normal.textColor = new Color(.86f, .94f, 1);
                subtitleStyle.normal.textColor = new Color(.86f, .94f, 1);
                normalStyle.normal.textColor = new Color(.88f, .91f, .96f);
                smallStyle.normal.textColor = new Color(.63f, .71f, .82f);
                badgeStyle.normal.textColor = new Color(.35f, .85f, .89f);
                buttonStyle.normal.background = buttonTexture;
                buttonStyle.hover.background = hoverTexture;
                buttonStyle.active.background = hoverTexture;
                buttonStyle.normal.textColor = Color.white;
                buttonStyle.hover.textColor = Color.white;
                buttonStyle.active.textColor = Color.white;
            }
            titleStyle.fontSize = Mathf.RoundToInt(23 * scale);
            subtitleStyle.fontSize = Mathf.RoundToInt(16 * scale);
            normalStyle.fontSize = Mathf.RoundToInt(14 * scale);
            smallStyle.fontSize = Mathf.RoundToInt(12 * scale);
            badgeStyle.fontSize = Mathf.RoundToInt(14 * scale);
            buttonStyle.fontSize = Mathf.RoundToInt(13 * scale);
            resultStyle.fontSize = Mathf.RoundToInt(25 * scale);
        }

        private static Texture2D Texture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (interfaceFont) Destroy(interfaceFont);
            if (panelTexture) Destroy(panelTexture);
            if (buttonTexture) Destroy(buttonTexture);
            if (hoverTexture) Destroy(hoverTexture);
        }
    }
}
