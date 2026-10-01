using System;
using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>
    /// Fast acceptance of authored solutions and mechanism sensitivity. The expensive original
    /// exhaustive searches are recorded in Checks/CampaignDesignVerification.txt, not repeated here.
    /// </summary>
    public static class CampaignLevelsVerification
    {
        public static List<string> RunAll()
        {
            var results = new List<string>(); var levels = PlayerRules.CreateCampaignLevels();
            Require(levels.Count == 6, "应有六张正式关卡。");
            var ids = new HashSet<string>();
            foreach (var tutorial in PlayerRules.CreateDemoLevels()) ids.Add(tutorial.id);
            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i]; var book = PlayerRules.CreateBook();
                Require(ids.Add(level.id), "正式关卡与其他关卡编号重复。");
                Require(!LevelValidator.HasErrors(PlayerRules.Validate(level)), level.name + "结构无效。");
                int goals = 0; foreach (var cell in level.cells) if (cell.ruleId == PlayerRules.Goal) goals++;
                Require(i < 3 ? goals == 2 && level.boxes.Count == (i == 2 ? 3 : 2)
                    : goals == 3 && level.boxes.Count == (i == 3 ? 3 : 4), "正式关卡应保持多箱、多目标配置。");
                var solution = CampaignLevels.GetVerifiedSolution(level.id);
                Require(solution.Count >= 15, level.name + "退化为过短演示。");
                var state = RuleEngine.CreateState(level, book);
                int rotationChanges = 0, teleports = 0;
                foreach (Direction direction in solution)
                {
                    var step = RuleEngine.Step(level, book, state, direction);
                    Require(step.changed && !step.interrupted, level.name + "验证解出现受阻指令或规则循环。");
                    for (int c = 0; c < state.rotations.Length; c++)
                        if (state.rotations[c] != step.state.rotations[c]) rotationChanges++;
                    // The complete route may end near its entrance. Observe the real intermediate
                    // entrance-to-exit frame instead of inferring teleportation from net distance.
                    var previous = state;
                    foreach (var frame in step.frames)
                    {
                        for (int b = 0; b < previous.boxes.Count; b++)
                        {
                            int from = previous.boxes[b], to = frame.boxes[b];
                            if (from >= 0 && to >= 0 && from != to &&
                                level.cells[from].ruleId == PlayerRules.PortalEntrance &&
                                level.cells[to].ruleId == PlayerRules.PortalExit &&
                                level.cells[from].portalPair == level.cells[to].portalPair) teleports++;
                        }
                        previous = frame;
                    }
                    state = step.state;
                    if (i == 0 && state.BoxAt(level.Index(2, 5)) >= 0)
                        Require(state.BoxAt(level.Index(6, 3)) >= 0, "岗位箱子提前归位，未执行必要的目标顺序。");
                }
                Require(state.status == GameStatus.Won && RuleEngine.GetStatus(level, book, state) == GameStatus.Won,
                    level.name + "实际规则回放未覆盖所有目标。");
                if (i == 1 || i == 2 || i == 5) Require(rotationChanges >= 3 && teleports >= 2, "两批货物未实际使用改向和传送。");
                if (i == 2) Require(state.BoxAt(level.Index(2, 4)) >= 0, "额外箱子未留在压力板上维持运输门。");
                if (i == 4) Require(teleports >= 1 && state.tiles[level.Index(1, 3)] == PlayerRules.Hole, "裂桥和最后一趟传送没有实际发生。");
                if (i == 5) Require(state.BoxAt(level.Index(1, 6)) >= 0, "压轴关的留守箱未维持远端岗位。");
                results.Add("正式关卡：" + level.name + "，" + solution.Count + " 指令实际规则回放通关，箱子分工和机关变化符合设计。");
            }

            var disabled = PlayerRules.CreateBook(); disabled.Find(PlayerRules.Plate).signalActors = ActorMask.None;
            Require(!ReplayWins(levels[0], disabled), "第一关验证解不再需要压板开门。");
            foreach (string mechanism in new[] { PlayerRules.RotationPlate, PlayerRules.PortalEntrance, PlayerRules.Ice })
            {
                disabled = PlayerRules.CreateBook();
                if (mechanism == PlayerRules.RotationPlate) disabled.Find(mechanism).signalActors = ActorMask.None;
                else disabled.Find(mechanism).onEnter.Clear();
                Require(!ReplayWins(levels[1], disabled), "第二关验证解不再需要 " + mechanism + "。");
            }
            foreach (string mechanism in new[] { PlayerRules.Plate, PlayerRules.RotationPlate })
            {
                disabled = PlayerRules.CreateBook(); disabled.Find(mechanism).signalActors = ActorMask.None;
                Require(!ReplayWins(levels[2], disabled), "第三关验证解不再需要 " + mechanism + "。");
            }
            foreach (int index in new[] { 3, 4, 5 })
            {
                disabled = PlayerRules.CreateBook(); disabled.Find(PlayerRules.Plate).signalActors = ActorMask.None;
                Require(!ReplayWins(levels[index], disabled), levels[index].name + "验证解不再需要压力门。");
            }
            foreach (string mechanism in new[] { PlayerRules.RotationPlate, PlayerRules.PortalEntrance, PlayerRules.Arrow })
            {
                disabled = PlayerRules.CreateBook();
                if (mechanism == PlayerRules.RotationPlate) disabled.Find(mechanism).signalActors = ActorMask.None;
                else disabled.Find(mechanism).onEnter.Clear();
                Require(!ReplayWins(levels[5], disabled), "第六关验证解不再需要 " + mechanism + "。");
            }
            results.Add("正式关卡：关键机关禁用后原验证解均失效；完整无解对照另见设计阶段穷举记录。");
            return results;
        }

        private static bool ReplayWins(LevelData level, RuleBookData book)
        {
            var state = RuleEngine.CreateState(level, book);
            foreach (Direction direction in CampaignLevels.GetVerifiedSolution(level.id))
            {
                var step = RuleEngine.Step(level, book, state, direction, false);
                if (!step.changed || step.interrupted) return false;
                state = step.state;
            }
            return state.status == GameStatus.Won;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("正式关卡验收失败：" + message); }
    }
}
