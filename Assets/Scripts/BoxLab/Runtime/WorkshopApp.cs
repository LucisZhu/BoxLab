using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BoxLab
{
    public enum WorkshopPage { Home, BuiltIn, MyMaps, Edit, Play, Settings, Tutorials }

    /// <summary>Complete standalone navigation. Unity editor tooling is never needed by players.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed partial class WorkshopApp : MonoBehaviour
    {
        public WorkshopPage Page { get; private set; }
        public PlayerEditor Editor { get { return editor; } }
        public PlayerStorage Storage { get { return storage; } }
        public GameController Game { get { return game; } }
        private BoardView view;
        private GameController game;
        private PlayerStorage storage;
        private PlayerPreferences preferences;
        private RuleBookData book;
        private List<LevelData> builtIns;
        private List<LevelData> tutorials;
        private bool playingTutorial;
        private List<LevelData> PlayingCollection { get { return playingTutorial ? tutorials : builtIns; } }
        private List<MapDocument> ownMaps = new List<MapDocument>();
        private PlayerEditor editor;
        private MapDocument editingDocument;
        private LevelData playingLevel;
        private string playingMapId;
        private bool playingBuiltIn, testing, quitting;
        private int builtInIndex;
        private Action deferred;
        private string notice = "", fatalError = "", modalTitle, modalText;
        private string modalConfirm = "确定";
        private Action modalAction;
        private Vector2 listScroll, modalScroll, fileScroll;
        private Vector2 settingsScroll, helpScroll, playDescriptionScroll;
        private GUIStyle title, heading, topHeading, body, muted, button, smallButton, input, centered, codeInput;
        private string headerTooltip;
        private Texture2D flat, panel, buttonBackground, hoverBackground;
        private Font font;
        private readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        private bool help, newMapDialog, importDialog, shareDialog, importFileMode;
        private string shareCode = "", shareMapName = "", importCode = "", codeFeedback = "";
        private const int MaxCodeInputLength = PlayerStorage.MaxShareCodeChars;
        private string newName = "我的新关卡", newWidth = "5", newHeight = "5", importPath = "";
        private string[] browsingFiles = new string[0], browsingDirectories = new string[0];
        private string browsingPath = "";
        private SokobanSolver solver;
        private LevelData pendingLevel;
        private string pendingMapId;
        private bool pendingBuiltIn, pendingTest;
        private int previousWidth, previousHeight;
        private int consumedEscapeFrame = -1;
        private bool completionPendingSave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg == "-boxlabVerify") return;
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("BoxLab.LegacyVerification", false)) return;
#endif
            if (FindObjectOfType<WorkshopApp>()) return;
            var existing = FindObjectOfType<GameController>();
            if (!existing) return;
            existing.externalUI = true; existing.enabled = false;
            existing.gameObject.AddComponent<WorkshopApp>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            try
            {
                view = GetComponent<BoardView>(); game = GetComponent<GameController>();
                game.externalUI = true; game.enabled = false;
                string testRoot = null; string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-boxlabData") testRoot = args[i + 1];
                storage = new PlayerStorage(testRoot); preferences = storage.LoadPreferences();
                book = PlayerRules.CreateBook(); tutorials = PlayerRules.CreateDemoLevels(); builtIns = PlayerRules.CreateCampaignLevels();
                game.StateChanged += OnGameChanged;
                Application.wantsToQuit += WantsQuit;
                ApplyPreferences(); ShowHome();
            }
            catch (Exception ex) { fatalError = "无法启动本地工坊：" + ex.Message; Debug.LogException(ex); }
        }
        private void OnDestroy()
        {
            Application.wantsToQuit -= WantsQuit;
            if (game) game.StateChanged -= OnGameChanged;
            foreach (var texture in thumbnails.Values) if (texture) Destroy(texture);
            foreach (var texture in new[] { flat, panel, buttonBackground, hoverBackground }) if (texture) Destroy(texture);
            if (font) Destroy(font);
            if (editor != null) editor.Dispose();
        }
        private void Update()
        {
            if (!Application.isEditor && !Screen.fullScreen && (Screen.width < 960 || Screen.height < 600))
                Screen.SetResolution(Mathf.Max(960, Screen.width), Mathf.Max(600, Screen.height), false);
            if (deferred != null) { var action = deferred; deferred = null; Guard(action); }
            if (solver != null && solver.Status == SolveStatus.Searching)
            {
                solver.Step(70);
                if (solver.Status != SolveStatus.Searching) FinishValidation();
            }
            if (previousWidth != Screen.width || previousHeight != Screen.height) LayoutCamera();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (HasOverlay || Page != WorkshopPage.Edit) consumedEscapeFrame = Time.frameCount;
                if (validationIssues != null) validationIssues = null;
                else if (modalTitle != null) { modalTitle = null; modalAction = null; }
                else if (help || newMapDialog || importDialog || shareDialog) { help = newMapDialog = importDialog = shareDialog = false; }
                else if (solver != null) { solver.Cancel(); solver = null; }
                else if (Page == WorkshopPage.Play) { if (!game.IsBusy) LeavePlay(); }
                else if (Page != WorkshopPage.Edit && Page != WorkshopPage.Home) ShowHome();
            }
            if (game) game.enabled = Page == WorkshopPage.Play && !HasOverlay;
        }
        private bool HasOverlay { get { return validationIssues != null || modalTitle != null || help || newMapDialog || importDialog || shareDialog || solver != null; } }
        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { ShowMessage("操作未完成", ex.Message + "\n已有地图和编辑内容仍然保留。"); }
        }
        private void Queue(Action action) { deferred = action; }
        public void ShowHome()
        {
            Page = WorkshopPage.Home; game.enabled = false; listScroll = Vector2.zero;
            if (tutorials.Count > 0) view.Render(tutorials[Math.Min(2, tutorials.Count - 1)], book, RuleEngine.CreateState(tutorials[Math.Min(2, tutorials.Count - 1)], book));
            LayoutCamera();
        }
        public void ShowBuiltInCollection(bool beginner)
        {
            Page = beginner ? WorkshopPage.Tutorials : WorkshopPage.BuiltIn;
            game.enabled = false; listScroll = Vector2.zero; LayoutCamera();
        }
        public void ShowMyMaps()
        {
            ownMaps = storage.ListMaps(); Page = WorkshopPage.MyMaps; game.enabled = false; listScroll = Vector2.zero;
            notice = string.IsNullOrEmpty(storage.LastWarning) ? "地图保存在这台电脑。复制分享码发给朋友，对方粘贴即可导入。" : "部分文件无法读取，已保留原文件：\n" + storage.LastWarning;
            LayoutCamera();
        }
        public void OpenEditor(MapDocument document)
        {
            if (editor != null) editor.Dispose();
            editingDocument = document.Clone();
            editor = new PlayerEditor(view, editingDocument.level.Clone(), book);
            editor.DismissHelp();
            editor.SaveRequested = () => Queue(SaveDraft);
            editor.PlayRequested = () => Queue(() => ValidateAndPlay(editor.Level, editingDocument.id, false, true));
            editor.ExitRequested = () => Queue(RequestLeaveEditor);
            view.Render(editor.Level, book, RuleEngine.CreateState(editor.Level, book));
            Page = WorkshopPage.Edit; game.enabled = false; notice = ""; LayoutCamera();
            if (!preferences.editorHelpSeen) { OpenHelp(2); preferences.editorHelpSeen = true; storage.SavePreferences(preferences); }
        }
        public MapDocument SaveDraftNow()
        {
            if (editor == null || editingDocument == null) throw new InvalidOperationException("没有正在编辑的地图。");
            if (string.IsNullOrWhiteSpace(editor.Level.name)) editor.Operations.Rename("未命名关卡");
            var draft = editingDocument.Clone(); draft.level = editor.Level.Clone();
            if (string.IsNullOrWhiteSpace(draft.level.name)) draft.level.name = "未命名关卡";
            storage.SaveMap(draft); editingDocument = draft; editor.MarkSaved(); return draft.Clone();
        }
        private void SaveDraft() { SaveDraftNow(); notice = "已保存到本机 · " + DateTime.Now.ToString("HH:mm:ss"); }
        private void RequestLeaveEditor()
        {
            if (editor.Dirty || !storage.Exists(editingDocument.id))
            {
                modalTitle = "离开前保存地图？"; modalText = "未完成的地图也可以作为草稿保存。";
                modalConfirm = "保存并离开"; modalAction = () => { SaveDraftNow(); ShowMyMaps(); };
            }
            else ShowMyMaps();
        }
        public void ValidateAndPlay(LevelData level, string mapId, bool builtin, bool test)
        {
            pendingLevel = level.Clone(); pendingMapId = mapId; pendingBuiltIn = builtin; pendingTest = test;
            var issues = PlayerRules.Validate(level);
            if (issues.Count > 0) { validationIssues = issues; validationScroll = Vector2.zero; return; }
            BeginSolutionCheck();
        }
        private void BeginSolutionCheck()
        {
            validationIssues = null;
            solver = new SokobanSolver(pendingLevel, book, 60000, 3500);
            if (solver.Status != SolveStatus.Searching) FinishValidation();
        }
        private void FinishValidation()
        {
            var completed = solver; solver = null;
            if (completed.Status == SolveStatus.Solved) StartPending();
            else if (completed.Status == SolveStatus.Unknown)
            { Ask("尚未验证是否可解", "这张地图在本次搜索时间内未找到解，也不能断定无解。你可以继续手动试玩。", "继续试玩", StartPending); }
            else if (completed.Status != SolveStatus.Cancelled) ShowMessage("地图暂时无法通关", completed.Message + "\n返回编辑器调整后再试。草稿仍可保存。");
        }
        private void StartPending() { StartPlay(pendingLevel, pendingMapId, pendingBuiltIn, pendingTest); }
        public void StartPlay(LevelData level, string mapId, bool builtin, bool test, BoardState resume = null, List<BoardState> history = null)
        {
            if (editor != null) editor.ClearVisuals();
            playingLevel = level.Clone(); playingMapId = mapId; playingBuiltIn = builtin; testing = test;
            if (builtin)
            {
                playingTutorial = tutorials.Exists(item => item.id == level.id);
                builtInIndex = PlayingCollection.FindIndex(item => item.id == level.id);
            }
            Page = WorkshopPage.Play; game.enabled = true; notice = "";
            playDescriptionScroll = Vector2.zero;
            game.Load(playingLevel, book, resume, history); LayoutCamera();
            if (!test) SaveProgress();
        }
        private void OnGameChanged()
        {
            if (Page != WorkshopPage.Play || game.State == null) return;
            if (testing && editor != null && editor.GuideActive) editor.NotifyGuidePlayResult(playingLevel, game.State.status);
            try
            {
                if (playingBuiltIn && game.State.status == GameStatus.Won && !preferences.completed.Contains(playingLevel.id))
                { preferences.completed.Add(playingLevel.id); completionPendingSave = true; }
                if (!testing) SaveProgress();
            }
            catch (Exception ex) { notice = "保存进度失败：" + ex.Message; }
        }
        private void SaveProgress()
        {
            if (game.State == null || playingLevel == null || testing) return;
            var history = game.ExportHistory();
            // The on-disk undo history is bounded; current board is always kept in full.
            if (history.Count > 128) history.RemoveRange(0, history.Count - 128);
            storage.SaveProgress(new PlaySave { mapId = playingMapId, builtIn = playingBuiltIn, level = playingLevel.Clone(), mapHash = PlayerStorage.Fingerprint(playingLevel), state = game.State.Clone(), history = history });
            if (completionPendingSave) { storage.SavePreferences(preferences); completionPendingSave = false; }
        }
        private void ContinueGame()
        {
            var save = storage.LoadProgress();
            if (save == null) { ShowMessage("还没有游玩进度", "从教学关卡或“我的关卡”开始游玩后，完成的每次操作都会自动保存。"); return; }
            StartPlay(save.level, save.mapId, save.builtIn, false, save.state, save.history);
        }
        public void LeavePlay()
        {
            if (game.IsBusy) return;
            try { SaveProgress(); }
            catch (Exception ex) { ShowMessage("暂时无法保存进度", "未离开当前关卡，你可以继续尝试保存。\n" + ex.Message); return; }
            game.enabled = false;
            if (testing && editor != null)
            { Page = WorkshopPage.Edit; editor.Resume(); view.Render(editor.Level, book, RuleEngine.CreateState(editor.Level, book)); LayoutCamera(); }
            else if (playingBuiltIn) ShowBuiltInCollection(playingTutorial);
            else ShowMyMaps();
        }
        private void ApplyPreferences()
        {
            if (!Application.isEditor) Screen.fullScreenMode = preferences.fullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            view.animationSeconds = preferences.reducedMotion ? .03f : .10f;
            BoardAtmosphere.EffectsEnabled = !preferences.hideEffects;
            WorkshopAudio.Configure(preferences.audioVolume, preferences.audioMuted);
            if (view.boardCamera) view.boardCamera.backgroundColor = new Color(.21f, .235f, .25f);
        }
        private bool WantsQuit()
        {
            if (Application.isEditor) return true;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-boxlabWorkshopVerify") >= 0) return true;
            if (quitting) return true;
            if ((Page == WorkshopPage.Edit || (Page == WorkshopPage.Play && testing)) && editor != null && (editor.Dirty || !storage.Exists(editingDocument.id)))
            { Ask("保存地图后退出？", "当前草稿还没有保存。", "保存并退出", () => { SaveDraftNow(); QuitNow(); }); return false; }
            try { SaveProgress(); return true; }
            catch (Exception ex) { ShowMessage("退出前保存失败", "仍留在游戏中，请检查存档目录后重试。\n" + ex.Message); return false; }
        }
        private void QuitNow() { SaveProgress(); quitting = true; Application.Quit(); }
        private void Ask(string caption, string text, string confirm, Action action)
        { modalTitle = caption; modalText = text; modalConfirm = confirm; modalAction = action; modalScroll = Vector2.zero; }
        private void ShowMessage(string caption, string text) { Ask(caption, text, "知道了", null); }
        private void LayoutCamera()
        {
            previousWidth = Screen.width; previousHeight = Screen.height;
            if (!view) return;
            Rect rect;
            if (Page == WorkshopPage.Edit) rect = BoardRect;
            else if (Page == WorkshopPage.Play) rect = new Rect(18, 90, Mathf.Max(200, Screen.width - 344), Mathf.Max(160, Screen.height - 140));
            else rect = new Rect(HomePreviewRect.x, HomePreviewRect.y + 78, HomePreviewRect.width, HomePreviewRect.height - 78);
            view.SetViewport(new Rect(rect.x / Screen.width, 1 - rect.yMax / Screen.height, rect.width / Screen.width, rect.height / Screen.height));
        }
        private float ToolWidth { get { return Mathf.Clamp(Screen.width * .26f, 310, 410); } }
        private Rect HomePreviewRect { get { return new Rect(Screen.width * .43f, 135, Screen.width * .53f, Mathf.Max(240, Screen.height - 182)); } }
        private Rect BoardRect { get { return new Rect(10, 62, Mathf.Max(200, Screen.width - ToolWidth - 26), Mathf.Max(180, Screen.height - 80)); } }
        private Rect ToolRect { get { return new Rect(Screen.width - ToolWidth - 8, 62, ToolWidth, Mathf.Max(180, Screen.height - 80)); } }

        private void EnsureStyles()
        {
            if (body != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" }, 18);
            WorkshopTheme.Shared.Ensure(font);
            flat = Texture(WorkshopTheme.Backdrop); panel = Texture(new Color(.10f, .12f, .13f));
            buttonBackground = Texture(new Color(.15f, .22f, .28f)); hoverBackground = Texture(new Color(.17f, .40f, .43f));
            body = WorkshopTheme.Shared.CreateLabel(17);
            title = new GUIStyle(body) { fontSize = 44, fontStyle = FontStyle.Bold };
            heading = new GUIStyle(body) { fontSize = 24, fontStyle = FontStyle.Bold };
            topHeading = new GUIStyle(heading) { wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            muted = WorkshopTheme.Shared.CreateLabel(14, true);
            button = WorkshopTheme.Shared.CreateButton(18, WorkshopButtonKind.Primary);
            smallButton = new GUIStyle(button) { fontSize = 14 };
            input = WorkshopTheme.Shared.CreateInput(18);
            codeInput = new GUIStyle(input) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft, richText = false };
            centered = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
        }
        private static Texture2D Texture(Color color)
        { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }
        private bool Button(Rect rect, string text, Action action, bool enabled = true, bool compact = false, int fontSize = 0, bool highlighted = false)
        {
            bool click = WorkshopTheme.Shared.Button(rect, text, enabled,
                highlighted ? WorkshopButtonKind.Attention : (text == "删除" || text == "不保存" ? WorkshopButtonKind.Danger : (compact ? WorkshopButtonKind.Secondary : WorkshopButtonKind.Primary)), fontSize > 0 ? fontSize : (compact ? 14 : 18));
            if (click) Queue(action); return click;
        }
        private void OnGUI()
        {
            EnsureStyles(); GUI.depth = -20;
            headerTooltip = null;
            if (consumedEscapeFrame == Time.frameCount && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) Event.current.Use();
            GUI.skin.font = font;
            WorkshopTheme.Shared.ApplyToSkin(GUI.skin);
            if (!string.IsNullOrEmpty(fatalError)) { GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), flat); GUI.Label(new Rect(40, 50, Screen.width - 80, 220), fatalError, heading); return; }
            bool overlay = HasOverlay; GUI.enabled = !overlay;
            if (Page != WorkshopPage.Edit && Page != WorkshopPage.Play && Page != WorkshopPage.Home) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), flat);
            if (Page == WorkshopPage.Home) DrawHome();
            else if (Page == WorkshopPage.BuiltIn || Page == WorkshopPage.Tutorials || Page == WorkshopPage.MyMaps) DrawGallery();
            else if (Page == WorkshopPage.Edit)
            {
                DrawTop("地图工坊", "先摆放，再连线，随时试玩");
                editor.Draw(BoardRect, ToolRect);
            }
            else if (Page == WorkshopPage.Play) DrawPlay();
            else DrawSettings();
            GUI.enabled = true;
            if (overlay) WorkshopTheme.Shared.DrawScrim(new Rect(0, 0, Screen.width, Screen.height));
            if (validationIssues != null) DrawValidationIssues();
            else if (solver != null) DrawSolver();
            else if (modalTitle != null) DrawModal();
            else if (newMapDialog) DrawNewMap();
            else if (shareDialog) DrawShare();
            else if (importDialog) DrawImport();
            else if (help) DrawHelp();
            if (!overlay && headerTooltip != null && new Rect(22, 8, 240, 40).Contains(Event.current.mousePosition))
            {
                float width = Mathf.Min(620, Screen.width - 44), height = body.CalcHeight(new GUIContent(headerTooltip), width - 24) + 20;
                var tipRect = new Rect(22, 57, width, height);
                WorkshopTheme.Shared.DrawPanel(tipRect);
                GUI.Label(new Rect(tipRect.x + 12, tipRect.y + 10, width - 24, height - 20), headerTooltip, body);
            }
        }
        private Texture2D TintTexture() { return panel; }
        private void DrawTop(string caption, string detail)
        {
            WorkshopTheme.Shared.DrawBackdrop(new Rect(0, 0, Screen.width, 55));
            WorkshopTheme.Shared.DrawDivider(new Rect(18, 54, Screen.width - 36, 1));
            string visible = caption ?? "";
            if (topHeading.CalcSize(new GUIContent(visible)).x > 240)
            {
                headerTooltip = visible;
                while (visible.Length > 0 && topHeading.CalcSize(new GUIContent(visible + "…")).x > 240)
                {
                    int removed = char.IsLowSurrogate(visible[visible.Length - 1]) && visible.Length > 1 ? 2 : 1;
                    visible = visible.Substring(0, visible.Length - removed);
                }
                visible += "…";
            }
            GUI.Label(new Rect(22, 8, 240, 40), visible, topHeading);
            GUI.Label(new Rect(270, 27, Screen.width - 410, 22), detail, muted);
            Button(new Rect(Screen.width - 96, 10, 76, 34), "? 帮助", () => OpenHelp(Page == WorkshopPage.Edit ? 2 : 0), true, true);
        }
        private void DrawHome()
        {
            Rect preview = HomePreviewRect;
            // Draw the surround without painting over the camera's board image.
            WorkshopTheme.Shared.DrawBackdrop(new Rect(Screen.width * .42f, 0, Screen.width * .58f, preview.y));
            WorkshopTheme.Shared.DrawBackdrop(new Rect(Screen.width * .42f, preview.y, preview.x - Screen.width * .42f, preview.height));
            WorkshopTheme.Shared.DrawBackdrop(new Rect(preview.xMax, preview.y, Screen.width - preview.xMax, preview.height));
            WorkshopTheme.Shared.DrawBackdrop(new Rect(Screen.width * .42f, preview.yMax, Screen.width * .58f, Screen.height - preview.yMax));
            Color savedColor = GUI.color;
            GUI.color = view.boardCamera.backgroundColor;
            GUI.DrawTexture(new Rect(preview.x, preview.y, preview.width, 78), Texture2D.whiteTexture);
            GUI.color = savedColor;
            WorkshopTheme.Shared.DrawBackdrop(new Rect(0, 0, Screen.width * .42f, Screen.height));
            WorkshopTheme.Shared.DrawDivider(new Rect(Screen.width * .42f - 1, 24, 1, Screen.height - 48));
            float x = Screen.width * .05f, width = Mathf.Min(340, Screen.width * .32f);
            GUI.Label(new Rect(x, 35, width + 20, 62), "BOX LAB", title);
            GUI.Label(new Rect(x, 104, width, 35), "推箱工坊", heading);
            GUI.Label(new Rect(x, 151, width, 58), "推一推，造一造，再分享给朋友。", body);
            float y = Mathf.Clamp(Screen.height * .30f, 218, 260), h = Mathf.Min(46, (Screen.height - y - 116) / 5), pitch = h + 8;
            Button(new Rect(x, y, width, h), "新手教程", () => ShowBuiltInCollection(true));
            Button(new Rect(x, y + pitch, width, h), "正式关卡", () => ShowBuiltInCollection(false));
            Button(new Rect(x, y + pitch * 2, width, h), "继续上次进度", ContinueGame);
            Button(new Rect(x, y + pitch * 3, width, h), "我的关卡", ShowMyMaps);
            Button(new Rect(x, y + pitch * 4, width, h), "+ 创作新关卡", () => { newMapDialog = true; newName = "我的新关卡"; });
            float utilityWidth = (width - 16) / 3, utilityY = y + pitch * 5 + 4;
            Button(new Rect(x, utilityY, utilityWidth, 44), "设置", () => { Page = WorkshopPage.Settings; }, true, true, 18);
            Button(new Rect(x + utilityWidth + 8, utilityY, utilityWidth, 44), "帮助", () => OpenHelp(), true, true, 18);
            Button(new Rect(x + 2 * (utilityWidth + 8), utilityY, utilityWidth, 44), "退出", () => { if (WantsQuit()) QuitNow(); }, true, true, 18);
            float guideWidth = Mathf.Min(350, preview.width - 34);
            if (WorkshopTheme.Shared.Button(new Rect(preview.center.x - guideWidth / 2, preview.y + 12, guideWidth, 54),
                "第一次创作？跟着做一关", true, WorkshopButtonKind.Primary, 22)) Queue(StartEditorGuide);
            GUI.Label(new Rect(x, Screen.height - 36, width + 20, 24), "KayKit / Kenney · CC0", muted);
        }
        private void DrawGallery()
        {
            bool own = Page == WorkshopPage.MyMaps;
            bool beginner = Page == WorkshopPage.Tutorials;
            var collection = beginner ? tutorials : builtIns;
            DrawTop(own ? "我的关卡" : (beginner ? "新手教程" : "正式关卡"), own ? "草稿也能保存，复制分享码即可分享" : (beginner ? "认识基本单元" : ""));
            Button(new Rect(22, 69, 110, 36), "← 主菜单", ShowHome, true, true);
            if (own)
            {
                Button(new Rect(144, 69, 142, 36), "+ 新建关卡", () => newMapDialog = true, true, true);
                Button(new Rect(298, 69, 140, 36), "导入分享码", () => { importDialog = true; importFileMode = false; importCode = ""; codeFeedback = ""; }, true, true);
                Button(new Rect(450, 69, 160, 36), "打开地图文件夹", () => Application.OpenURL(new Uri(storage.MapsPath).AbsoluteUri), true, true);
            }
            else
            {
                Button(new Rect(144, 69, 148, 36), beginner ? "去正式关卡 →" : "新手教程", () => ShowBuiltInCollection(!beginner), true, true);
                Button(new Rect(304, 69, 168, 36), "学习编辑地图", StartEditorGuide, true, true);
            }
            int count = own ? ownMaps.Count : collection.Count;
            int columns = Screen.width >= 1500 ? 3 : (Screen.width >= 1100 ? 2 : 1);
            float cardWidth = (Screen.width - 58 - (columns - 1) * 18) / columns;
            float descriptionHeight = 78;
            if (!own) foreach (var entry in collection)
                descriptionHeight = Mathf.Max(descriptionHeight, body.CalcHeight(new GUIContent(entry.description ?? ""), cardWidth - 182));
            float height = 235 + descriptionHeight - 78;
            listScroll = GUI.BeginScrollView(new Rect(20, 122, Screen.width - 40, Screen.height - 185), listScroll, new Rect(0, 0, Screen.width - 62, Mathf.Max(200, ((count + columns - 1) / columns) * (height + 18))));
            for (int i = 0; i < count; i++)
            {
                var map = own ? ownMaps[i].level : collection[i];
                var card = new Rect((i % columns) * (cardWidth + 18), (i / columns) * (height + 18), cardWidth, height);
                WorkshopTheme.Shared.DrawPanel(card, WorkshopPanelKind.Card);
                GUI.DrawTexture(new Rect(card.x + 14, card.y + 18, 134, 134), Thumbnail(map), ScaleMode.ScaleToFit);
                GUI.Label(new Rect(card.x + 165, card.y + 18, card.width - 180, 34), ShortLabel(map.name, Mathf.Max(8, (int)((card.width - 180) / 24))), heading);
                bool draft = LevelValidator.HasErrors(PlayerRules.Validate(map));
                string state = draft ? "草稿 · 尚未完成" : (preferences.completed.Contains(map.id) ? "已完成" : "可游玩");
                GUI.Label(new Rect(card.x + 165, card.y + 59, card.width - 180, 28), map.width + " × " + map.height + "    " + state, muted);
                GUI.Label(new Rect(card.x + 165, card.y + 94, card.width - 182, descriptionHeight), map.description ?? "", body);
                if (own)
                {
                    var document = ownMaps[i]; float row = card.y + height - 52; float unit = (card.width - 36) / 5;
                    Button(new Rect(card.x + 10, row, unit, 36), "游玩", () => ValidateAndPlay(document.level, document.id, false, false), !draft, true);
                    Button(new Rect(card.x + 14 + unit, row, unit, 36), "编辑", () => OpenEditor(document), true, true);
                    Button(new Rect(card.x + 18 + unit * 2, row, unit, 36), "复制", () => { var copy = document.Clone(); copy.id = Guid.NewGuid().ToString("N"); copy.level.id = Guid.NewGuid().ToString("N"); copy.level.name = ClipName(copy.level.name + " · 副本"); storage.SaveMap(copy); ShowMyMaps(); }, true, true);
                    Button(new Rect(card.x + 22 + unit * 3, row, unit, 36), "分享", () => { shareCode = storage.ExportShareCode(document); shareMapName = document.level.name; codeFeedback = ""; shareDialog = true; }, true, true);
                    Button(new Rect(card.x + 26 + unit * 4, row, unit, 36), "删除", () => Ask("删除这张地图？", document.level.name + "\n地图将移至本机 Deleted 备份目录。", "删除", () => { storage.DeleteMap(document.id); ShowMyMaps(); }), true, true);
                }
                else
                {
                    Button(new Rect(card.x + 16, card.y + height - 55, card.width * .48f - 16, 40), "开始", () => StartPlay(map, map.id, true, false));
                    Button(new Rect(card.x + card.width * .5f, card.y + height - 55, card.width * .48f - 10, 40), "复制到工坊", () => { var copy = map.Clone(); copy.id = Guid.NewGuid().ToString("N"); copy.name += " · 我的版本"; OpenEditor(new MapDocument { level = copy }); }, true, true);
                }
            }
            if (count == 0) GUI.Label(new Rect(45, 80, Screen.width - 160, 110), "这里还没有地图。\n点“新建关卡”开始创作，或粘贴朋友发来的分享码。", centered);
            GUI.EndScrollView();
            GUI.Label(new Rect(24, Screen.height - 51, Screen.width - 48, 44), own ? notice : "每关都可以随时撤销、重开，或复制到工坊改造。", muted);
        }
        private static string ClipName(string name) { return name.Length > 80 ? name.Substring(0, 80) : name; }
        private static string ShortLabel(string value, int max) { return value != null && value.Length > max ? value.Substring(0, max - 1) + "…" : value; }
        private Texture2D Thumbnail(LevelData map)
        {
            string key = PlayerStorage.Fingerprint(map); Texture2D result;
            if (thumbnails.TryGetValue(key, out result)) return result;
            int size = 128; result = new Texture2D(size, size, TextureFormat.RGB24, false); result.filterMode = FilterMode.Point;
            var pixels = new Color[size * size]; for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.035f, .055f, .075f);
            float step = 120f / Mathf.Max(map.width, map.height);
            float ox = (size - map.width * step) / 2f, oy = (size - map.height * step) / 2f;
            for (int c = 0; c < map.cells.Count; c++)
            {
                Color color; var rule = book.Find(map.cells[c].ruleId); if (rule == null || !ColorUtility.TryParseHtmlString(rule.colorHex, out color)) color = Color.gray;
                int x = Mathf.RoundToInt(ox + (c % map.width) * step), y = Mathf.RoundToInt(oy + (c / map.width) * step);
                FillPixels(pixels, size, x, y, Mathf.Max(1, (int)step - 1), Mathf.Max(1, (int)step - 1), color);
                if (c == map.player || map.boxes.Contains(c)) FillPixels(pixels, size, x + (int)(step * .25f), y + (int)(step * .25f), Mathf.Max(1, (int)(step * .5f)), Mathf.Max(1, (int)(step * .5f)), c == map.player ? Color.cyan : new Color(1, .72f, .27f));
            }
            foreach (var edge in map.edges)
            {
                if (edge.kind == EdgeKind.Open) continue;
                int x = Mathf.RoundToInt(ox + (edge.cell % map.width) * step), y = Mathf.RoundToInt(oy + (edge.cell / map.width) * step);
                Color color = edge.kind == EdgeKind.Gate ? new Color(.9f, .35f, .42f) : new Color(.93f, .96f, .99f);
                if (edge.direction == Direction.East) FillPixels(pixels, size, x + (int)step - 2, y, 2, (int)step, color);
                else if (edge.direction == Direction.North) FillPixels(pixels, size, x, y + (int)step - 2, (int)step, 2, color);
                else if (edge.direction == Direction.West) FillPixels(pixels, size, x, y, 2, (int)step, color);
                else FillPixels(pixels, size, x, y, (int)step, 2, color);
            }
            result.SetPixels(pixels); result.Apply(); thumbnails.Add(key, result); return result;
        }
        private static void FillPixels(Color[] pixels, int size, int x, int y, int w, int h, Color color)
        { for (int row = Mathf.Max(0, y); row < Mathf.Min(size, y + h); row++) for (int col = Mathf.Max(0, x); col < Mathf.Min(size, x + w); col++) pixels[row * size + col] = color; }
        private void DrawPlay()
        {
            DrawTop(testing ? "试玩 · " + playingLevel.name : playingLevel.name, testing ? "使用地图副本，返回后继续编辑" : "每次完成操作后自动保存进度");
            float x = Screen.width - 306; WorkshopTheme.Shared.DrawPanel(new Rect(x - 16, 66, 300, Screen.height - 86));
            GUI.Label(new Rect(x, 90, 268, 38), "让每个目标都有箱子", heading);
            GUI.Label(new Rect(x, 143, 268, 50), "移动  WASD / 方向键\n撤销  Z     重开  R（可撤销）", body);
            var state = game.State;
            bool won = state != null && state.status == GameStatus.Won;
            bool hasNext = won && playingBuiltIn && builtInIndex >= 0 && builtInIndex + 1 < PlayingCollection.Count;
            float bottom = Screen.height - 196;
            if (state != null)
            {
                GUI.Label(new Rect(x, 213, 268, 36), "步数  " + state.moves + "     推动  " + state.pushes, heading);
                WorkshopTheme.Shared.DrawDivider(new Rect(x, 259, 266, 1));
                string description = state.status == GameStatus.Won ? "关卡完成！\n所有目标都已占满。" : (state.status == GameStatus.Lost ? "箱子不够了。\n撤销一步，就能继续尝试。" : playingLevel.description ?? "");
                float contentHeight = body.CalcHeight(new GUIContent(description), 242) + 12;
                playDescriptionScroll = GUI.BeginScrollView(new Rect(x, 275, 268, Mathf.Max(40, bottom - 291)), playDescriptionScroll, new Rect(0, 0, 244, contentHeight));
                GUI.Label(new Rect(0, 0, 242, contentHeight), description, body); GUI.EndScrollView();
            }
            Button(new Rect(x, bottom, 126, 42), "撤销  Z", game.Undo, !game.IsBusy);
            Button(new Rect(x + 140, bottom, 126, 42), "重开  R", game.Restart, !game.IsBusy);
            if (hasNext)
                Button(new Rect(x, bottom + 55, 266, 42), "下一关 →", () => { var level = PlayingCollection[builtInIndex + 1]; StartPlay(level, level.id, true, false); }, !game.IsBusy, highlighted: true);
            else if (won && playingBuiltIn && playingTutorial)
                Button(new Rect(x, bottom + 55, 266, 42), "学会了，去创作 →", StartEditorGuide, !game.IsBusy, highlighted: true);
            else if (!testing) Button(new Rect(x, bottom + 55, 266, 42), "保存当前进度", () => { SaveProgress(); notice = "进度已保存。"; }, !game.IsBusy);
            Button(new Rect(x, bottom + 110, 266, 42), testing ? "返回编辑器  Esc" : "返回关卡列表  Esc", LeavePlay, !game.IsBusy,
                highlighted: won && !hasNext && !(playingBuiltIn && playingTutorial));
            GUI.Label(new Rect(24, Screen.height - 40, Screen.width - 355, 30), game.IsBusy ? "机关处理中……" : (string.IsNullOrEmpty(notice) ? game.Message : notice), muted);
        }
        private void DrawSettings()
        {
            DrawTop("设置", "显示、声音和本地数据"); float x = Mathf.Max(40, (Screen.width - 700) / 2);
            Button(new Rect(x, 89, 130, 40), "← 主菜单", ShowHome);
            settingsScroll = GUI.BeginScrollView(new Rect(x - 12, 148, 724, Screen.height - 170), settingsScroll, new Rect(0, 0, 696, 758));
            WorkshopTheme.Shared.DrawPanel(new Rect(0, 0, 690, 748));
            Button(new Rect(24, 22, 640, 46), "显示模式：" + (preferences.fullScreen ? "全屏" : "窗口"), () => { preferences.fullScreen = !preferences.fullScreen; ApplyPreferences(); storage.SavePreferences(preferences); });
            Button(new Rect(24, 81, 640, 46), "游戏速度：" + (preferences.reducedMotion ? "快速" : "标准"), () => { preferences.reducedMotion = !preferences.reducedMotion; ApplyPreferences(); storage.SavePreferences(preferences); });
            Button(new Rect(24, 140, 640, 46), "氛围特效：" + (preferences.hideEffects ? "关闭" : "开启"), () => { preferences.hideEffects = !preferences.hideEffects; ApplyPreferences(); storage.SavePreferences(preferences); });
            Button(new Rect(24, 199, 640, 46), "音效：" + (preferences.audioMuted ? "静音" : "开启"), () => { preferences.audioMuted = !preferences.audioMuted; ApplyPreferences(); storage.SavePreferences(preferences); });
            Button(new Rect(24, 258, 148, 42), "− 10%", () => ChangeAudioVolume(-.1f), preferences.audioVolume > 0, true);
            GUI.Label(new Rect(188, 264, 300, 34), "音效音量  " + Mathf.RoundToInt(preferences.audioVolume * 100) + "%", centered);
            Button(new Rect(516, 258, 148, 42), "+ 10%", () => ChangeAudioVolume(.1f), preferences.audioVolume < 1, true);
            Button(new Rect(24, 317, 640, 46), "试听音效", () => WorkshopAudio.Play(WorkshopSound.Win), !preferences.audioMuted && preferences.audioVolume > 0);
            Button(new Rect(24, 376, 640, 46), "打开存档目录", () => Application.OpenURL(new Uri(storage.Root).AbsoluteUri));
            GUI.Label(new Rect(24, 448, 638, 154), "地图与游玩进度保存在本机，互不覆盖。\n分享地图请使用“我的关卡 → 分享”，不需要注册或联网。\n窗口最小 960 × 600；长列表可滚动浏览。\n场景与人物：KayKit；冰雾与 Adventure 界面：Kenney · CC0。\n提示音为本项目原创合成音效，无背景音乐。", body);
            GUI.Label(new Rect(24, 625, 638, 100), storage.Root, muted);
            GUI.EndScrollView();
        }
        private void ChangeAudioVolume(float amount)
        {
            preferences.audioVolume = Mathf.Clamp01(Mathf.Round((preferences.audioVolume + amount) * 100) / 100f);
            ApplyPreferences(); storage.SavePreferences(preferences);
            WorkshopAudio.Play(WorkshopSound.UI);
        }
        private Rect DialogRect(float width = 680, float height = 420)
        { width = Mathf.Min(width, Screen.width - 36); height = Mathf.Min(height, Screen.height - 36); return new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height); }
        private void DrawModal()
        {
            Rect r = DialogRect(730, 480); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 26, r.y + 24, r.width - 52, 48), modalTitle, heading);
            float contentHeight = Mathf.Max(240, body.CalcHeight(new GUIContent(modalText), r.width - 78));
            modalScroll = GUI.BeginScrollView(new Rect(r.x + 26, r.y + 88, r.width - 52, r.height - 170), modalScroll, new Rect(0, 0, r.width - 76, contentHeight));
            GUI.Label(new Rect(0, 0, r.width - 80, contentHeight), modalText, body); GUI.EndScrollView();
            var action = modalAction;
            Button(new Rect(r.x + 26, r.yMax - 62, 200, 40), modalConfirm, () => { modalTitle = null; modalAction = null; if (action != null) action(); });
            if (action != null) Button(new Rect(r.x + 244, r.yMax - 62, 120, 40), "取消", () => { modalTitle = null; modalAction = null; });
            if (modalTitle == "离开前保存地图？" || modalTitle == "保存地图后退出？")
            {
                bool quit = modalTitle == "保存地图后退出？";
                Button(new Rect(r.xMax - 196, r.yMax - 62, 170, 40), "不保存", () => { modalTitle = null; modalAction = null; if (quit) QuitNow(); else ShowMyMaps(); });
            }
        }
        private void DrawNewMap()
        {
            Rect r = DialogRect(590, 400); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 28, r.y + 24, 400, 44), "从一张空白棋盘开始", heading);
            GUI.Label(new Rect(r.x + 28, r.y + 92, 90, 36), "名称", body);
            newName = GUI.TextField(new Rect(r.x + 124, r.y + 88, r.width - 156, 38), newName, 80, input);
            GUI.Label(new Rect(r.x + 28, r.y + 160, 80, 38), "宽 × 高", body);
            newWidth = GUI.TextField(new Rect(r.x + 124, r.y + 153, 78, 38), newWidth, 2, input);
            GUI.Label(new Rect(r.x + 219, r.y + 161, 30, 35), "×", body);
            newHeight = GUI.TextField(new Rect(r.x + 253, r.y + 153, 78, 38), newHeight, 2, input);
            GUI.Label(new Rect(r.x + 28, r.y + 223, r.width - 56, 62), "每边 2～32 格；之后也能调整。\n可以随时保存退出", muted);
            Button(new Rect(r.x + 28, r.yMax - 70, 190, 43), "进入工坊", () => { int w, h; if (!int.TryParse(newWidth, out w) || !int.TryParse(newHeight, out h) || w < 2 || w > 32 || h < 2 || h > 32) throw new InvalidDataException("宽和高请输入 2～32 的整数。"); var level = LevelData.Create(w, h, string.IsNullOrWhiteSpace(newName) ? "未命名关卡" : newName.Trim()); level.player = -1; newMapDialog = false; OpenEditor(new MapDocument { level = level }); });
            Button(new Rect(r.x + 239, r.yMax - 70, 130, 43), "取消", () => newMapDialog = false);
        }
        private void Browse(string path)
        {
            browsingPath = Path.GetFullPath(path); importPath = browsingPath;
            browsingDirectories = Directory.GetDirectories(browsingPath);
            browsingFiles = Directory.GetFiles(browsingPath, "*.boxmap"); Array.Sort(browsingDirectories); Array.Sort(browsingFiles); fileScroll = Vector2.zero;
        }
        private void DrawShare()
        {
            Rect r = DialogRect(820, 520); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 24, r.y + 22, r.width - 48, 42), "分享地图 · " + ShortLabel(shareMapName, 22), heading);
            GUI.Label(new Rect(r.x + 24, r.y + 77, r.width - 48, 58), "把这段分享码发给朋友，对方在“我的关卡 → 导入分享码”中粘贴，即可原样打开。无需联网。", body);
            bool enabled = GUI.enabled; GUI.enabled = false;
            GUI.TextArea(new Rect(r.x + 24, r.y + 148, r.width - 48, r.height - 280), shareCode.Length > 12000 ? shareCode.Substring(0, 12000) + "\n……" : shareCode, codeInput);
            GUI.enabled = enabled;
            GUI.Label(new Rect(r.x + 24, r.yMax - 117, r.width - 48, 45), string.IsNullOrEmpty(codeFeedback) ? "共 " + shareCode.Length.ToString("N0") + " 个字符 · 复制按钮会复制完整分享码" : codeFeedback, muted);
            Button(new Rect(r.x + 24, r.yMax - 65, 245, 42), "复制分享码到剪贴板", () => { GUIUtility.systemCopyBuffer = shareCode; codeFeedback = "已复制！可以直接粘贴到聊天中发送。"; });
            Button(new Rect(r.x + 286, r.yMax - 65, 130, 42), "关闭", () => shareDialog = false);
        }
        private void DrawImport()
        {
            if (importFileMode) { DrawFileImport(); return; }
            Rect r = DialogRect(820, 550); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 24, r.y + 22, r.width - 48, 42), "导入朋友的分享码", heading);
            GUI.Label(new Rect(r.x + 24, r.y + 78, r.width - 48, 55), "粘贴完整分享码，地图的布局、朝向和机关连线都会保留。导入后会成为你的独立副本。", body);
            GUI.SetNextControlName("BoxLab.ShareCode");
            importCode = GUI.TextArea(new Rect(r.x + 24, r.y + 148, r.width - 48, r.height - 320), importCode, MaxCodeInputLength, codeInput);
            Button(new Rect(r.x + 24, r.yMax - 158, 180, 34), "从剪贴板粘贴", () =>
            {
                string pasted = GUIUtility.systemCopyBuffer ?? "";
                if (pasted.Length > MaxCodeInputLength) { codeFeedback = "剪贴板内容过长，请只复制完整的地图分享码。"; return; }
                importCode = pasted; codeFeedback = string.IsNullOrWhiteSpace(pasted) ? "剪贴板为空，请先复制朋友发来的分享码。" : "已粘贴，点击下方按钮导入。";
            }, true, true);
            Button(new Rect(r.x + 215, r.yMax - 158, 100, 34), "清空", () => { importCode = ""; codeFeedback = ""; }, true, true);
            GUI.Label(new Rect(r.x + 24, r.yMax - 111, r.width - 48, 38), codeFeedback, muted);
            Button(new Rect(r.x + 24, r.yMax - 62, 210, 42), "导入为我的地图", () =>
            {
                try { var document = storage.ImportShareCode(importCode); importDialog = false; ShowMyMaps(); notice = "已导入：" + document.level.name; }
                catch (Exception ex) { codeFeedback = ex.Message; }
            }, !string.IsNullOrWhiteSpace(importCode));
            Button(new Rect(r.x + 250, r.yMax - 62, 110, 42), "取消", () => importDialog = false);
            Button(new Rect(r.xMax - 194, r.yMax - 62, 170, 42), "导入旧地图文件", () => { Browse(storage.SharePath); importFileMode = true; }, true, true);
        }
        private void DrawFileImport()
        {
            Rect r = DialogRect(820, 580); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 24, r.y + 21, r.width - 48, 40), "打开朋友的地图", heading);
            importPath = GUI.TextField(new Rect(r.x + 24, r.y + 74, r.width - 150, 36), importPath, input);
            Button(new Rect(r.xMax - 112, r.y + 74, 88, 36), "转到", () => { if (Directory.Exists(importPath)) Browse(importPath); else importPath = Path.GetFullPath(importPath); }, true, true);
            Button(new Rect(r.x + 24, r.y + 124, 120, 32), "上级目录", () => { var parent = Directory.GetParent(browsingPath); if (parent != null) Browse(parent.FullName); }, true, true);
            Button(new Rect(r.x + 155, r.y + 124, 120, 32), "桌面", () => Browse(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), true, true);
            Button(new Rect(r.x + 286, r.y + 124, 120, 32), "文档", () => Browse(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), true, true);
            Button(new Rect(r.x + 417, r.y + 124, 150, 32), "导出文件夹", () => Browse(storage.SharePath), true, true);
            float rows = browsingDirectories.Length + browsingFiles.Length;
            fileScroll = GUI.BeginScrollView(new Rect(r.x + 24, r.y + 170, r.width - 48, r.height - 260), fileScroll, new Rect(0, 0, r.width - 74, Mathf.Max(80, rows * 38)));
            int index = 0;
            foreach (string path in browsingDirectories) { string local = path; Button(new Rect(0, index++ * 38, r.width - 80, 34), "[文件夹] " + Path.GetFileName(path), () => Browse(local), true, true); }
            foreach (string path in browsingFiles) { string local = path; Button(new Rect(0, index++ * 38, r.width - 80, 34), Path.GetFileName(path), () => importPath = local, true, true); }
            if (index == 0) GUI.Label(new Rect(8, 16, r.width - 110, 70), "此目录没有 .boxmap 文件。也可直接粘贴完整文件路径。", muted);
            GUI.EndScrollView();
            Button(new Rect(r.x + 24, r.yMax - 66, 210, 42), "导入为我的地图", () => { var document = storage.Import(importPath); importDialog = false; ShowMyMaps(); notice = "已导入：" + document.level.name; });
            Button(new Rect(r.x + 250, r.yMax - 66, 130, 42), "取消", () => importDialog = false);
        }
        private void DrawSolver()
        {
            Rect r = DialogRect(590, 290); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 28, r.y + 24, 520, 45), "检查地图能否完成", heading);
            GUI.Label(new Rect(r.x + 28, r.y + 96, 510, 88), "正在按实际机关规则搜索……\n已检查 " + solver.Visited + " 个局面。", body);
            Button(new Rect(r.x + 28, r.yMax - 70, 190, 42), "取消，继续编辑", () => { solver.Cancel(); solver = null; });
        }
        public void StartEditorGuide()
        {
            OpenEditor(new MapDocument { level = PlayerEditor.CreateGuideLevel() });
            help = false; editor.DismissHelp(); editor.BeginGuide();
        }
    }
}
