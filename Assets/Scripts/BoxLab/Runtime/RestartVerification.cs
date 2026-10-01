using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    /// <summary>
    /// Synchronous controller/history checks for the isolated acceptance run. Call while the
    /// app is on its home page: this temporarily loads fixtures into the supplied controller.
    /// </summary>
    public static class RestartVerification
    {
        public static List<string> RunAll(GameController controller, string isolatedRoot)
        {
            if (!controller) throw new ArgumentNullException(nameof(controller));
            if (string.IsNullOrWhiteSpace(isolatedRoot)) throw new ArgumentException("重开验收必须提供隔离存档目录。", nameof(isolatedRoot));
            var results = new List<string>();
            var book = PlayerRules.CreateBook();

            var level = LevelData.Create(5, 5, "验收 · 重开与计数");
            level.player = level.Index(1, 1); DemoLevels.Box(level, 2, 2); DemoLevels.Tile(level, 4, 4, PlayerRules.Goal);
            var initial = RuleEngine.CreateState(level, book);
            controller.Load(level, book); controller.Restart(); controller.Restart();
            Require(Exact(controller.State, initial) && controller.ExportHistory().Count == 0, "初始局面重开产生了无意义历史。");
            var history = new List<BoardState>(); var returned = initial.Clone();
            returned = Advance(level, book, returned, Direction.North, history);
            returned = Advance(level, book, returned, Direction.South, history);
            Require(returned.GetKey() == initial.GetKey() && returned.moves == 2, "计数验收未回到相同棋盘。");
            controller.Load(level, book, returned, history); controller.Restart();
            Require(Exact(controller.State, initial) && controller.ExportHistory().Count == 3, "回到相同棋盘但有步数时，重开未记录。");
            controller.Restart(); Require(controller.ExportHistory().Count == 3, "连续重开重复记录了初始局面。");
            controller.Undo(); Require(Exact(controller.State, returned), "撤销重开没有恢复原步数。");
            controller.Undo(); Require(Exact(controller.State, history[1]), "重开清除了更早的操作历史。");
            results.Add("重开：初始重复重开不增历史；同棋盘不同步数仍可撤销，之前的操作历史保持。");

            level = LevelData.Create(5, 5, "验收 · 重开动态局面");
            level.player = level.Index(1, 1); DemoLevels.Box(level, 2, 1);
            DemoLevels.Tile(level, 2, 1, PlayerRules.Fragile); DemoLevels.Tile(level, 4, 1, PlayerRules.Goal);
            DemoLevels.Tile(level, 1, 0, PlayerRules.RotationPlate); DemoLevels.Tile(level, 3, 3, PlayerRules.Arrow);
            level.cells[level.Index(3, 3)].bindings.Add(DemoLevels.Binding(level, PlayerRules.Switches, level.Index(1, 0)));
            initial = RuleEngine.CreateState(level, book); history = new List<BoardState>(); var progressed = initial.Clone();
            progressed = Advance(level, book, progressed, Direction.South, history);
            progressed = Advance(level, book, progressed, Direction.North, history);
            progressed = Advance(level, book, progressed, Direction.East, history);
            Require(progressed.tiles[level.Index(2, 1)] == PlayerRules.Hole && progressed.rotations[level.Index(3, 3)] == 1
                && progressed.player == initial.player && progressed.pushes == 1, "动态验收未产生坍塌、朝向和箱子变化。");
            controller.Load(level, book, progressed, history); controller.Restart();
            Require(Exact(controller.State, initial), "重开未恢复初始地形与朝向。");
            var exported = controller.ExportHistory();
            Require(exported.Count == history.Count + 1 && Exact(exported[exported.Count - 1], progressed), "重开前完整快照未追加到已有历史。");
            exported[exported.Count - 1].tiles[0] = "corrupted-export-copy";
            controller.Undo(); Require(Exact(controller.State, progressed), "撤销重开丢失动态局面或导出快照影响了内部记录。");
            controller.Restart();
            results.Add("重开：撤销精确恢复箱子、坍塌地形、旋转箭头和计数；历史导出是独立副本。");

            var storage = new PlayerStorage(isolatedRoot);
            var document = new MapDocument { level = level.Clone() }; storage.SaveMap(document);
            storage.SaveProgress(new PlaySave
            {
                mapId = document.id, mapHash = PlayerStorage.Fingerprint(level), level = level.Clone(),
                state = controller.State.Clone(), history = controller.ExportHistory()
            });
            var saved = new PlayerStorage(isolatedRoot).LoadProgress();
            Require(saved != null && Exact(saved.state, initial) && saved.history.Count == history.Count + 1, "重开后的存档没有保留完整撤销记录。");
            controller.Load(saved.level, book, saved.state, saved.history); controller.Undo();
            Require(Exact(controller.State, progressed), "重新读取存档后无法撤销重开。");
            controller.Undo(); Require(Exact(controller.State, history[history.Count - 1]), "读档后撤销重开打断了更早历史。");
            results.Add("重开：真实磁盘保存并用新存储实例读取后，仍能撤销重开及之前的推动。");

            foreach (bool lost in new[] { false, true })
            {
                level = LevelData.Create(5, 5, lost ? "验收 · 失败后重开" : "验收 · 通关后重开");
                level.player = level.Index(1, 2); DemoLevels.Box(level, 2, 2);
                DemoLevels.Tile(level, lost ? 4 : 3, lost ? 4 : 2, PlayerRules.Goal);
                if (lost) DemoLevels.Tile(level, 3, 2, PlayerRules.Hole);
                initial = RuleEngine.CreateState(level, book); history = new List<BoardState>();
                var terminal = Advance(level, book, initial, Direction.East, history);
                Require(terminal.status == (lost ? GameStatus.Lost : GameStatus.Won), "终局验收没有形成预期结果。");
                controller.Load(level, book, terminal, history); controller.Restart();
                Require(Exact(controller.State, initial), "终局重开没有恢复初始状态。");
                controller.Undo(); Require(Exact(controller.State, terminal), "撤销终局重开未恢复通关或箱子掉落状态。");
                controller.Undo(); Require(Exact(controller.State, initial), "终局重开后不能继续撤销之前的推动。");
            }
            results.Add("重开：通关和失败后都能重开，再撤销回原终局，继续撤销原操作。");
            return results;
        }

        private static BoardState Advance(LevelData level, RuleBookData book, BoardState before, Direction direction, List<BoardState> history)
        {
            var step = RuleEngine.Step(level, book, before, direction);
            Require(step.changed && !step.interrupted, "验收指令未成功执行。");
            history.Add(before.Clone()); return step.state;
        }
        private static bool Exact(BoardState a, BoardState b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("重开验收失败：" + message); }
    }
}
