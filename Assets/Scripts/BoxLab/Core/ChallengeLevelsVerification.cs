using System;
using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>Uses the real transition function and exhaustive search, including disabled-mechanism controls.</summary>
    public static class ChallengeLevelsVerification
    {
        public static List<string> RunAll()
        {
            var results = new List<string>(); var levels = ChallengeLevels.Create();
            Require(levels.Count == 2, "组合关数量不符。");
            var ids = new HashSet<string>();
            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i]; var book = PlayerRules.CreateBook();
                Require(ids.Add(level.id), "组合关编号重复。");
                Require(!LevelValidator.HasErrors(PlayerRules.Validate(level)), level.name + "结构校验失败。");
                var solver = Solve(level, book);
                Require(solver.Status == SolveStatus.Solved, level.name + "未找到解：" + solver.Message);
                Require(solver.Solution.Count >= 15 && solver.Solution.Count <= 50, level.name + "不在目标长度范围内。");
                var state = RuleEngine.CreateState(level, book);
                foreach (Direction direction in solver.Solution)
                {
                    var step = RuleEngine.Step(level, book, state, direction);
                    Require(step.changed && !step.interrupted, level.name + "解法回放被阻挡或中断。");
                    state = step.state;
                }
                Require(state.status == GameStatus.Won, level.name + "回放未通关。");
                if (i == 0)
                    Require(state.boxes.Count == 2 && state.BoxAt(level.Index(2, 5)) >= 0 && state.BoxAt(level.Index(6, 5)) >= 0,
                        "运送关未保留额外箱子压板并覆盖目标。");
                results.Add("组合关：" + level.name + "，最短 " + solver.Solution.Count + " 指令，访问 " + solver.Visited + " 局面，实际规则回放通关。");
            }

            var disabledPlate = PlayerRules.CreateBook(); disabledPlate.Find(PlayerRules.Plate).signalActors = ActorMask.None;
            Require(Solve(levels[0], disabledPlate).Status == SolveStatus.Unsolvable, "压力板禁用后仍可通关，机关可能只是装饰。");
            results.Add("组合关：关闭压力板信号后穷尽无解，留守箱子与联动门为必要条件。");
            foreach (string mechanism in new[] { PlayerRules.PortalEntrance, PlayerRules.Ice, PlayerRules.Arrow })
            {
                var disabled = PlayerRules.CreateBook(); disabled.Find(mechanism).onEnter.Clear();
                Require(Solve(levels[1], disabled).Status == SolveStatus.Unsolvable, "接力关禁用 " + mechanism + " 后仍可通关。");
            }
            results.Add("组合关：分别禁用传送、冰面或箭头后均穷尽无解，三种机关都参与必要运输路线。");
            return results;
        }

        private static SokobanSolver Solve(LevelData level, RuleBookData book)
        {
            var solver = new SokobanSolver(level, book, 60000, 10000);
            while (solver.Status == SolveStatus.Searching) solver.Step(400);
            return solver;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("组合关验收失败：" + message); }
    }
}
