using System;
using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>
    /// Conservative authoring analysis shared by the form and compiler. It follows possible
    /// ChangeTile replacements, not gameplay paths: every (cell, definition) pair is processed once.
    /// </summary>
    public static class RuleAnalysis
    {
        struct Candidate
        {
            public int cell;
            public RuleDefinition rule;
            public Candidate(int cell, RuleDefinition rule) { this.cell = cell; this.rule = rule; }
        }

        public static Dictionary<int, List<RuleDefinition>> PossibleDefinitions(LevelData level, RuleBookData book)
        {
            var result = new Dictionary<int, List<RuleDefinition>>();
            if (level == null || level.cells == null || book == null || book.rules == null) return result;
            var seen = new Dictionary<int, HashSet<string>>();
            var queue = new Queue<Candidate>();
            Action<int, RuleDefinition> offer = (cell, rule) =>
            {
                if (cell < 0 || cell >= level.cells.Count || level.cells[cell] == null || rule == null) return;
                HashSet<string> rules;
                if (!seen.TryGetValue(cell, out rules))
                {
                    seen[cell] = rules = new HashSet<string>(StringComparer.Ordinal);
                    result[cell] = new List<RuleDefinition>();
                }
                if (!rules.Add(rule.id ?? "")) return;
                result[cell].Add(rule); queue.Enqueue(new Candidate(cell, rule));
            };
            for (int cell = 0; cell < level.cells.Count; cell++)
                if (level.cells[cell] != null) offer(cell, book.Find(level.cells[cell].ruleId));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var action in Actions(current.rule))
                {
                    if (action.kind != ActionKind.ChangeTile) continue;
                    var replacement = book.Find(action.targetRuleId);
                    if (replacement == null) continue;
                    foreach (int target in RuleEngine.ResolveBinding(level, current.cell, level.cells[current.cell].bindings, action.targetBinding))
                        offer(target, replacement);
                }
            }
            return result;
        }

        public static List<string> BindingKeys(RuleDefinition rule)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (rule == null) return new List<string>();
            CollectConditions(rule.canEnter, keys);
            foreach (var branches in new[] { rule.onEnter, rule.onLeave })
            {
                if (branches == null) continue;
                foreach (var branch in branches)
                {
                    if (branch == null) continue;
                    CollectConditions(branch.condition, keys);
                    if (branch.actions == null) continue;
                    foreach (var action in branch.actions)
                        if (action != null && action.kind == ActionKind.ChangeTile && !string.IsNullOrWhiteSpace(action.targetBinding)) keys.Add(action.targetBinding);
                }
            }
            var result = new List<string>(keys); result.Sort(StringComparer.Ordinal); return result;
        }

        public static List<string> BindingKeysForInstance(LevelData level, RuleBookData book, int cell)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var possible = PossibleDefinitions(level, book);
            List<RuleDefinition> rules;
            if (possible.TryGetValue(cell, out rules))
                foreach (var rule in rules) foreach (string key in BindingKeys(rule)) keys.Add(key);
            var result = new List<string>(keys); result.Sort(StringComparer.Ordinal); return result;
        }

        static void CollectConditions(ConditionSet conditions, HashSet<string> keys)
        {
            if (conditions == null || conditions.nodes == null) return;
            foreach (var node in conditions.nodes)
                if (node != null && (node.kind == ConditionKind.Occupied || node.kind == ConditionKind.SignalCount || node.kind == ConditionKind.AllSignals) && !string.IsNullOrWhiteSpace(node.binding)) keys.Add(node.binding);
        }

        public static IEnumerable<RuleAction> Actions(RuleDefinition rule)
        {
            if (rule == null) yield break;
            foreach (var branches in new[] { rule.onEnter, rule.onLeave })
            {
                if (branches == null) continue;
                foreach (var branch in branches)
                {
                    if (branch == null || branch.actions == null) continue;
                    foreach (var action in branch.actions) if (action != null) yield return action;
                }
            }
        }
    }
}
