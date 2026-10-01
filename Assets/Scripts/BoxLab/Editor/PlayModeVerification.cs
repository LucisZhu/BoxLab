#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BoxLab.Editor
{
    /// <summary>Exercises the actual Play-mode controller and animations, not only the pure rule engine.</summary>
    [InitializeOnLoad]
    public static class PlayModeVerification
    {
        private const string Prefix = "BoxLab.PlayVerification.";
        private const string RunningKey = Prefix + "Running";
        private const string ReportKey = Prefix + "Report";
        private const string AssetKey = Prefix + "Asset";
        private const string BaselineKey = Prefix + "Baseline";

        [Serializable]
        private sealed class Report
        {
            public string started;
            public string finished;
            public string projectAsset;
            public string runId;
            public bool runtimeCompleted;
            public bool success;
            public List<string> checks = new List<string>();
            public List<string> screenshots = new List<string>();
            public string error = "";
        }

        private static Report report;
        private static Stack<IEnumerator> routines;
        private static GameController controller;
        private static BoxProject temporaryProject;
        private static double runDeadline;
        private static bool exiting;
        private static double enteredAt;

        static PlayModeVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += Update;
            if (SessionState.GetBool(RunningKey, false)) LoadReport();
        }

        [MenuItem("Tools/BoxLab/验证实际试玩流程", priority = 31)]
        public static void StartRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("请先退出 Play，再开始实际试玩验证。");
                return;
            }
            if (SessionState.GetBool(RunningKey, false))
            {
                Debug.LogWarning("实际试玩验证已在运行。");
                return;
            }
            var currentController = UnityEngine.Object.FindObjectOfType<GameController>();
            if (!currentController || !currentController.project)
                throw new InvalidOperationException("当前场景缺少已配置的 GameController。请先打开 BoxLab 场景。");
            string assetPath = AssetDatabase.GetAssetPath(currentController.project);
            if (string.IsNullOrEmpty(assetPath))
                throw new InvalidOperationException("试玩验证需要一个已保存的 BoxProject 资产。");
            report = new Report
            {
                started = DateTime.Now.ToString("O"),
                projectAsset = assetPath,
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss")
            };
            SessionState.SetString(AssetKey, assetPath);
            SessionState.SetString(BaselineKey, JsonUtility.ToJson(currentController.project));
            SessionState.SetBool(RunningKey, true);
            SaveReport();
            exiting = false;
            controller = null;
            routines = null;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            Debug.Log("BOXLAB_PLAY_START：正在进入实际 Play 模式，验证期间请勿操作游戏输入。");
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                LoadReport();
                enteredAt = EditorApplication.timeSinceStartup;
                runDeadline = enteredAt + 180;
                exiting = false;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                LoadReport();
                if (report != null && !report.runtimeCompleted && string.IsNullOrEmpty(report.error))
                {
                    report.error = "实际试玩验证尚未完成，Play 模式已退出。";
                    report.success = false;
                    SaveReport();
                }
                exiting = true;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                FinishInEditor();
            }
        }

        private static void Update()
        {
            if (!SessionState.GetBool(RunningKey, false) || !EditorApplication.isPlaying || exiting) return;
            try
            {
                if (report == null) LoadReport();
                if (enteredAt == 0)
                {
                    enteredAt = EditorApplication.timeSinceStartup;
                    runDeadline = enteredAt + 180;
                }
                if (EditorApplication.timeSinceStartup > runDeadline) throw new TimeoutException("实际试玩验证达到 180 秒总上限。");
                if (routines == null)
                {
                    controller = UnityEngine.Object.FindObjectOfType<GameController>();
                    // Start must initialize the saved scene before tests may replace its runtime project reference.
                    if (!controller || controller.State == null)
                    {
                        if (EditorApplication.timeSinceStartup - enteredAt > 15) throw new TimeoutException("GameController.Start 未能在 15 秒内产生有效局面，请检查 Console。");
                        return;
                    }
                    temporaryProject = ScriptableObject.CreateInstance<BoxProject>();
                    temporaryProject.hideFlags = HideFlags.HideAndDontSave;
                    JsonUtility.FromJsonOverwrite(SessionState.GetString(BaselineKey, ""), temporaryProject);
                    controller.project = temporaryProject;
                    if (controller.view) controller.view.animationSeconds = .045f;
                    routines = new Stack<IEnumerator>();
                    routines.Push(RunRuntime());
                }
                // Flatten nested enumerators; a null yield gives Unity a real rendered/update frame.
                for (int transitions = 0; transitions < 100 && routines.Count > 0; transitions++)
                {
                    var routine = routines.Peek();
                    if (!routine.MoveNext()) { routines.Pop(); continue; }
                    var nested = routine.Current as IEnumerator;
                    if (nested != null) { routines.Push(nested); continue; }
                    break;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static IEnumerator RunRuntime()
        {
            var levels = temporaryProject.levels;
            Require(levels != null && levels.Count > 0, "没有可验证的关卡。");
            int iceIndex = FindLevel(TerrainVisual.Ice);
            int fragileIndex = FindLevel(TerrainVisual.Fragile);
            int holeIndex = FindLevel(TerrainVisual.Hole);
            Require(iceIndex >= 0 && fragileIndex >= 0 && holeIndex >= 0, "需要冰面、坍塌和空洞示例来验证边界行为。");

            // Ice has a multi-frame transition, so repeated input arrives during a genuine animation.
            controller.LoadLevel(iceIndex);
            Require(controller.State != null, "冰面关卡加载失败。");
            var iceInitial = controller.State.Clone();
            var iceDirection = FindChangedDirection(levels[iceIndex], iceInitial, true);
            var iceExpected = RuleEngine.Step(levels[iceIndex], temporaryProject.book, iceInitial, iceDirection).state;
            controller.TryMove(iceDirection);
            Require(controller.IsBusy, "有效指令没有进入动画锁定状态。");
            for (int i = 0; i < 12; i++) controller.TryMove((Direction)(i % 4));
            controller.Undo();
            controller.Restart();
            yield return WaitForIdle("冰面动画与忙碌输入丢弃");
            RequireSame(controller.State, iceExpected, "动画期间输入被排队或撤销/重开错误打断动画。");
            Pass("忙碌期间连续 12 次移动、撤销与重开均被忽略；结算只包含原始指令。");
            yield return Capture("01-ice-settled");
            controller.Undo();
            RequireSame(controller.State, iceInitial, "冰面连锁撤销未恢复原始完整局面。");
            Pass("冰面整条自动移动链由一次撤销完整恢复。");
            controller.TryMove(iceDirection);
            yield return WaitForIdle("冰面重开前动画");
            controller.Restart();
            RequireSame(controller.State, iceInitial, "重开未恢复初始局面。");

            controller.LoadLevel(fragileIndex);
            var fragileInitial = controller.State.Clone();
            Direction fragileDirection = FindFragilePush(levels[fragileIndex], fragileInitial);
            int fragileCell = levels[fragileIndex].Neighbor(fragileInitial.player, fragileDirection);
            int fragileBox = fragileInitial.BoxAt(fragileCell);
            controller.TryMove(fragileDirection);
            yield return WaitForIdle("坍塌推动");
            Require(controller.State.player == fragileInitial.player, "坍塌格上的箱子被推出后，人物错误跟进。");
            Require(temporaryProject.book.Find(controller.State.tiles[fragileCell]).visual == TerrainVisual.Hole, "原箱子格没有立即变为空洞。");
            Require(controller.State.boxes[fragileBox] == levels[fragileIndex].Neighbor(fragileCell, fragileDirection), "坍塌推动后的箱子位置错误。");
            Pass("实际 Play：A1 人物保持原位，A2 坍塌为空洞，箱子到 A3。");
            yield return Capture("02-fragile-no-follow");
            controller.Undo();
            RequireSame(controller.State, fragileInitial, "撤销未同时恢复人物、箱子与坍塌地面。");
            controller.TryMove(fragileDirection);
            yield return WaitForIdle("坍塌重开前动画");
            controller.Restart();
            RequireSame(controller.State, fragileInitial, "重开未恢复坍塌道路。");
            Pass("坍塌局面的一次撤销和重开均恢复地面、箱子、人物及计数。");

            controller.LoadLevel(holeIndex);
            var holeInitial = controller.State.Clone();
            Direction holeDirection = FindLosingDirection(levels[holeIndex], holeInitial);
            controller.TryMove(holeDirection);
            yield return WaitForIdle("空洞掉落");
            Require(controller.State.status == GameStatus.Lost, "箱子进入空洞后没有出现失败状态。");
            Require(controller.State.boxes.Exists(position => position < 0), "掉入空洞的箱子没有被移除。");
            yield return Capture("03-hole-failure");
            controller.Undo();
            RequireSame(controller.State, holeInitial, "失败后的撤销没有恢复已移除箱子。");
            controller.TryMove(holeDirection);
            yield return WaitForIdle("空洞重开前动画");
            controller.Restart();
            RequireSame(controller.State, holeInitial, "失败后的重开没有恢复初始局面。");
            Pass("实际 Play：空洞移除箱子并失败；撤销与重开都恢复可继续操作的初始局面。");

            for (int index = 0; index < levels.Count; index++)
            {
                var level = levels[index];
                var solver = new SokobanSolver(level, temporaryProject.book, 250000, 20000);
                while (solver.Status == SolveStatus.Searching)
                {
                    solver.Step(250);
                    yield return null;
                }
                Require(solver.Status == SolveStatus.Solved, level.name + " 求解未成功：" + solver.Message);
                controller.LoadLevel(index);
                Require(controller.State != null, level.name + " 实际加载失败。");
                foreach (Direction direction in solver.Solution)
                {
                    BoardState before = controller.State.Clone();
                    var expected = RuleEngine.Step(level, temporaryProject.book, before, direction).state;
                    controller.TryMove(direction);
                    yield return WaitForIdle(level.name + " 解法动画");
                    RequireSame(controller.State, expected, level.name + " 实际控制器与求解逻辑结果不一致。");
                }
                Require(controller.State.status == GameStatus.Won, level.name + " 解法通过实际控制器回放后未通关。");
                Pass("实际控制器与动画回放通关：" + level.name + " / " + solver.Solution.Count + " 次指令 / " + solver.Visited + " 搜索局面。");
                if (index == levels.Count - 1 || HasVisual(level, TerrainVisual.PortalEntrance))
                    yield return Capture("win-" + (index + 1).ToString("00"));
            }

            // The serialized authoring asset is also checked before exiting Play.
            var asset = AssetDatabase.LoadAssetAtPath<BoxProject>(SessionState.GetString(AssetKey, ""));
            Require(asset && JsonUtility.ToJson(asset) == SessionState.GetString(BaselineKey, ""), "试玩期间编辑资产发生变化。");
            Pass("实际 Play 全过程未修改作者的项目、关卡、规则或 selectedLevel。");
            report.runtimeCompleted = true;
            report.success = true;
            SaveReport();
            exiting = true;
            EditorApplication.isPlaying = false;
        }

        private static IEnumerator WaitForIdle(string label)
        {
            double deadline = EditorApplication.timeSinceStartup + 12;
            // Always yield once so even a no-animation operation is observed on the next Unity update.
            yield return null;
            while (controller && controller.IsBusy)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label + " 在 12 秒内未完成。");
                yield return null;
            }
            Require(controller && controller.State != null, label + " 完成后控制器或局面丢失。");
        }

        private static IEnumerator Capture(string suffix)
        {
            double settle = EditorApplication.timeSinceStartup + .25;
            while (EditorApplication.timeSinceStartup < settle) yield return null;
            Directory.CreateDirectory("BoxLabEvidence");
            string relative = "BoxLabEvidence/play-" + report.runId + "-" + suffix + ".png";
            string absolute = Path.GetFullPath(relative);
            ScreenCapture.CaptureScreenshot(absolute);
            double deadline = EditorApplication.timeSinceStartup + 8;
            while (!File.Exists(absolute) || new FileInfo(absolute).Length < 1000)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("截图未能保存：" + relative);
                yield return null;
            }
            report.screenshots.Add(relative);
            SaveReport();
            yield return null;
        }

        private static int FindLevel(TerrainVisual visual)
        {
            for (int i = 0; i < temporaryProject.levels.Count; i++)
                if (HasVisual(temporaryProject.levels[i], visual)) return i;
            return -1;
        }

        private static bool HasVisual(LevelData level, TerrainVisual visual)
        {
            foreach (var cell in level.cells)
            {
                var rule = temporaryProject.book.Find(cell.ruleId);
                if (rule != null && rule.visual == visual) return true;
            }
            return false;
        }

        private static Direction FindChangedDirection(LevelData level, BoardState initial, bool preferLongChain)
        {
            Direction result = Direction.North;
            int bestFrames = -1;
            for (int d = 0; d < 4; d++)
            {
                var step = RuleEngine.Step(level, temporaryProject.book, initial, (Direction)d);
                if (!step.changed || step.interrupted) continue;
                if (!preferLongChain) return (Direction)d;
                if (step.frames.Count > bestFrames) { bestFrames = step.frames.Count; result = (Direction)d; }
            }
            Require(bestFrames >= 0, "没有找到有效移动。");
            return result;
        }

        private static Direction FindFragilePush(LevelData level, BoardState initial)
        {
            for (int d = 0; d < 4; d++)
            {
                int source = level.Neighbor(initial.player, (Direction)d);
                if (initial.BoxAt(source) < 0) continue;
                var rule = temporaryProject.book.Find(initial.tiles[source]);
                if (rule != null && rule.visual == TerrainVisual.Fragile && RuleEngine.Step(level, temporaryProject.book, initial, (Direction)d).changed) return (Direction)d;
            }
            throw new InvalidOperationException("坍塌示例初始布局没有可直接推动的坍塌箱子。");
        }

        private static Direction FindLosingDirection(LevelData level, BoardState initial)
        {
            for (int d = 0; d < 4; d++)
            {
                var step = RuleEngine.Step(level, temporaryProject.book, initial, (Direction)d);
                if (step.changed && step.state.status == GameStatus.Lost) return (Direction)d;
            }
            throw new InvalidOperationException("空洞示例初始布局没有可直接验证的掉落操作。");
        }

        private static void RequireSame(BoardState actual, BoardState expected, string message)
        { Require(actual != null && expected != null && JsonUtility.ToJson(actual) == JsonUtility.ToJson(expected), message); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void Pass(string message)
        {
            report.checks.Add("PASS " + message);
            SaveReport();
        }

        private static void Fail(Exception exception)
        {
            if (report == null) LoadReport();
            if (report == null) report = new Report { started = DateTime.Now.ToString("O") };
            report.success = false;
            report.error = exception.ToString();
            SaveReport();
            Debug.LogError("BOXLAB_PLAY_FAILURE：" + exception.Message);
            exiting = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else FinishInEditor();
        }

        private static void FinishInEditor()
        {
            LoadReport();
            if (report == null) return;
            try
            {
                string path = SessionState.GetString(AssetKey, "");
                string baseline = SessionState.GetString(BaselineKey, "");
                var asset = AssetDatabase.LoadAssetAtPath<BoxProject>(path);
                if (!asset || JsonUtility.ToJson(asset) != baseline)
                {
                    report.success = false;
                    report.error += "\n退出试玩后作者数据与初始快照不一致。";
                    if (asset && !string.IsNullOrEmpty(baseline))
                    {
                        JsonUtility.FromJsonOverwrite(baseline, asset);
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssets();
                        report.checks.Add("已从测试前快照恢复作者数据；本次验证仍标记失败。");
                    }
                }
                else report.checks.Add("PASS 退出 Play 后作者资产 JSON 完全不变，原始 selectedLevel 保留。");
                report.finished = DateTime.Now.ToString("O");
                Directory.CreateDirectory("BoxLabEvidence");
                File.WriteAllText("BoxLabEvidence/play-report.json", JsonUtility.ToJson(report, true));
                var lines = new List<string>
                {
                    report.success ? "BOXLAB_PLAY_SUCCESS" : "BOXLAB_PLAY_FAILURE",
                    "开始：" + report.started,
                    "结束：" + report.finished,
                    "作者资产：" + report.projectAsset,
                    ""
                };
                lines.AddRange(report.checks);
                if (!string.IsNullOrEmpty(report.error)) { lines.Add(""); lines.Add(report.error); }
                lines.Add(""); lines.AddRange(report.screenshots);
                File.WriteAllLines("BoxLabEvidence/play-report.txt", lines);
                if (report.success) Debug.Log("BOXLAB_PLAY_SUCCESS：真实控制器、动画、输入锁、撤销、重开与全部关卡通关验证完成。报告：BoxLabEvidence/play-report.txt");
                else Debug.LogError("BOXLAB_PLAY_FAILURE：" + report.error);
            }
            finally
            {
                SessionState.SetBool(RunningKey, false);
                SessionState.EraseString(BaselineKey);
                routines = null;
                controller = null;
                temporaryProject = null;
                enteredAt = 0;
                exiting = false;
            }
        }

        private static void SaveReport()
        { if (report != null) SessionState.SetString(ReportKey, JsonUtility.ToJson(report)); }
        private static void LoadReport()
        {
            string json = SessionState.GetString(ReportKey, "");
            if (!string.IsNullOrEmpty(json)) report = JsonUtility.FromJson<Report>(json);
        }
    }
}
#endif
