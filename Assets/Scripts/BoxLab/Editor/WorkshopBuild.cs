#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BoxLab.Editor
{
    /// <summary>Explicit V2 verification/build entry points. A request file is consumed once.</summary>
    [InitializeOnLoad]
    public static class WorkshopBuild
    {
        const string Evidence = "BoxLabEvidence";
        const string RequestPath = Evidence + "/request-v2.txt";
        static bool running;
        static double nextPoll;

        static WorkshopBuild() { EditorApplication.update += PollRequest; }
        static void PollRequest()
        {
            if (running || Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(RequestPath)) return;
            string request;
            try { request = File.ReadAllText(RequestPath).Trim().ToLowerInvariant(); File.Delete(RequestPath); }
            catch (IOException) { return; }
            // Removal precedes execution so an assembly reload or failed operation cannot repeat it.
            EditorApplication.delayCall += () =>
            {
                if (running) return;
                running = true;
                try
                {
                    Directory.CreateDirectory(Evidence);
                    File.WriteAllText(Evidence + "/request-v2-status.txt", "RUNNING " + request + " " + DateTime.UtcNow.ToString("o"));
                    if (request == "verify") Verify();
                    else if (request == "build") Build();
                    else if (request == "both" || request == "verify-build") { Verify(); Build(); }
                    else throw new InvalidOperationException("Unknown workshop request. Use verify, build, or both.");
                    File.WriteAllText(Evidence + "/request-v2-status.txt", "SUCCESS " + request + " " + DateTime.UtcNow.ToString("o"));
                }
                catch (Exception error)
                {
                    File.WriteAllText(Evidence + "/request-v2-status.txt", "FAILED " + request + "\n" + error);
                    Debug.LogException(error);
                }
                finally { running = false; }
            };
        }

        [MenuItem("Tools/BoxLab/玩家工坊/验证玩家版完整数据流程", priority = 80)]
        public static void Verify()
        {
            Directory.CreateDirectory(Evidence);
            var results = new List<string> { "BOXLAB_PLAYER_VERIFICATION", "UTC " + DateTime.UtcNow.ToString("o"), "Unity " + Application.unityVersion };
            var project = AssetDatabase.LoadAssetAtPath<BoxProject>(ProjectSetup.ProjectPath);
            string originalProject = project ? JsonUtility.ToJson(project) : null;
            byte[] originalDisk = File.Exists(ProjectSetup.ProjectPath) ? File.ReadAllBytes(ProjectSetup.ProjectPath) : null;
            try
            {
                results.AddRange(RegressionTests.RunAll());
                results.AddRange(ChallengeLevelsVerification.RunAll());
                results.AddRange(CampaignLevelsVerification.RunAll());
                results.AddRange(WorkshopVerification.VerifyEditing());
                results.AddRange(PlayerEditingVerification.RunAll());
                results.AddRange(WorkshopVerification.VerifyStorage(Path.GetFullPath(Path.Combine(Evidence, "PlayerStorageChecks", Guid.NewGuid().ToString("N")))));
                var book = PlayerRules.CreateBook();
                foreach (var level in PlayerRules.CreateDemoLevels())
                {
                    var issues = PlayerRules.Validate(level);
                    if (LevelValidator.HasErrors(issues)) throw new InvalidOperationException(level.name + ": " + string.Join(" / ", issues.ConvertAll(x => x.message).ToArray()));
                    var solver = new SokobanSolver(level, book, 100000, 30000);
                    while (solver.Status == SolveStatus.Searching) solver.Step(500);
                    if (solver.Status != SolveStatus.Solved) throw new InvalidOperationException(level.name + ": " + solver.Message);
                    var state = RuleEngine.CreateState(level, book);
                    foreach (var direction in solver.Solution)
                    {
                        var step = RuleEngine.Step(level, book, state, direction);
                        if (step.interrupted) throw new InvalidOperationException(level.name + ": interrupted solution playback.");
                        state = step.state;
                    }
                    if (state.status != GameStatus.Won) throw new InvalidOperationException(level.name + ": solved path failed to win.");
                    results.Add("PASS 玩家教学 " + level.name + " / " + solver.Solution.Count + " 指令 / " + solver.Visited + " 局面");
                }
                if (project && JsonUtility.ToJson(project) != originalProject) throw new InvalidOperationException("Verification modified the legacy author's in-memory project.");
                if (originalDisk != null && Convert.ToBase64String(File.ReadAllBytes(ProjectSetup.ProjectPath)) != Convert.ToBase64String(originalDisk)) throw new InvalidOperationException("Verification modified the legacy author's asset on disk.");
                results.Add("PASS 旧版作者资产与用户关卡未被验收过程修改。");
                results.Add("PASS Unity JSON + isolated disk storage checks. Real standalone UI/animation checks require -boxlabWorkshopVerify.");
                results.Insert(0, "BOXLAB_PLAYER_VERIFICATION_SUCCESS");
                File.WriteAllLines(Evidence + "/player-verification.txt", results);
                Debug.Log("BOXLAB_PLAYER_VERIFICATION_SUCCESS\n" + string.Join("\n", results.ToArray()));
            }
            catch (Exception error)
            {
                results.Insert(0, "BOXLAB_PLAYER_VERIFICATION_FAILURE"); results.Add(error.ToString());
                File.WriteAllLines(Evidence + "/player-verification.txt", results); throw;
            }
        }

        [MenuItem("Tools/BoxLab/玩家工坊/构建 Windows 玩家版", priority = 81)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出试玩，再构建玩家版。");
            if (!File.Exists(ProjectSetup.ScenePath)) throw new FileNotFoundException("找不到入口场景，请先初始化 BoxLab 工程。", ProjectSetup.ScenePath);
            ArtSetup.Build();
            ArtAssetAudit.Run();
            PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
            Directory.CreateDirectory("Builds/Windows"); Directory.CreateDirectory(Evidence);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath }, locationPathName = "Builds/Windows/BoxLab.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            var lines = new List<string>
            {
                "V2 PLAYER BUILD", "UTC " + DateTime.UtcNow.ToString("o"), "Unity " + Application.unityVersion,
                "Result: " + report.summary.result, "Errors: " + report.summary.totalErrors, "Warnings: " + report.summary.totalWarnings,
                "Bytes: " + report.summary.totalSize, "Output: " + Path.GetFullPath("Builds/Windows/BoxLab.exe")
            };
            foreach (var step in report.steps) foreach (var message in step.messages)
                if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning) lines.Add(message.type + ": " + message.content);
            File.WriteAllLines(Evidence + "/player-build.txt", lines);
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                throw new InvalidOperationException("玩家版构建未达到 0 errors / 0 warnings，请查看 BoxLabEvidence/player-build.txt。");
            Debug.Log("BOXLAB_PLAYER_BUILD_SUCCESS " + report.summary.totalSize + " bytes");
        }

        public static void RunBatch()
        {
            try
            {
                Verify();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-boxlabBuild") >= 0) Build();
                Debug.Log("BOXLAB_PLAYER_BATCH_SUCCESS");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }
    }
}
#endif
