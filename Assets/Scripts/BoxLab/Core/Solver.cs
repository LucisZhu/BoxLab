using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BoxLab
{
    public enum SolveStatus { Searching, Solved, Unsolvable, Unknown, Cancelled, Invalid }

    /// <summary>Bounded BFS over the exact game transition function; driven incrementally by the editor.</summary>
    public sealed class SokobanSolver
    {
        private sealed class Node
        {
            public BoardState state;
            public int parent;
            public Direction direction;
        }
        private readonly LevelData level;
        private readonly RuleBookData book;
        private readonly int maxStates;
        private readonly double maxMilliseconds;
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private readonly List<Node> nodes = new List<Node>();
        private readonly Queue<int> queue = new Queue<int>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly Dictionary<string, int> ruleCodes = new Dictionary<string, int>();
        private bool interruptedBranch;
        public SolveStatus Status { get; private set; }
        public List<Direction> Solution { get; private set; } = new List<Direction>();
        public int Visited { get { return seen.Count; } }
        public int Frontier { get { return queue.Count; } }
        public double ElapsedMilliseconds { get { return timer.Elapsed.TotalMilliseconds; } }
        public string Message { get; private set; }

        public SokobanSolver(LevelData level, RuleBookData book, int maxStates = 100000, double maxMilliseconds = 5000)
        {
            this.level = level;
            this.book = book;
            this.maxStates = Math.Max(1, maxStates);
            this.maxMilliseconds = Math.Max(1, maxMilliseconds);
            var issues = LevelValidator.Validate(level, book);
            if (LevelValidator.HasErrors(issues))
            {
                Finish(SolveStatus.Invalid, "结构校验未通过，请先修正错误。");
                return;
            }
            for (int i = 0; i < book.rules.Count; i++) ruleCodes[book.rules[i].id] = i + 1;
            var state = RuleEngine.CreateState(level, book);
            nodes.Add(new Node { state = state, parent = -1 });
            queue.Enqueue(0);
            seen.Add(Key(state));
            Status = SolveStatus.Searching;
            Message = "正在按实际规则搜索……";
            if (IsSolved(state)) CompleteSolution(0);
        }

        public void Step(int expansionBudget = 100)
        {
            if (Status != SolveStatus.Searching) return;
            for (int n = 0; n < Math.Max(1, expansionBudget); n++)
            {
                if (timer.Elapsed.TotalMilliseconds >= maxMilliseconds)
                { Finish(SolveStatus.Unknown, "已达到时间上限，尚未验证可解性；这不表示无解。"); return; }
                if (queue.Count == 0)
                {
                    Finish(interruptedBranch ? SolveStatus.Unknown : SolveStatus.Unsolvable,
                        interruptedBranch ? "存在被规则循环保护中止的分支，无法证明无解。" : "已穷尽所有可达局面：当前关卡无解。");
                    return;
                }
                int parent = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var result = RuleEngine.Step(level, book, nodes[parent].state, (Direction)d, false);
                    if (result.interrupted) { interruptedBranch = true; continue; }
                    if (!result.changed || result.state.status == GameStatus.Lost) continue;
                    string key = Key(result.state);
                    if (seen.Contains(key)) continue;
                    if (seen.Count >= maxStates)
                    { Finish(SolveStatus.Unknown, "已达到局面数量上限，尚未验证可解性；可提高上限或手动试玩。"); return; }
                    seen.Add(key);
                    int index = nodes.Count;
                    nodes.Add(new Node { state = result.state, parent = parent, direction = (Direction)d });
                    if (result.state.status == GameStatus.Won || IsSolved(result.state)) { CompleteSolution(index); return; }
                    queue.Enqueue(index);
                }
            }
        }

        public void Cancel()
        {
            if (Status == SolveStatus.Searching) Finish(SolveStatus.Cancelled, "校验已取消，未作出可解性结论。");
        }

        private bool IsSolved(BoardState state)
        {
            return RuleEngine.GetStatus(level, book, state) == GameStatus.Won;
        }

        private string Key(BoardState state)
        {
            // Counts/time are deliberately absent; changed floor definitions are deliberately present.
            var sorted = new List<int>(state.boxes);
            sorted.Sort();
            var key = new StringBuilder(4 + sorted.Count + state.tiles.Length);
            key.Append((char)(state.player + 1));
            key.Append((char)sorted.Count);
            foreach (int box in sorted) key.Append((char)(box + 1));
            foreach (string rule in state.tiles)
            {
                int value;
                if (!ruleCodes.TryGetValue(rule ?? "", out value))
                { value = ruleCodes.Count + 1; ruleCodes[rule ?? ""] = value; }
                key.Append((char)value);
            }
            for (int i = 0; i < state.tiles.Length; i++) key.Append((char)RuleEngine.GetRotation(level, state, i));
            return key.ToString();
        }

        private void CompleteSolution(int index)
        {
            while (nodes[index].parent >= 0)
            {
                Solution.Add(nodes[index].direction);
                index = nodes[index].parent;
            }
            Solution.Reverse();
            Finish(SolveStatus.Solved, "已找到解：" + Solution.Count + " 次指令。已访问 " + Visited + " 个局面。");
        }

        private void Finish(SolveStatus status, string message)
        {
            Status = status;
            Message = message;
            timer.Stop();
            queue.Clear();
        }
    }
}
