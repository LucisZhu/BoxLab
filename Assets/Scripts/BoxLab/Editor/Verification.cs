#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BoxLab.Editor
{
    public static class Verification
    {
        [MenuItem("Tools/BoxLab/验证全部逻辑与示例", priority = 30)]
        public static void RunAll()
        {
            var results = RegressionTests.RunAll();
            results.AddRange(AuthoringVerification.RunAll());
            var project = ProjectSetup.EnsureProject();
            string serialized = JsonUtility.ToJson(project);
            var restored = ScriptableObject.CreateInstance<BoxProject>();
            try
            {
                JsonUtility.FromJsonOverwrite(serialized, restored);
                if (restored.levels.Count != project.levels.Count || restored.book.rules.Count != project.book.rules.Count)
                    throw new Exception("规则与关卡序列化往返失败。");
                foreach (var level in restored.levels)
                {
                    var issues = LevelValidator.Validate(level, restored.book);
                    if (LevelValidator.HasErrors(issues)) throw new Exception(level.name + " 校验失败：" + string.Join("; ", issues));
                    var solver = new SokobanSolver(level, restored.book, 250000, 15000);
                    while (solver.Status == SolveStatus.Searching) solver.Step(200);
                    if (solver.Status != SolveStatus.Solved) throw new Exception(level.name + " 未验证可解：" + solver.Message);
                    var state = RuleEngine.CreateState(level, restored.book);
                    foreach (var direction in solver.Solution)
                    {
                        var step = RuleEngine.Step(level, restored.book, state, direction);
                        if (!step.changed || step.interrupted) throw new Exception(level.name + " 解法回放与求解器不一致。");
                        state = step.state;
                    }
                    if (state.status != GameStatus.Won) throw new Exception(level.name + " 解法未到达通关状态。");
                    results.Add("PASS 示例可解并回放通关：" + level.name + " / " + solver.Solution.Count + " 指令 / " + solver.Visited + " 局面");
                }
                var custom = restored.book.Find(Presets.Ice).Clone();
                custom.id = Guid.NewGuid().ToString("N"); custom.name = "验证：新定义"; custom.builtIn = false;
                custom.onEnter[0].condition = ConditionSet.Actor(ActorMask.Player);
                custom.onEnter[0].actions[0].directionMode = DirectionMode.Fixed;
                custom.onEnter[0].actions[0].direction = Direction.North;
                custom.onEnter[0].actions[0].distance = 1;
                restored.book.rules.Add(custom);
                var test = LevelData.Create(5, 5, "新规则配置验证");
                test.player = test.Index(1, 1); test.cells[test.Index(2, 1)].ruleId = custom.id;
                test.boxes.Add(test.Index(3, 3)); test.cells[test.Index(4, 3)].ruleId = Presets.Goal;
                var moved = RuleEngine.Step(test, restored.book, RuleEngine.CreateState(test), Direction.East);
                if (moved.state.player != test.Index(2, 2)) throw new Exception("新规则的作用对象和操作组合未生效。");
                if (restored.book.Find(Presets.Ice).onEnter[0].actions[0].distance != 3) throw new Exception("复制规则意外修改了原定义。");
                results.Add("PASS 自定义新规则：复制/改条件对象/改方向操作/应用地图，旧预设保持原样");
                results.Add("PASS JsonUtility 全规则树和稳定引用往返");
            }
            finally { UnityEngine.Object.DestroyImmediate(restored); }
            Directory.CreateDirectory("BoxLabEvidence");
            File.WriteAllLines("BoxLabEvidence/verification.txt", results);
            Debug.Log("BoxLab verification: " + results.Count + " checks passed.\n" + string.Join("\n", results));
        }

        [MenuItem("Tools/BoxLab/构建 Windows 验证版", priority = 40)]
        public static void BuildWindows()
        {
            if (!File.Exists(ProjectSetup.ScenePath)) ProjectSetup.CreateScene(ProjectSetup.EnsureProject());
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = "Builds/Windows/BoxLab.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Directory.CreateDirectory("BoxLabEvidence");
            File.WriteAllText("BoxLabEvidence/build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0) throw new Exception("Windows 构建未成功，请查看 Console / Editor.log。");
            Debug.Log("BOXLAB_BUILD_SUCCESS: " + report.summary.totalSize + " bytes");
        }

        [MenuItem("Tools/BoxLab/导出场景预览截图", priority = 50)]
        public static void CapturePreviews()
        {
            var view = UnityEngine.Object.FindObjectOfType<BoardView>();
            if (!view || !view.project) throw new Exception("请先打开 BoxLab 场景。");
            Directory.CreateDirectory("BoxLabEvidence");
            int old = view.levelIndex;
            try
            {
                for (int i = 0; i < view.project.levels.Count; i++)
                {
                    view.levelIndex = i; view.RefreshPreview();
                    var camera = view.boardCamera;
                    var oldRect = camera.rect;
                    var oldTarget = camera.targetTexture;
                    var previous = RenderTexture.active;
                    var target = new RenderTexture(1200, 800, 24);
                    var texture = new Texture2D(1200, 800, TextureFormat.RGB24, false);
                    try
                    {
                        camera.rect = new Rect(0, 0, 1, 1); camera.targetTexture = target; view.FitCamera(); camera.Render();
                        RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); texture.Apply();
                        File.WriteAllBytes("BoxLabEvidence/preview-" + i.ToString("00") + ".png", texture.EncodeToPNG());
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget; camera.rect = oldRect; RenderTexture.active = previous;
                        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
                    }
                }
            }
            finally { view.levelIndex = old; view.RefreshPreview(); }
        }
    }
}
#endif
