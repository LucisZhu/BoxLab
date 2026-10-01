#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BoxLab.Editor
{
    /// <summary>Exercises actual authoring commands on an unsaved temporary asset, with no UI dialogs.</summary>
    public static class AuthoringVerification
    {
        sealed class ViewState { public BoxProject project; public int levelIndex; }
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(BoxLabWindow window, string field, object value) => typeof(BoxLabWindow).GetField(field, Private).SetValue(window, value);
        static T Get<T>(BoxLabWindow window, string field) => (T)typeof(BoxLabWindow).GetField(field, Private).GetValue(window);
        static object Call(BoxLabWindow window, string name, params object[] args) => typeof(BoxLabWindow).GetMethod(name, Private).Invoke(window, args);
        static void Brush(BoxLabWindow window, string name)
        {
            var field = typeof(BoxLabWindow).GetField("brush", Private); field.SetValue(window, Enum.Parse(field.FieldType, name));
        }
        static void Click(BoxLabWindow window, int cell) => Call(window, "HandleCanvas", cell, Direction.North, false, false, false);
        static void Require(bool condition, string message) { if (!condition) throw new Exception("编辑器校验失败：" + message); }
        static string Envelope(LevelData level, RuleBookData book) => "{\"version\":1,\"level\":" + JsonUtility.ToJson(level) + ",\"book\":" + JsonUtility.ToJson(book) + "}";

        public static List<string> RunAll()
        {
            var results = new List<string>();
            var assets = Resources.FindObjectsOfTypeAll<BoxProject>().Where(EditorUtility.IsPersistent).ToDictionary(p => p, p => JsonUtility.ToJson(p));
            var views = UnityEngine.Object.FindObjectsOfType<BoardView>().ToDictionary(v => v, v => new ViewState { project = v.project, levelIndex = v.levelIndex });
            var project = ScriptableObject.CreateInstance<BoxProject>();
            project.hideFlags = HideFlags.HideAndDontSave;
            project.book = Presets.CreateBook(); project.levels = new List<LevelData>();
            BoxLabWindow window = null;
            try
            {
                window = ScriptableObject.CreateInstance<BoxLabWindow>();
                Set(window, "project", project); Set(window, "levelIndex", 0); Set(window, "previewQueued", false);
                Call(window, "NewLevel");
                Require(project.levels.Count == 1 && project.levels[0].cells.Count == 25, "新建地图未生成完整网格。");
                var level = project.levels[0];
                level.cells[6].ruleId = Presets.PortalEntrance; level.cells[6].portalPair = "test-pair";
                level.cells[8].ruleId = Presets.PortalExit; level.cells[8].portalPair = "test-pair";
                string entranceId = level.cells[6].id;
                level.cells[0].bindings.Add(new BindingData { key = "watch", cellIds = new List<string> { entranceId } });
                Brush(window, "MoveCell"); Click(window, 6); Click(window, 16);
                Require(level.FindCellIndex(entranceId) == 16 && level.cells[16].portalPair == "test-pair" && level.cells[0].bindings[0].cellIds[0] == entranceId, "移动传送端点丢失身份或关联。");
                Set(window, "resizeW", 7); Set(window, "resizeH", 6); Call(window, "Resize");
                Require(level.width == 7 && level.height == 6 && level.FindCellIndex(entranceId) == 22, "调整地图尺寸改变了格子的二维位置或身份。");
                results.Add("编辑器：新建、地块移动、传送关联和调整尺寸保留身份。");

                Call(window, "SetEdge", 1, Direction.East, EdgeKind.Wall, "");
                Call(window, "SetEdge", 2, Direction.West, EdgeKind.Gate, Presets.Gate);
                Require(level.edges.Count == 1 && level.FindEdge(1, Direction.East).kind == EdgeKind.Gate && ReferenceEquals(level.FindEdge(1, Direction.East), level.FindEdge(2, Direction.West)), "门没有覆写同一条公共边界。");
                results.Add("编辑器：墙到门的覆写保持唯一公共边界。");

                Undo.FlushUndoRecordObjects(); Undo.ClearUndo(project); Undo.IncrementCurrentGroup();
                Brush(window, "Player"); Click(window, 0); Undo.FlushUndoRecordObjects();
                Require(project.levels[0].player == 0, "人物笔刷没有生效。");
                Undo.PerformUndo();
                Require(project.levels[0].player == -1, "原生 Undo 未恢复人物笔刷修改。");
                results.Add("编辑器：实际笔刷修改可由 Unity Undo 整体恢复。");

                var ice = project.book.Find(Presets.Ice); int distance = ice.onEnter[0].actions[0].distance;
                Call(window, "DuplicateRule", ice); var copy = project.book.rules.Last(); copy.onEnter[0].actions[0].distance = distance + 2;
                Require(copy.id != ice.id && ice.onEnter[0].actions[0].distance == distance, "复制定义共享了可变条件或操作。");
                Call(window, "DuplicateLevel"); var duplicate = project.levels.Last(); duplicate.cells[0].portalPair = "independent";
                Require(duplicate.id != project.levels[0].id && project.levels[0].cells[0].portalPair != "independent", "复制关卡共享了可变格子。");
                results.Add("编辑器：复制关卡和规则后修改互不影响。");

                string beforeBadImport = JsonUtility.ToJson(project); bool rejected = false;
                try { Call(window, "ImportJson", "{\"version\":1,\"level\":null,\"book\":null}"); }
                catch (TargetInvocationException e) { rejected = e.InnerException != null; }
                Require(rejected && beforeBadImport == JsonUtility.ToJson(project), "损坏 JSON 在验证之前改变了工程。");
                var imported = LevelData.Create(5, 5, "验证导入"); imported.player = 0; imported.boxes.Add(1); imported.cells[2].ruleId = Presets.Goal;
                string originalLevel = JsonUtility.ToJson(project.levels[0]); int previousRules = project.book.rules.Count;
                Call(window, "ImportJson", Envelope(imported, Presets.CreateBook()));
                var importedLevel = project.levels.Last();
                Require(project.book.rules.Count > previousRules && importedLevel.cells[2].ruleId != Presets.Goal && project.book.Find(importedLevel.cells[2].ruleId).visual == TerrainVisual.Goal && originalLevel == JsonUtility.ToJson(project.levels[0]), "导入未重映射规则或修改了已有地图。");
                results.Add("编辑器：损坏导入无副作用，合法导入重映射规则且保留原有关卡。");

                var impossible = LevelData.Create(3, 3, "编译拒绝封闭目标"); impossible.player = 0; impossible.boxes.Add(1); impossible.cells[4].ruleId = Presets.Goal;
                project.levels = new List<LevelData> { impossible }; project.book = Presets.CreateBook(); project.selectedLevel = 0; Set(window, "levelIndex", 0);
                foreach (Direction direction in Enum.GetValues(typeof(Direction))) Call(window, "SetEdge", 4, direction, EdgeKind.Wall, "");
                Call(window, "CancelSearch"); Call(window, "Play");
                Require(Get<bool>(window, "playAfterSearch"), "编译并试玩未自动启动求解。");
                var solver = Get<SokobanSolver>(window, "solver");
                while (solver.Status == SolveStatus.Searching) solver.Step(100);
                Set(window, "previewQueued", false); Call(window, "Tick");
                Require(solver.Status == SolveStatus.Unsolvable && !Get<bool>(window, "playAfterSearch") && !EditorApplication.isPlayingOrWillChangePlaymode, "封闭目标无解关卡未阻止试玩。");
                results.Add("编辑器：编译自动求解，并阻止已证明无解的关卡进入 Play。");
            }
            finally
            {
                if (window) { Set(window, "previewQueued", false); Call(window, "CancelSearch"); UnityEngine.Object.DestroyImmediate(window); }
                Undo.ClearUndo(project); UnityEngine.Object.DestroyImmediate(project);
                foreach (var item in views)
                    if (item.Key && (item.Key.project != item.Value.project || item.Key.levelIndex != item.Value.levelIndex))
                    { item.Key.project = item.Value.project; item.Key.levelIndex = item.Value.levelIndex; item.Key.RefreshPreview(); }
            }
            foreach (var item in assets) Require(item.Key && JsonUtility.ToJson(item.Key) == item.Value, "校验意外修改了真实工程资产。");
            results.Add("编辑器：全部校验仅操作临时工程，真实资产保持不变。");
            return results;
        }
    }
}
#endif
