using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Isolated V2 acceptance checks, usable in the editor and an actual built player.</summary>
    public sealed class WorkshopVerification : MonoBehaviour
    {
        [Serializable] sealed class PhaseRecord { public string mapId, stateJson; public int historyCount; }
        readonly List<string> results = new List<string>();
        WorkshopApp app;
        string evidence, phase, runtimeError;
        bool finished;
        double started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PrepareIsolatedPreferences()
        {
            if (!Requested()) return;
            Application.runInBackground = true;
            string root = Argument("-boxlabData");
            if (string.IsNullOrEmpty(root)) return; // Start reports a clear error before any authoring tests.
            var store = new PlayerStorage(root);
            var preferences = store.LoadPreferences(); preferences.editorHelpSeen = true; preferences.reducedMotion = true; preferences.hideEffects = false;
            store.SavePreferences(preferences);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested()) return;
            var host = new GameObject("Workshop acceptance verification"); DontDestroyOnLoad(host); host.AddComponent<WorkshopVerification>();
        }
        static bool Requested() { return Array.IndexOf(Environment.GetCommandLineArgs(), "-boxlabWorkshopVerify") >= 0; }
        static string Argument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return null;
        }
        void Awake() { Application.logMessageReceived += RuntimeLog; }
        void OnDestroy() { Application.logMessageReceived -= RuntimeLog; }
        void RuntimeLog(string text, string stack, LogType type)
        { if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) runtimeError = text + "\n" + stack; }
        void Update()
        {
            if (finished || started <= 0) return;
            if (!string.IsNullOrEmpty(runtimeError)) Finish(false, runtimeError);
            else if (Time.realtimeSinceStartupAsDouble - started > 240) Finish(false, "Runtime workshop verification exceeded 240 seconds.");
        }
        IEnumerator Start()
        {
            started = Time.realtimeSinceStartupAsDouble;
            evidence = Path.GetFullPath(Argument("-boxlabEvidence") ?? Path.Combine(Environment.CurrentDirectory, "BoxLabEvidence"));
            phase = Argument("-boxlabWorkshopPhase") ?? "full";
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0 && !finished)
            {
                object wait = null; bool yielded = false; Exception error = null;
                try
                {
                    for (int i = 0; i < 100 && stack.Count > 0; i++)
                    {
                        var top = stack.Peek();
                        if (!top.MoveNext()) { stack.Pop(); continue; }
                        var nested = top.Current as IEnumerator;
                        if (nested != null) { stack.Push(nested); continue; }
                        wait = top.Current; yielded = true; break;
                    }
                }
                catch (Exception caught) { error = caught; }
                if (error != null) { Finish(false, error.ToString()); yield break; }
                if (yielded) yield return wait; else if (stack.Count > 0) yield return null;
            }
            if (!finished) Finish(true, "");
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory(evidence);
            Require(!string.IsNullOrEmpty(Argument("-boxlabData")), "Verification requires an explicit isolated -boxlabData directory.");
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Use a graphics device; -nographics cannot verify the actual player.");
            double deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (!app || app.Storage == null || app.Game == null)
            {
                app = FindObjectOfType<WorkshopApp>();
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("WorkshopApp did not boot.");
                yield return null;
            }
            Require(Path.GetFullPath(app.Storage.Root).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(Argument("-boxlabData")).TrimEnd(Path.DirectorySeparatorChar), "Runtime tests must use their isolated save directory.");
            if (phase == "read") { yield return ResumeAcrossProcess(); yield return Capture("workshop-cross-process-resume"); yield break; }
            if (phase == "write") { yield return SaveAcrossProcess(); yield return Capture("workshop-cross-process-saved"); yield break; }
            // A real window size change restores the hidden test player's GUI surface for captures.
            Screen.SetResolution(960, 600, false);
            for (int i = 0; i < 8; i++) yield return null;
            results.AddRange(VerifyStorage(Path.Combine(app.Storage.Root, "AcceptanceStorage-" + Guid.NewGuid().ToString("N"))));
            results.AddRange(VerifyEditing());
            results.AddRange(PlayerEditingVerification.RunAll());
            results.AddRange(ChallengeLevelsVerification.RunAll());
            results.AddRange(CampaignLevelsVerification.RunAll());
            app.ShowHome();
            results.AddRange(RestartVerification.RunAll(app.Game, Path.Combine(app.Storage.Root, "AcceptanceRestart-" + Guid.NewGuid().ToString("N"))));
            yield return AudioVerification.RunAll(results, app.Game);
            VerifyAudioPreferences();
            app.ShowHome(); yield return Capture("workshop-home");

            var draftLevel = LevelData.Create(5, 5, "验收 · 从空白到分享"); draftLevel.player = -1;
            var draft = new MapDocument { level = draftLevel };
            app.OpenEditor(draft); app.Editor.DismissHelp();
            Require(app.Page == WorkshopPage.Edit && app.Editor.Level.player == -1, "New blank draft did not open.");
            var emptySaved = app.SaveDraftNow();
            Require(app.Storage.Exists(emptySaved.id) && !app.Editor.Dirty, "An unfinished draft must save successfully.");
            var operations = app.Editor.Operations; string reason;
            Require(operations.Place(PlayerEditing.Player, PlayerEditTarget.Ground(6), 0, out reason), reason);
            Require(operations.Place(PlayerEditing.Box, PlayerEditTarget.Ground(7), 0, out reason), reason);
            Require(operations.Place(PlayerRules.Goal, PlayerEditTarget.Ground(8), 0, out reason), reason);
            Require(app.Editor.Dirty, "Real editor operations must mark the draft dirty.");
            var authored = app.SaveDraftNow();
            Require(!app.Editor.Dirty, "Saving must clear the authoring dirty flag.");
            string original = PlayerStorage.Fingerprint(authored.level);
            app.ShowMyMaps();
            var reopenedStorage = new PlayerStorage(app.Storage.Root); var reopened = reopenedStorage.LoadMap(authored.id);
            Require(PlayerStorage.Fingerprint(reopened.level) == original, "A fresh store instance did not load the saved map exactly.");
            var share = reopenedStorage.ExportShareCode(reopened); var imported = reopenedStorage.ImportShareCode(share);
            Require(imported.id != reopened.id && imported.level.id != reopened.level.id && reopenedStorage.ListMaps().Count >= 2, "Import must create an independent map rather than overwrite the original.");
            app.OpenEditor(imported); app.Editor.DismissHelp(); yield return Capture("workshop-editor");
            string draftBeforeTest = JsonUtility.ToJson(app.Editor.Level);
            app.ValidateAndPlay(app.Editor.Level, imported.id, false, true);
            yield return WaitForPlay();
            yield return Command(Direction.East);
            Require(app.Game.State.status == GameStatus.Won, "The runtime-authored and imported map did not win.");
            yield return Capture("workshop-test-win-return");
            app.LeavePlay();
            Require(app.Page == WorkshopPage.Edit && JsonUtility.ToJson(app.Editor.Level) == draftBeforeTest && !app.Editor.Dirty, "Returning from test changed the authored draft.");
            Require(PlayerStorage.Fingerprint(reopenedStorage.LoadMap(authored.id).level) == original, "Testing/import altered the original saved map.");
            Pass("Real app flow: new draft → author with PlayerEditing → save → fresh store load → share/export → import copy → validate → animated win → unchanged editor draft.");

            yield return SaveAcrossProcess();
            yield return ResumeAcrossProcess();
            yield return VerifySaveFailureRecovery();
            yield return VerifyCompletionRetry();
            Pass("Same-process fresh-store resume tested. Separate write/read command modes are available for a true process restart.");
            var levels = PlayerRules.CreateDemoLevels(); var book = PlayerRules.CreateBook();
            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i]; var solver = new SokobanSolver(level, book, 100000, 20000);
                while (solver.Status == SolveStatus.Searching) { solver.Step(400); yield return null; }
                Require(solver.Status == SolveStatus.Solved, level.name + ": " + solver.Message);
                app.StartPlay(level, level.id, true, false);
                if (level.id == "boxlab-player-lesson-07") yield return Capture("workshop-directional-portals-before");
                if (level.id == "boxlab-player-lesson-box-only") yield return Capture("workshop-box-passage-before");
                foreach (var direction in solver.Solution) yield return Command(direction);
                if (level.id == "boxlab-player-lesson-07") yield return Capture("workshop-directional-portals-after");
                if (level.id == "boxlab-player-lesson-box-only") yield return Capture("workshop-box-passage-after");
                Require(app.Game.State.status == GameStatus.Won, level.name + " did not finish in the real controller.");
                if (i == 0) yield return Capture("workshop-first-win-next");
                Pass("Animated V2 lesson: " + level.name + ", " + solver.Solution.Count + " inputs, " + solver.Visited + " states.");
            }
            Require(BoardAtmosphere.VerificationPassed, "Ice mist and goal sparks were not both observed and cleared by the effects switch.");
            Pass("Atmosphere: real ice mist and goal particles emitted; disabling effects cleared both systems.");
            yield return Capture("workshop-final-win");
            foreach (var level in PlayerRules.CreateCampaignLevels())
            {
                app.StartPlay(level, level.id, true, false);
                yield return Capture(level.id + "-start");
                var solution = CampaignLevels.GetVerifiedSolution(level.id);
                foreach (var direction in solution) yield return Command(direction);
                Require(app.Game.State.status == GameStatus.Won, "Formal campaign did not complete in the real controller: " + level.name);
                Pass("Animated formal campaign: " + level.name + ", " + solution.Count + " inputs, " + app.Game.State.pushes + " pushes.");
                yield return Capture(level.id + "-complete");
                app.LeavePlay();
                Require(app.Page == WorkshopPage.BuiltIn, "Formal campaign must return to its own collection.");
                app.ShowHome();
                typeof(WorkshopApp).GetMethod("ContinueGame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(app, null);
                Require(app.Page == WorkshopPage.Play && app.Game.State.status == GameStatus.Won, "Continue must restore the saved formal level.");
                app.LeavePlay(); Require(app.Page == WorkshopPage.BuiltIn, "Resumed formal level returned to the tutorial collection.");
            }
            app.StartPlay(levels[0], levels[0].id, true, false); app.LeavePlay();
            Require(app.Page == WorkshopPage.Tutorials, "Existing lesson ids must return to the beginner collection.");
            app.ShowHome();
            typeof(WorkshopApp).GetMethod("ContinueGame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(app, null);
            app.LeavePlay(); Require(app.Page == WorkshopPage.Tutorials, "Existing saved lesson must resume in the beginner collection.");
            var oldPortalLesson = levels.Find(item => item.id == "boxlab-player-lesson-07");
            Require(oldPortalLesson != null && oldPortalLesson.name.StartsWith("08"), "The former portal lesson must retain its stable id after renumbering.");
            app.StartPlay(oldPortalLesson, oldPortalLesson.id, true, false); app.LeavePlay(); app.ShowHome();
            typeof(WorkshopApp).GetMethod("ContinueGame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(app, null);
            var resumedLesson = (LevelData)typeof(WorkshopApp).GetField("playingLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(app);
            Require(resumedLesson.id == oldPortalLesson.id, "Existing portal progress resumed as the newly inserted box passage lesson.");
            app.LeavePlay(); Require(app.Page == WorkshopPage.Tutorials, "Renumbered tutorial returned to the wrong collection.");
            Pass("Independent tutorial/campaign collections: live completion, resume by stable id and return navigation all retained the correct collection.");
            yield return CapturePolishPreview();
            yield return CaptureReadabilityPreview();
            yield return CaptureProductShell();
            app.ShowHome();
        }

        void VerifyAudioPreferences()
        {
            var store = new PlayerStorage(Path.Combine(app.Storage.Root, "AudioPreferences-" + Guid.NewGuid().ToString("N")));
            PlayerStorage.AtomicWrite(Path.Combine(store.Root, "settings.json"), "{\"editorHelpSeen\":true,\"completed\":[]}");
            var settings = store.LoadPreferences();
            Require(Mathf.Approximately(settings.audioVolume, .65f) && !settings.audioMuted, "Old settings must gain an audible default, not zero volume.");
            settings.audioVolume = .3f; settings.audioMuted = true; store.SavePreferences(settings);
            settings = new PlayerStorage(store.Root).LoadPreferences();
            Require(Mathf.Approximately(settings.audioVolume, .3f) && settings.audioMuted && settings.editorHelpSeen, "Audio settings did not survive a fresh storage instance.");
            Pass("Audio preferences: old settings retain prior fields and gain defaults; volume/mute persist across reload.");
        }

        IEnumerator CaptureProductShell()
        {
            Require(Resources.Load<Texture2D>("BoxLabUI/Adventure/panel_brown_dark_corners_a") != null, "Adventure button artwork missing from built player.");
            Screen.SetResolution(960, 600, false);
            for (int i = 0; i < 8; i++) yield return null;
            yield return VerifyGuidedCreation();
            app.ShowHome(); yield return Capture("product-home-960");
            yield return CaptureHelpPages("960");
            var newMapDialogField = typeof(WorkshopApp).GetField("newMapDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            newMapDialogField.SetValue(app, true); yield return Capture("product-new-map-960"); newMapDialogField.SetValue(app, false);
            typeof(WorkshopApp).GetProperty("Page").SetValue(app, WorkshopPage.Settings, null);
            yield return Capture("product-settings-960");
            var level = PlayerRules.CreateDemoLevels()[0];
            app.StartPlay(level, level.id, true, false); yield return Capture("product-play-960");
            string goalDetail;
            Require(app.GetComponent<BoardView>().ValidateGoalMarkerMaterials(out goalDetail), goalDetail);
            Pass(goalDetail);
            var goalView = app.GetComponent<BoardView>();
            goalView.ShowEditGhost(PlayerRules.Goal, level.Index(3, 3), Direction.North, false, 0, true);
            yield return Capture("product-goal-ghost-valid-960");
            goalView.ShowEditGhost(PlayerRules.Goal, level.Index(3, 3), Direction.North, false, 0, false);
            yield return Capture("product-goal-ghost-invalid-960");
            goalView.HideEditGhost();
            app.OpenHelp(); yield return null;
            Require(!app.Game.enabled, "The help overlay must pause gameplay input.");
            app.CloseHelp(); yield return null;
            Require(app.Game.enabled, "Closing help must restore gameplay input.");
            var longTitle = level.Clone(); longTitle.name = "工坊入门 · 我的第一道机关 · 长标题展示";
            app.StartPlay(longTitle, longTitle.id, false, true); yield return Capture("product-long-play-title-960");
            var blank = LevelData.Create(8, 8, "一个比较长的地图名称也应该保留完整可读的标题提示"); blank.player = -1;
            blank.cells[blank.Index(6, 6)].ruleId = PlayerRules.PortalEntrance;
            app.OpenEditor(new MapDocument { level = blank }); app.Editor.DismissHelp();
            app.ValidateAndPlay(blank, "validation-preview", false, true);
            yield return Capture("product-validation-960");
            var diagnostics = typeof(WorkshopApp).GetField("validationIssues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(diagnostics.GetValue(app) is List<ValidationIssue>, "Invalid map should present located diagnostics.");
            diagnostics.SetValue(app, null);
            app.Editor.FocusValidationIssue(new ValidationIssue { cellIndex = blank.Index(6, 6), message = "传送入口需要配对出口。" });
            yield return Capture("product-editor-focus-960");
            app.StartEditorGuide(); yield return Capture("product-guide-start-960");
            app.OpenHelp(app.HelpPageCount - 1); yield return Capture("product-help-editor-960"); app.CloseHelp();
            Screen.SetResolution(2504, 850, false);
            for (int i = 0; i < 8; i++) yield return null;
            app.ShowHome(); yield return Capture("product-home-wide");
            app.ShowBuiltInCollection(true); yield return Capture("product-tutorial-gallery-wide");
            app.ShowBuiltInCollection(false); yield return Capture("product-campaign-gallery-wide");
            app.StartEditorGuide(); yield return Capture("product-guide-start-wide");
            Screen.SetResolution(1280, 800, false);
            for (int i = 0; i < 8; i++) yield return null;
            app.ShowHome(); yield return CaptureHelpPages("1280");
            app.StartPlay(PlayerRules.CreateDemoLevels()[5], "permissions-preview", true, false);
            string portraitDetail;
            Require(app.GetComponent<BoardView>().ValidatePermissionPortraitMaterials(out portraitDetail), portraitDetail);
            Pass(portraitDetail);
            yield return Capture("product-person-passage-1280");
            var boxPassage = PlayerRules.CreateDemoLevels().Find(item => item.id == "boxlab-player-lesson-box-only");
            app.StartPlay(boxPassage, "box-permissions-preview", true, false);
            Require(app.GetComponent<BoardView>().ValidatePermissionPortraitMaterials(out portraitDetail), portraitDetail);
            Pass(portraitDetail);
            yield return Capture("product-box-passage-1280");
            app.OpenEditor(new MapDocument { level = boxPassage.Clone() }); app.Editor.DismissHelp();
            yield return Capture("product-passage-palette-1280");
            app.StartPlay(PlayerRules.CreateDemoLevels().Find(item => item.id == "boxlab-player-lesson-07"), "portal-preview", true, false);
            yield return new WaitForSecondsRealtime(.8f);
            yield return Capture("product-portal-glow-1280");
            Pass("Product shell rendered at 960×600: home/settings/play, located diagnostics and focused editor; real Adventure art loaded.");
        }

        IEnumerator CaptureHelpPages(string viewport)
        {
            Require(app.Page == WorkshopPage.Home, "General help screenshots must use the home-page guide.");
            app.OpenHelp();
            Require(app.HelpPageCount == 2, "General help must retain two concise illustrated pages.");
            for (int page = 0; page < app.HelpPageCount; page++)
            {
                app.OpenHelp(page);
                Require(app.HelpVisible && app.HelpPage == page, "Help page navigation failed.");
                yield return Capture("product-help-" + (page + 1) + "-" + viewport);
            }
            app.CloseHelp();
            Require(!app.HelpVisible, "Help did not close.");
            Pass("Illustrated help: both pages rendered at " + viewport + ", page state and close behavior verified.");
        }

        IEnumerator VerifySaveFailureRecovery()
        {
            var level = PlayerRules.CreateDemoLevels()[0];
            app.StartPlay(level, level.id, true, false);
            var path = Path.Combine(app.Storage.Root, "continue.json");
            string before = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                app.LeavePlay();
                Require(app.Page == WorkshopPage.Play, "A failed progress write must leave the current play session open.");
                var modal = typeof(WorkshopApp).GetField("modalTitle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Require((string)modal.GetValue(app) == "暂时无法保存进度", "Failed leave must explain save failure.");
                modal.SetValue(app, null);
            }
            Require(File.ReadAllText(path) == before, "Failed write changed the prior saved progress.");
            app.LeavePlay();
            Require(app.Page != WorkshopPage.Play, "Leave should succeed after the save destination becomes available.");
            Pass("Locked progress file: leave blocked with feedback; prior save preserved; retry succeeds after unlock.");
            yield return null;
        }

        IEnumerator VerifyCompletionRetry()
        {
            var level = PlayerRules.CreateDemoLevels()[0]; level.id = "completion-retry-" + Guid.NewGuid().ToString("N");
            app.StartPlay(level, level.id, true, false);
            using (var locked = new FileStream(Path.Combine(app.Storage.Root, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                yield return Command(Direction.North);
                Require(app.Game.State.status == GameStatus.Won, "Completion retry fixture did not win.");
                Require((bool)typeof(WorkshopApp).GetField("completionPendingSave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(app), "Failed completion save was not retained for retry.");
            }
            app.LeavePlay();
            Require(new PlayerStorage(app.Storage.Root).LoadPreferences().completed.Contains(level.id), "Completion retry did not persist the earned completion mark.");
            Pass("Locked settings file on win: completion kept pending and successfully persisted on retry.");
        }

        IEnumerator VerifyGuidedCreation()
        {
            app.StartEditorGuide(); var editor = app.Editor; var level = editor.Level; var operations = editor.Operations; string reason;
            Require(editor.GuideActive && editor.GuideStep == 0 && !editor.GuideComplete, "Guided creation did not begin on an empty draft.");
            Require(operations.Place(PlayerEditing.Player, PlayerEditTarget.Ground(level.Index(1, 2)), 0, out reason), reason);
            Require(operations.Place(PlayerEditing.Box, PlayerEditTarget.Ground(level.Index(2, 2)), 0, out reason), reason);
            yield return Capture("product-guide-step3-ground-tab");
            Require(operations.Place(PlayerRules.Goal, PlayerEditTarget.Ground(level.Index(3, 2)), 0, out reason), reason);
            Require(operations.Place(PlayerRules.Plate, PlayerEditTarget.Ground(level.Index(1, 2)), 0, out reason), reason);
            yield return Capture("product-guide-step5-wall-tab");
            Require(operations.Place(PlayerRules.Gate, PlayerEditTarget.Boundary(level.Index(2, 2), Direction.East), 0, out reason), reason);
            yield return Capture("product-guide-step6-link-mode");
            Require(operations.Link(level.Index(1, 2), PlayerEditTarget.Boundary(level.Index(2, 2), Direction.East), out reason), reason);
            yield return null;
            Require(editor.GuideStep == 6 && !editor.GuideComplete, "Guide must teach the editable map name after wiring.");
            operations.Rename("我的第一道机关 · 自定义名字");
            yield return null;
            Require(editor.GuideStep == 7, "Custom name should advance to the description lesson.");
            operations.SetDescription("先压住压力板，再把箱子推进金圈。");
            yield return null;
            Require(editor.GuideStep == 8 && !editor.GuideComplete, "Guide must require actual play after naming and describing the map.");
            yield return Capture("product-guide-ready");
            string before = PlayerStorage.Fingerprint(level);
            app.ValidateAndPlay(level, "guide-test", false, true); yield return WaitForPlay(); yield return Command(Direction.East);
            Require(app.Game.State.status == GameStatus.Won && editor.GuideComplete, "Guided map must win through its linked gate and mark the real play result.");
            app.LeavePlay();
            Require(app.Page == WorkshopPage.Edit && PlayerStorage.Fingerprint(editor.Level) == before, "Guide test altered the authoring draft.");
            yield return Capture("product-guide-complete");
            Pass("Guided creation: real placement/wiring operations → validate → gate push win → untouched authoring draft; completion requires actual win.");
        }

        IEnumerator CapturePolishPreview()
        {
            // A useful visual fixture also exercises the player's extra pressure-plate box design.
            var level = LevelData.Create(7, 6, "美术预览 · 冰雾与方木箱"); level.player = -1;
            var edit = new PlayerEditing(level, PlayerRules.CreateBook()); string reason;
            Require(edit.Place(PlayerEditing.Player, PlayerEditTarget.Ground(level.Index(1, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Goal, PlayerEditTarget.Ground(level.Index(4, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerEditing.Box, PlayerEditTarget.Ground(level.Index(2, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Plate, PlayerEditTarget.Ground(level.Index(1, 3)), 0, out reason), reason);
            Require(edit.Place(PlayerEditing.Box, PlayerEditTarget.Ground(level.Index(1, 3)), 0, out reason), reason);
            for (int x = 1; x <= 5; x++)
                Require(edit.Place(PlayerRules.Ice, PlayerEditTarget.Ground(level.Index(x, 4)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.RotationPlate, PlayerEditTarget.Ground(level.Index(4, 3)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Arrow, PlayerEditTarget.Ground(level.Index(5, 3)), 1, out reason), reason);
            Require(edit.Link(level.Index(4, 3), PlayerEditTarget.Ground(level.Index(5, 3)), out reason), reason);
            var gate = PlayerEditTarget.Boundary(level.Index(3, 2), Direction.East);
            Require(edit.Place(PlayerRules.Gate, gate, 0, out reason), reason);
            Require(edit.Link(level.Index(1, 3), gate, out reason), reason);
            Require(edit.Place(PlayerEditing.Wall, PlayerEditTarget.Boundary(level.Index(3, 3), Direction.East), 0, out reason), reason);
            Require(!LevelValidator.HasErrors(PlayerRules.Validate(level)), "Visual extra-box fixture failed validation.");
            app.OpenEditor(new MapDocument { level = level }); app.Editor.DismissHelp();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture("workshop-polish-editor");
            app.StartPlay(level.Clone(), level.id, false, true);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture("workshop-polish-play");
            yield return Command(Direction.East); yield return Command(Direction.East);
            Require(app.Game.State.status == GameStatus.Won && app.Game.State.BoxAt(level.Index(1, 3)) >= 0,
                "Extra pressure-plate box must remain in place when all goals are covered.");
            Pass("Real controller: two boxes / one goal wins with the spare box left on a pressure plate.");
        }

        IEnumerator CaptureReadabilityPreview()
        {
            // Exercise the real board's updated silhouettes, wall joins and button travel.
            var level = LevelData.Create(7, 6, "机关外观 · 墙角与按压反馈"); level.player = -1;
            var edit = new PlayerEditing(level, PlayerRules.CreateBook()); string reason;
            Require(edit.Place(PlayerEditing.Player, PlayerEditTarget.Ground(level.Index(0, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerEditing.Box, PlayerEditTarget.Ground(level.Index(5, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Goal, PlayerEditTarget.Ground(level.Index(6, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Plate, PlayerEditTarget.Ground(level.Index(1, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.RotationPlate, PlayerEditTarget.Ground(level.Index(3, 1)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Arrow, PlayerEditTarget.Ground(level.Index(3, 2)), 0, out reason), reason);
            Require(edit.Link(level.Index(3, 1), PlayerEditTarget.Ground(level.Index(3, 2)), out reason), reason);
            Require(edit.Place(PlayerRules.Fragile, PlayerEditTarget.Ground(level.Index(0, 2)), 0, out reason), reason);
            Require(edit.Place(PlayerRules.PortalEntrance, PlayerEditTarget.Ground(level.Index(0, 3)), 2, out reason), reason);
            Require(edit.Place(PlayerRules.PortalExit, PlayerEditTarget.Ground(level.Index(1, 3)), 1, out reason), reason);
            for (int x = 3; x <= 5; x++) Require(edit.Place(PlayerRules.Ice, PlayerEditTarget.Ground(level.Index(x, 3)), 0, out reason), reason);
            var gate = PlayerEditTarget.Boundary(level.Index(5, 4), Direction.East);
            Require(edit.Place(PlayerRules.Gate, gate, 0, out reason), reason);
            Require(edit.Link(level.Index(1, 1), gate, out reason), reason);
            // An L, a T and a cross; perimeter corners are present on every fixture.
            foreach (var target in new[] {
                PlayerEditTarget.Boundary(level.Index(1, 4), Direction.North),
                PlayerEditTarget.Boundary(level.Index(1, 4), Direction.East),
                PlayerEditTarget.Boundary(level.Index(3, 4), Direction.North),
                PlayerEditTarget.Boundary(level.Index(4, 4), Direction.North),
                PlayerEditTarget.Boundary(level.Index(3, 4), Direction.East),
                PlayerEditTarget.Boundary(level.Index(5, 4), Direction.North),
                PlayerEditTarget.Boundary(level.Index(6, 4), Direction.North),
                PlayerEditTarget.Boundary(level.Index(5, 5), Direction.East) })
                Require(edit.Place(PlayerEditing.Wall, target, 0, out reason), reason);
            Require(!LevelValidator.HasErrors(PlayerRules.Validate(level)), "Readability fixture failed validation.");
            app.OpenEditor(new MapDocument { level = level }); app.Editor.DismissHelp();
            yield return new WaitForSecondsRealtime(.6f);
            VerifyReadabilityProjectionAndPortals(level, "editor");
            yield return Capture("workshop-readability-editor");
            app.StartPlay(level.Clone(), level.id, false, true);
            yield return new WaitForSecondsRealtime(.6f);
            VerifyReadabilityProjectionAndPortals(level, "play");
            yield return Capture("workshop-readability-idle");
            Require(app.Game.view.PortalParticleCount > 0 && app.Game.view.PortalParticleCount <= BoardView.PortalParticleLimit,
                "The two live portal visuals must emit visible glints within their shared particle limit.");
            bool previousEffects = BoardAtmosphere.EffectsEnabled;
            try
            {
                BoardAtmosphere.EffectsEnabled = false;
                yield return null;
                yield return new WaitForEndOfFrame();
                Require(app.Game.view.PortalParticleCount == 0, "Disabling atmosphere effects must clear live portal particles by the following frame.");
            }
            finally { BoardAtmosphere.EffectsEnabled = previousEffects; }
            Pass("Portal atmosphere: real entrance/exit glints emitted within the shared cap; the effects switch cleared them without changing the authored map.");
            yield return Command(Direction.East);
            yield return new WaitForSecondsRealtime(.25f);
            yield return Capture("workshop-button-pressed");
            yield return Command(Direction.West);
            yield return new WaitForSecondsRealtime(.25f);
            yield return Capture("workshop-button-released");
            yield return Command(Direction.East); yield return Command(Direction.East); yield return Command(Direction.East);
            yield return new WaitForSecondsRealtime(.25f);
            yield return Capture("workshop-rotation-pressed");
            int rotation = RuleEngine.GetRotation(level, app.Game.State, level.Index(3, 2));
            yield return Command(Direction.North);
            yield return new WaitForSecondsRealtime(.25f);
            Require(RuleEngine.GetRotation(level, app.Game.State, level.Index(3, 2)) == rotation,
                "Releasing the rotation button must not rotate its target again.");
            yield return Capture("workshop-rotation-released");
            Pass("Real visual fixture: goal, ice, portals, fragile floor, L/T/cross wall joins and press/release feedback; rotation is retained on release.");
        }

        void VerifyReadabilityProjectionAndPortals(LevelData level, string stage)
        {
            var view = app.Game.view;
            Require(view && view.boardCamera, "Readability camera is unavailable in " + stage + ".");
            Camera camera = view.boardCamera;
            Vector3 origin = camera.WorldToScreenPoint(view.GetCellWorld(level.Index(2, 2)));
            Vector3 east = camera.WorldToScreenPoint(view.GetCellWorld(level.Index(3, 2)));
            Vector3 north = camera.WorldToScreenPoint(view.GetCellWorld(level.Index(2, 3)));
            Require(origin.z > 0 && east.z > 0 && north.z > 0 && east.x - origin.x > 2 && north.y - origin.y > 2,
                "The visible grid must project East rightwards and North upwards in " + stage + ".");
            Require(Mathf.Abs(east.y - origin.y) <= .25f && Mathf.Abs(north.x - origin.x) <= .25f,
                "The board must remain axis-aligned on screen in " + stage + ": adjacent X cells changed screen Y by "
                + (east.y - origin.y).ToString("F3") + " px; adjacent Z cells changed screen X by " + (north.x - origin.x).ToString("F3") + " px.");

            Transform generated = view.transform.Find("BoxLab Preview (generated)");
            Require(generated, "The real readability board has no generated geometry.");
            int portals = 0;
            for (int i = 0; i < level.cells.Count; i++)
            {
                bool entrance = level.cells[i].ruleId == PlayerRules.PortalEntrance;
                if (!entrance && level.cells[i].ruleId != PlayerRules.PortalExit) continue;
                portals++;
                string semanticName = entrance ? "Directional portal entrance" : "Directional portal exit";
                Transform portal = generated.Find("Cell " + (i % level.width) + "," + (i / level.width) + "/" + semanticName);
                Require(portal, "The actual portal visual is missing from cell " + i + ".");
                VerifyPortalAxes(view, portal, entrance, level.cells[i].rotation);

                // Reuse the actual rendered hierarchy off-screen, then exercise the same yaw
                // helper used by board and ghost art. No authored map or controller state changes.
                GameObject copy = null;
                try
                {
                    copy = Instantiate(portal.gameObject, view.transform, false);
                    copy.name = "Inactive four-direction portal verification";
                    copy.SetActive(false);
                    for (int rotation = 0; rotation < 4; rotation++)
                    {
                        copy.transform.localRotation = Quaternion.Euler(0, rotation * 90, 0);
                        BoardView.ApplyPortalDoorwayYaw(copy.transform, rotation);
                        VerifyPortalAxes(view, copy.transform, entrance, rotation);
                    }
                }
                finally { if (copy) Destroy(copy); }
            }
            Require(portals == 2, "The readability fixture must retain its two different portal directions.");
            Pass("Readability " + stage + ": real camera projects horizontal rows/vertical columns; actual entry/exit markers and all four doorway yaws preserve cardinal direction.");
        }

        static void VerifyPortalAxes(BoardView view, Transform portal, bool entrance, int rotation)
        {
            rotation = ((rotation % 4) + 4) % 4;
            Vector3 expectedFront = view.transform.TransformDirection(Quaternion.Euler(0, rotation * 90, 0) * Vector3.forward).normalized;
            Require(Vector3.Dot(portal.forward.normalized, expectedFront) > .9999f,
                "Portal display yaw changed its cardinal semantic root at rotation " + rotation + ".");
            Transform arrow = portal.Find("Direction");
            Require(arrow && Vector3.Dot(arrow.forward.normalized, entrance ? -expectedFront : expectedFront) > .9999f,
                "Portal floor arrow no longer shows the exact entry/exit travel direction at rotation " + rotation + ".");
            Transform doorway = portal.Find("Directional portal doorway");
            float expectedYaw = rotation == 1 ? 10 : rotation == 3 ? -10 : 0;
            Require(doorway && Mathf.Abs(Mathf.DeltaAngle(doorway.localEulerAngles.y, expectedYaw)) < .05f,
                "Only the doorway model should receive the intended side-on readability yaw at rotation " + rotation + ".");
        }

        IEnumerator SaveAcrossProcess()
        {
            var level = PlayerRules.CreateDemoLevels()[4]; var map = new MapDocument { level = level.Clone() };
            app.Storage.SaveMap(map); app.StartPlay(map.level, map.id, false, false);
            var initial = app.Game.State.Clone();
            app.Game.TryMove(Direction.East);
            Require(app.Game.IsBusy, "Animated move must engage the input lock.");
            for (int n = 0; n < 12; n++) { app.Game.TryMove(Direction.North); app.Game.Undo(); app.Game.Restart(); }
            yield return WaitIdle();
            Require(app.Game.State.moves == 1 && app.Game.ExportHistory().Count == 1 && app.Game.State.status == GameStatus.Playing, "Buffered inputs, undo or restart escaped the busy lock.");
            var record = new PhaseRecord { mapId = map.id, stateJson = JsonUtility.ToJson(app.Game.State), historyCount = app.Game.ExportHistory().Count };
            PlayerStorage.AtomicWrite(Path.Combine(app.Storage.Root, "acceptance-phase.json"), JsonUtility.ToJson(record));
            var store = new PlayerStorage(app.Storage.Root); var saved = store.LoadProgress();
            Require(saved != null && saved.mapId == map.id && JsonUtility.ToJson(saved.state) == record.stateJson, "Current position and arrow rotation did not persist.");
            Require(JsonUtility.ToJson(saved.history[0]) == JsonUtility.ToJson(initial), "Undo history did not persist.");
            Pass("Saved real mid-level progress with dynamic rotation and undo history; busy inputs were discarded.");
        }

        IEnumerator ResumeAcrossProcess()
        {
            string path = Path.Combine(app.Storage.Root, "acceptance-phase.json");
            Require(File.Exists(path), "Read phase requires a previous write phase in the same isolated data directory.");
            var record = JsonUtility.FromJson<PhaseRecord>(File.ReadAllText(path));
            var store = new PlayerStorage(app.Storage.Root); var saved = store.LoadProgress();
            Require(saved != null && saved.mapId == record.mapId && JsonUtility.ToJson(saved.state) == record.stateJson, "Reloaded progress differs from the write phase.");
            Require(saved.history.Count == record.historyCount, "Reloaded undo history count differs.");
            app.StartPlay(saved.level, saved.mapId, saved.builtIn, false, saved.state, saved.history);
            Require(JsonUtility.ToJson(app.Game.State) == record.stateJson, "Controller did not restore the persisted board.");
            app.Game.Undo();
            Require(app.Game.State.moves == 0 && app.Game.State.GetKey() == RuleEngine.CreateState(saved.level).GetKey(), "Undo after resume did not restore the original arrows/positions.");
            yield return Command(Direction.East); yield return Command(Direction.East);
            Require(app.Game.State.status == GameStatus.Won, "Restored game could not continue to completion.");
            app.Game.Restart();
            Require(app.Game.State.moves == 0 && app.Game.State.GetKey() == RuleEngine.CreateState(saved.level).GetKey(), "Restart after resume did not restore authored state.");
            Pass((phase == "read" ? "True separate-process" : "Fresh-store") + " resume restored arrows, positions and undo history; undo, win and restart succeeded.");
        }

        IEnumerator WaitForPlay()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (app.Page != WorkshopPage.Play)
            { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Valid runtime map did not enter Play after validation."); yield return null; }
        }
        IEnumerator WaitIdle()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (app.Game.IsBusy)
            { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Animation did not settle."); yield return null; }
            yield return null;
        }
        IEnumerator Command(Direction direction)
        {
            app.Game.TryMove(direction); yield return WaitIdle();
            Require(app.Game.State != null, "Controller lost its current board.");
        }

        public static List<string> VerifyEditing()
        {
            var results = new List<string>(); var book = PlayerRules.CreateBook();
            var level = LevelData.Create(6, 5, "编辑后端验收"); level.player = -1;
            var edit = new PlayerEditing(level, book); string reason;
            Require(edit.Place(PlayerRules.Plate, PlayerEditTarget.Ground(7), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Gate, PlayerEditTarget.Boundary(8, Direction.East), 0, out reason), reason);
            Require(edit.Link(7, PlayerEditTarget.Boundary(8, Direction.East), out reason), reason);
            string plateId = edit.Level.cells[7].id; int style = edit.Level.cells[7].signalStyle;
            Require(edit.Place(PlayerRules.Plate, PlayerEditTarget.Ground(13), 0, out reason, PlayerEditTarget.Ground(7)), reason);
            Require(edit.Level.cells[13].id == plateId && edit.Level.cells[13].signalStyle == style && edit.Links()[0].sourceCell == 13, "Moving a plate broke its identity/style/link.");
            Require(edit.Place(PlayerRules.Gate, PlayerEditTarget.Boundary(8, Direction.East), 0, out reason), reason);
            Require(edit.Links().Count == 1, "Repainting the same gate erased its links.");
            Require(edit.Place(PlayerRules.Goal, PlayerEditTarget.Ground(14), 0, out reason), reason);
            string beforeInvalid = JsonUtility.ToJson(edit.Level);
            Require(!edit.Place(PlayerRules.Plate, PlayerEditTarget.Ground(14), 0, out reason, PlayerEditTarget.Ground(13)) && JsonUtility.ToJson(edit.Level) == beforeInvalid, "Invalid drop changed or deleted the source.");
            edit.Remove(PlayerEditTarget.Ground(13), PlayerRules.Plate);
            Require(edit.Links().Count == 0, "Deleting a source must remove dangling connections.");
            Require(edit.Undo() && edit.Level.cells[13].id == plateId && edit.Links().Count == 1, "Undo did not restore the deleted source and links together.");
            results.Add("PASS PlayerEditing: stable identity/style during move, invalid-drop rollback, typed links, deletion cleanup and undo.");
            edit.BeginStroke();
            foreach (int cell in new[] { 18, 19, 20 }) Require(edit.Place(PlayerRules.Ice, PlayerEditTarget.Ground(cell), 0, out reason), reason);
            edit.EndStroke(); Require(edit.Undo(), "Stroke undo unavailable.");
            foreach (int cell in new[] { 18, 19, 20 }) Require(edit.Level.cells[cell].ruleId == PlayerRules.Plain, "Brush stroke did not undo as one operation.");
            Require(edit.Redo(), "Stroke redo unavailable.");
            foreach (int cell in new[] { 18, 19, 20 }) Require(edit.Level.cells[cell].ruleId == PlayerRules.Ice, "Brush redo lost a cell.");
            Require(edit.Place(PlayerRules.PortalEntrance, PlayerEditTarget.Ground(21), 0, out reason), reason);
            Require(edit.Place(PlayerRules.PortalExit, PlayerEditTarget.Ground(22), 0, out reason), reason);
            Require(edit.Level.cells[21].portalPair == edit.Level.cells[22].portalPair && !string.IsNullOrEmpty(edit.Level.cells[21].portalPair), "Portal placement did not auto-pair.");
            string portalId = edit.Level.cells[22].id, pair = edit.Level.cells[22].portalPair;
            Require(edit.Place(PlayerRules.PortalExit, PlayerEditTarget.Ground(23), 0, out reason, PlayerEditTarget.Ground(22)), reason);
            Require(edit.Level.cells[23].id == portalId && edit.Level.cells[23].portalPair == pair, "Moving a portal broke pair identity.");
            edit.MarkSaved(); Require(!edit.Dirty, "MarkSaved did not clear dirty.");
            edit.Rename("修改后的名字"); Require(edit.Dirty, "Rename did not mark dirty.");
            Require(edit.Undo() && !edit.Dirty, "Undo back to saved state should clear dirty.");
            results.Add("PASS PlayerEditing: whole-stroke undo/redo, automatic portal pairing, moved endpoint identity, saved-state dirty tracking.");
            Require(edit.Place(PlayerRules.RotationPlate, PlayerEditTarget.Ground(2), 0, out reason), reason);
            Require(edit.Place(PlayerRules.Arrow, PlayerEditTarget.Ground(3), 1, out reason), reason);
            Require(edit.Link(2, PlayerEditTarget.Ground(3), out reason), reason);
            Require(!edit.Link(13, PlayerEditTarget.Ground(3), out reason), "Ordinary plate incorrectly linked to arrow.");
            Require(!edit.Link(2, PlayerEditTarget.Boundary(8, Direction.East), out reason), "Rotation plate incorrectly linked to gate.");
            edit.Resize(7, 6);
            Require(edit.Level.cells[2].ruleId == PlayerRules.RotationPlate && edit.Level.cells[3].bindings[0].cellIds.Contains(edit.Level.cells[2].id), "Resize lost retained cell references.");
            results.Add("PASS PlayerEditing: source types are enforced and retained references survive resize.");
            return results;
        }

        public static List<string> VerifyStorage(string root)
        {
            var results = new List<string>(); var store = new PlayerStorage(root);
            var draft = new MapDocument { level = LevelData.Create(5, 5, "未完成草稿") }; draft.level.player = -1;
            draft.level.cells[2].ruleId = PlayerRules.PortalEntrance; draft.level.cells[2].portalPair = "unfinished";
            store.SaveMap(draft);
            Require(LevelValidator.HasErrors(PlayerRules.Validate(new PlayerStorage(root).LoadMap(draft.id).level)), "Unfinished draft should persist but remain invalid for play.");
            results.Add("PASS Storage: incomplete actor/goal/portal draft saves and reloads without being incorrectly playable.");
            var document = new MapDocument { level = PlayerRules.CreateDemoLevels()[4] }; store.SaveMap(document);
            string before = PlayerStorage.Fingerprint(document.level);
            var imported = store.Import(store.Export(document));
            Require(imported.id != document.id && imported.level.id != document.level.id && PlayerStorage.Fingerprint(store.LoadMap(document.id).level) == before, "Import overwrote an existing map.");
            var arrow = document.level.cells.Find(x => x.ruleId == PlayerRules.Arrow);
            var loadedArrow = new PlayerStorage(root).LoadMap(document.id).level.cells.Find(x => x.ruleId == PlayerRules.Arrow);
            Require(loadedArrow.id == arrow.id && loadedArrow.rotation == arrow.rotation && loadedArrow.bindings[0].cellIds[0] == arrow.bindings[0].cellIds[0], "Sharing/save changed orientation or stable connections.");
            results.Add("PASS Storage: disk roundtrip and import-as-copy preserve source maps, orientation, stable cell IDs and links.");
            results.AddRange(VerifyShareCodes(store));
            results.AddRange(VerifySurplusBoxStorage(store));
            Reject(() => PlayerStorage.ParseMap("{ malformed"), "Malformed JSON was accepted.");
            var badDocument = document.Clone(); badDocument.version = 999;
            Reject(() => PlayerStorage.ParseMap(JsonUtility.ToJson(badDocument)), "Unsupported map version was accepted.");
            badDocument = document.Clone(); badDocument.id = "../outside";
            Reject(() => store.SaveMap(badDocument), "Unsafe document ID was accepted.");
            string oversize = Path.Combine(root, "oversize.boxmap"); File.WriteAllText(oversize, new string(' ', PlayerStorage.MaxMapBytes + 1));
            Reject(() => store.Import(oversize), "Oversize map was imported.");
            results.Add("PASS Storage: malformed, unsupported, unsafe-ID and oversized imports are rejected.");
            var book = PlayerRules.CreateBook(); var initial = RuleEngine.CreateState(document.level); var moved = RuleEngine.Step(document.level, book, initial, Direction.East).state;
            var progress = new PlaySave { mapId = document.id, level = document.level.Clone(), mapHash = before, state = moved, history = new List<BoardState> { initial } };
            store.SaveProgress(progress); var restored = new PlayerStorage(root).LoadProgress();
            Require(JsonUtility.ToJson(restored.state) == JsonUtility.ToJson(moved) && restored.history.Count == 1, "Progress did not retain rotation, positions and undo history.");
            Action<Action<PlaySave>, string> rejectProgress = (mutate, message) =>
            {
                var bad = JsonUtility.FromJson<PlaySave>(JsonUtility.ToJson(progress)); mutate(bad); store.SaveProgress(bad);
                Reject(() => new PlayerStorage(root).LoadProgress(), message);
            };
            rejectProgress(x => x.state.rotations = new int[0], "Truncated rotations accepted.");
            rejectProgress(x => x.state.boxes[0] = x.state.player, "Actor overlap accepted.");
            rejectProgress(x => x.state.tiles[x.state.player] = PlayerRules.Hole, "Player standing on an altered hole accepted.");
            rejectProgress(x => x.state.tiles[document.level.cells.FindIndex(c => c.ruleId == PlayerRules.Goal)] = PlayerRules.Plain, "Removed goal accepted.");
            rejectProgress(x => x.state.status = GameStatus.Won, "Incorrect won status accepted.");
            rejectProgress(x => x.mapHash = "changed", "Mismatched map hash accepted.");
            store.SaveProgress(progress); document.level.description += " map revision"; store.SaveMap(document);
            Reject(() => new PlayerStorage(root).LoadProgress(), "Progress resumed against a modified authored map.");
            results.Add("PASS Storage: malformed state, altered terrain/goal, forged status/hash and map-revision mismatch are rejected.");
            var backup = new MapDocument { level = LevelData.Create(4, 4, "备份版本一") }; backup.level.player = -1; store.SaveMap(backup);
            backup.level.name = "备份版本二"; store.SaveMap(backup);
            File.WriteAllText(Path.Combine(store.MapsPath, backup.id + ".boxmap"), "broken");
            Require(new PlayerStorage(root).LoadMap(backup.id).level.name == "备份版本一", "Corrupt current map did not recover valid backup.");
            store.DeleteMap(backup.id);
            Require(!store.Exists(backup.id) && Directory.GetFiles(Path.Combine(root, "Deleted"), "*.boxmap", SearchOption.AllDirectories).Length > 0, "Deleted maps should remain recoverable in archive.");
            results.Add("PASS Storage: interrupted/corrupt latest file can use a backup; deletion archives existing data.");
            return results;
        }

        static List<string> VerifySurplusBoxStorage(PlayerStorage store)
        {
            var book = PlayerRules.CreateBook();
            var level = LevelData.Create(4, 3, "额外箱存档回归"); level.player = 4;
            level.boxes.AddRange(new[] { 5, 9 }); level.cells[6].ruleId = PlayerRules.Goal;
            level.cells[9].ruleId = PlayerRules.Plate; level.cells[5].editTopLayer = 1;
            level.edges.Add(new EdgeData { cell = 5, direction = Direction.East, kind = EdgeKind.Gate, ruleId = PlayerRules.Gate,
                bindings = new List<BindingData> { new BindingData { key = PlayerRules.Switches, cellIds = new List<string> { level.cells[9].id } } } });
            var document = new MapDocument { level = level }; store.SaveMap(document);
            var initial = RuleEngine.CreateState(level, book); var won = RuleEngine.Step(level, book, initial, Direction.East).state;
            var save = new PlaySave { mapId = document.id, mapHash = PlayerStorage.Fingerprint(level), level = level.Clone(), state = won, history = new List<BoardState> { initial } };
            store.SaveProgress(save); var restored = new PlayerStorage(store.Root).LoadProgress();
            Require(restored.state.status == GameStatus.Won && restored.state.boxes[1] == 9 && restored.history[0].status == GameStatus.Playing, "Extra box on a plate invalidated saved completion or undo history.");
            var wrong = won.Clone(); wrong.status = GameStatus.Playing;
            Reject(() => PlayerStorage.ValidateState(level, wrong), "Fully covered targets with an extra box accepted an incorrect Playing status.");
            var imported = store.ImportShareCode(store.ExportShareCode(document));
            Require(!LevelValidator.HasErrors(PlayerRules.Validate(imported.level)) && imported.level.boxes.Count == 2 && imported.level.cells[5].editTopLayer == 1,
                "Sharing lost an extra box or its authoring deletion layer.");
            var layerChanged = level.Clone(); layerChanged.cells[5].editTopLayer = 0;
            Require(PlayerStorage.Fingerprint(layerChanged) == PlayerStorage.Fingerprint(level), "Editor-only deletion order changed the gameplay revision hash.");
            string legacyLevelJson = JsonUtility.ToJson(level).Replace(",\"editTopLayer\":0", "").Replace(",\"editTopLayer\":1", "");
            using (var sha = SHA256.Create()) save.mapHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(legacyLevelJson))).Replace("-", "");
            string legacySaveJson = JsonUtility.ToJson(save).Replace(",\"editTopLayer\":0", "").Replace(",\"editTopLayer\":1", "");
            File.WriteAllText(Path.Combine(store.Root, "continue.json"), legacySaveJson);
            restored = new PlayerStorage(store.Root).LoadProgress();
            Require(restored.state.status == GameStatus.Won && restored.level.cells[5].editTopLayer == 0, "A pre-layer-field progress hash failed to load with the legacy default.");

            var disposal = LevelData.Create(4, 3, "剩余箱数量存档回归"); disposal.player = 0;
            disposal.boxes.AddRange(new[] { 1, 6 }); disposal.cells[11].ruleId = PlayerRules.Goal;
            disposal.cells[2].ruleId = disposal.cells[7].ruleId = PlayerRules.Hole;
            document = new MapDocument { level = disposal }; store.SaveMap(document);
            initial = RuleEngine.CreateState(disposal, book);
            var dropped = RuleEngine.Step(disposal, book, initial, Direction.East).state;
            save = new PlaySave { mapId = document.id, mapHash = PlayerStorage.Fingerprint(disposal), level = disposal.Clone(), state = dropped, history = new List<BoardState> { initial } };
            store.SaveProgress(save); restored = new PlayerStorage(store.Root).LoadProgress();
            Require(restored.state.status == GameStatus.Playing && restored.state.boxes.Contains(-1), "One spare box lost incorrectly invalidated resumable progress.");
            var moved = RuleEngine.Step(disposal, book, dropped, Direction.North).state;
            save.state = RuleEngine.Step(disposal, book, moved, Direction.East).state;
            save.history.Add(dropped); save.history.Add(moved); store.SaveProgress(save);
            restored = new PlayerStorage(store.Root).LoadProgress();
            Require(restored.state.status == GameStatus.Lost && restored.history[1].status == GameStatus.Playing, "Too few remaining boxes did not persist failure with valid undo history.");
            return new List<string> { "PASS Surplus boxes: plate helper completion, disposal-then-continue/fail, undo history and share-code roundtrip; editor deletion layer persists without invalidating legacy progress hashes." };
        }

        static List<string> VerifyShareCodes(PlayerStorage store)
        {
            var results = new List<string>();
            var level = LevelData.Create(6, 5, "分享码 · 布局与连接");
            level.description = "保留中文、箭头方向、按钮样式、墙边、门关联和传送配对。";
            level.player = 0; level.boxes.Add(1); level.cells[2].ruleId = PlayerRules.Goal;
            level.cells[7].ruleId = PlayerRules.Plate; level.cells[7].signalStyle = 4;
            level.cells[8].ruleId = PlayerRules.RotationPlate; level.cells[8].signalStyle = 13;
            level.cells[9].ruleId = PlayerRules.Arrow; level.cells[9].rotation = 3;
            level.cells[9].bindings.Add(new BindingData { key = PlayerRules.Switches, cellIds = new List<string> { level.cells[8].id } });
            level.cells[21].ruleId = PlayerRules.PortalEntrance; level.cells[22].ruleId = PlayerRules.PortalExit;
            level.cells[21].rotation = 3; level.cells[22].rotation = 1;
            level.cells[21].portalPair = level.cells[22].portalPair = "share-regression-pair";
            level.cells[14].ruleId = PlayerRules.Ice; level.cells[15].ruleId = PlayerRules.Fragile;
            level.cells[16].ruleId = PlayerRules.Hole; level.cells[17].ruleId = PlayerRules.Selective;
            level.cells[18].ruleId = PlayerRules.BoxOnly;
            level.edges.Add(new EdgeData { cell = 3, direction = Direction.South, kind = EdgeKind.Wall });
            level.edges.Add(new EdgeData { cell = 10, direction = Direction.East, kind = EdgeKind.Gate, ruleId = PlayerRules.Gate,
                bindings = new List<BindingData> { new BindingData { key = PlayerRules.Switches, cellIds = new List<string> { level.cells[7].id } } } });
            Require(!LevelValidator.HasErrors(PlayerRules.Validate(level)), "Share-code fixture is not a valid authored map.");
            var source = new MapDocument { level = level }; store.SaveMap(source);
            string sourceJson = JsonUtility.ToJson(source), sourceHash = PlayerStorage.Fingerprint(source.level);
            int mapsBefore = Directory.GetFiles(store.MapsPath, "*.boxmap").Length;
            string code = store.ExportShareCode(source);
            Require(code.StartsWith(PlayerStorage.ShareCodePrefix, StringComparison.Ordinal) && code.IndexOf('\n') < 0, "Export did not produce a single portable share code.");
            Require(JsonUtility.ToJson(source) == sourceJson && Directory.GetFiles(store.MapsPath, "*.boxmap").Length == mapsBefore, "Share-code export mutated or wrote a map.");
            var formatted = new StringBuilder(" \r\n\t");
            for (int i = 0; i < code.Length; i++) { formatted.Append(code[i]); if (i % 53 == 52) formatted.Append("\r\n　"); }
            formatted.Append(" \t\n");
            var copy = store.ImportShareCode(formatted.ToString());
            Require(copy.id != source.id && copy.level.id != source.level.id, "Share import reused source identity.");
            var restored = new PlayerStorage(store.Root).LoadMap(copy.id);
            var normalized = restored.level.Clone(); normalized.id = source.level.id; normalized.name = source.level.name;
            Require(restored.level.cells[17].ruleId == PlayerRules.Selective && restored.level.cells[18].ruleId == PlayerRules.BoxOnly,
                "Share code changed either the person-only or box-only passage type.");
            Require(PlayerStorage.Fingerprint(normalized) == sourceHash, "Share import changed layout, pairing, connections, style or rotations.");
            Require(PlayerStorage.Fingerprint(store.LoadMap(source.id).level) == sourceHash && JsonUtility.ToJson(source) == sourceJson, "Share import changed the source map.");
            var secondCopy = store.ImportShareCode(code);
            Require(secondCopy.id != copy.id && secondCopy.level.id != copy.level.id && Directory.GetFiles(store.MapsPath, "*.boxmap").Length == mapsBefore + 2, "Importing the same code again must create a separate copy.");
            results.Add("PASS Share code: BL2 text roundtrip, wrapped whitespace, Chinese names, full layout, portal pairs, links, styles and rotations; each import is an independent persisted copy.");

            int countBeforeRejects = Directory.GetFiles(store.MapsPath, "*.boxmap").Length;
            Reject(() => store.ImportShareCode(" \r\n"), "Empty share code was accepted.");
            Reject(() => store.ImportShareCode("BL9:" + code.Substring(PlayerStorage.ShareCodePrefix.Length)), "Unknown share-code version was accepted.");
            Reject(() => store.ImportShareCode("地图分享码：" + code), "Explanatory prose was incorrectly parsed as a share code.");
            Reject(() => store.ImportShareCode(PlayerStorage.ShareCodePrefix + "???"), "Invalid base64 share code was accepted.");
            Reject(() => store.ImportShareCode(code.Substring(0, code.Length - 4)), "Truncated share code was accepted.");
            byte[] damaged = Convert.FromBase64String(code.Substring(PlayerStorage.ShareCodePrefix.Length)); damaged[damaged.Length - 1] ^= 1;
            Reject(() => store.ImportShareCode(PlayerStorage.ShareCodePrefix + Convert.ToBase64String(damaged)), "Corrupt share-code payload was accepted.");
            Reject(() => store.ImportShareCode(new string('A', PlayerStorage.MaxShareCodeChars + 1)), "Oversized pasted share code was accepted.");
            string expanded = ShareCodeForTest(Encoding.UTF8.GetBytes(new string(' ', PlayerStorage.MaxMapBytes + 1)));
            Reject(() => store.ImportShareCode(expanded), "Compressed share code expanded beyond the map limit.");
            string invalidJson = ShareCodeForTest(Encoding.UTF8.GetBytes("{ malformed"));
            Reject(() => store.ImportShareCode(invalidJson), "Compressed malformed JSON was accepted.");
            var unsupported = source.Clone(); unsupported.version = 999;
            string invalidSchema = ShareCodeForTest(Encoding.UTF8.GetBytes(JsonUtility.ToJson(unsupported)));
            Reject(() => store.ImportShareCode(invalidSchema), "A valid compressed payload bypassed map version validation.");
            string invalidUtf8 = ShareCodeForTest(new byte[] { 0xff, 0xfe, 0xfd });
            Reject(() => store.ImportShareCode(invalidUtf8), "Invalid UTF-8 share payload was accepted.");
            Require(Directory.GetFiles(store.MapsPath, "*.boxmap").Length == countBeforeRejects && PlayerStorage.Fingerprint(store.LoadMap(source.id).level) == sourceHash,
                "Rejected share codes left new files or changed the original map.");
            results.Add("PASS Share code: bad prefix/base64, truncation, corruption, oversized input/decompression, malformed JSON/UTF-8 and unsupported map versions rejected before any map write.");
            return results;
        }

        // Builds deliberate malformed payloads so acceptance checks exercise the importer, not the exporter.
        static string ShareCodeForTest(byte[] json)
        {
            byte[] compressed;
            using (var output = new MemoryStream())
            { using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(json, 0, json.Length); compressed = output.ToArray(); }
            byte[] payload = new byte[compressed.Length + 32];
            using (var sha = SHA256.Create()) Buffer.BlockCopy(sha.ComputeHash(compressed), 0, payload, 0, 32);
            Buffer.BlockCopy(compressed, 0, payload, 32, compressed.Length);
            return PlayerStorage.ShareCodePrefix + Convert.ToBase64String(payload);
        }

        IEnumerator Capture(string name)
        {
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            CheckRendering(); Texture2D texture = null; string details; string source = "screen including GUI";
            try
            {
                try { texture = ScreenCapture.CaptureScreenshotAsTexture(); }
                catch (Exception error) { results.Add("NOTE screen capture failed: " + error.Message); }
                if (!Visible(texture, out details))
                {
                    if (texture) Destroy(texture); texture = CameraImage(app.Game.view.boardCamera);
                    source = "camera-only fallback; GUI/HUD not included";
                    results.Add("NOTE hidden/minimized screen was blank; fallback does not validate the visible UI.");
                }
                Require(Visible(texture, out details), "Screenshot has no visible scene variation.");
                File.WriteAllBytes(Path.Combine(evidence, name + ".png"), texture.EncodeToPNG());
                Pass("Screenshot " + name + ": " + source + ", " + details);
            }
            finally { if (texture) Destroy(texture); }
        }
        void CheckRendering()
        {
            Require(app.Game.view && app.Game.view.boardCamera, "Missing runtime board camera.");
            foreach (var renderer in app.Game.view.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    Require(material && material.shader && material.shader.isSupported && material.shader.name != "Hidden/InternalErrorShader", "Missing or unsupported runtime material.");
        }
        static Texture2D CameraImage(Camera camera)
        {
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; var oldRect = camera.rect; float aspect = camera.aspect;
            var target = new RenderTexture(1280, 800, 24); Texture2D image = null;
            try
            {
                Require(target.Create(), "RenderTexture creation failed."); camera.targetTexture = target; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = 1.6f;
                camera.Render(); RenderTexture.active = target; image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply(); return image;
            }
            catch { if (image) Destroy(image); throw; }
            finally { camera.targetTexture = oldTarget; camera.rect = oldRect; camera.aspect = aspect; RenderTexture.active = oldActive; target.Release(); Destroy(target); }
        }
        static bool Visible(Texture2D texture, out string details)
        {
            details = "no image"; if (!texture || texture.width < 100 || texture.height < 100) return false;
            var pixels = texture.GetPixels32(); int step = Math.Max(1, pixels.Length / 10000), low = 765, high = 0;
            for (int i = 0; i < pixels.Length; i += step) { int brightness = pixels[i].r + pixels[i].g + pixels[i].b; low = Math.Min(low, brightness); high = Math.Max(high, brightness); }
            details = texture.width + "x" + texture.height + ", brightness range " + (high - low); return high - low >= 45;
        }
        static void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch { rejected = true; } Require(rejected, message); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Pass(string message) { results.Add("PASS " + message); Debug.Log("BOXLAB_WORKSHOP_CHECK " + message); }
        void Finish(bool success, string message)
        {
            if (finished) return; finished = true;
            string marker = success ? "BOXLAB_WORKSHOP_SUCCESS" : "BOXLAB_WORKSHOP_FAILURE";
            results.Insert(0, marker); results.Add("Phase: " + phase); results.Add("Elapsed seconds: " + (Time.realtimeSinceStartupAsDouble - started).ToString("F2"));
            if (!string.IsNullOrEmpty(message)) results.Add(message);
            try { Directory.CreateDirectory(evidence); File.WriteAllLines(Path.Combine(evidence, "workshop-runtime-" + phase + ".txt"), results); }
            catch (Exception error) { Debug.LogError(error); }
            if (success) Debug.Log(marker); else Debug.LogError(marker + "\n" + message);
            Application.Quit(success ? 0 : 1);
        }
    }
}
