using System;
using System.Collections.Generic;

namespace BoxLab
{
    public enum ValidationSeverity { Warning, Error }
    [Serializable]
    public class ValidationIssue
    {
        public ValidationSeverity severity;
        public string message;
        public int cellIndex = -1;
        // Diagnostic location only. -1 means a cell/global issue; 0..3 identifies its public edge.
        public int edgeDirection = -1;
        public string ruleId = "";
        public override string ToString() { return severity + ": " + message; }
    }

    public static class LevelValidator
    {
        public static bool HasErrors(IEnumerable<ValidationIssue> issues)
        {
            if (issues != null) foreach (var issue in issues) if (issue.severity == ValidationSeverity.Error) return true;
            return false;
        }

        public static List<ValidationIssue> Validate(LevelData level, RuleBookData book)
        {
            var issues = new List<ValidationIssue>();
            if (level == null || book == null || book.rules == null)
            { Add(issues, "缺少关卡或规则库。"); return issues; }
            if (level.width < 1 || level.height < 1 || level.width > 32 || level.height > 32)
            { Add(issues, "地图宽高必须在 1～32 格之间。"); return issues; }
            if (level.cells == null || level.cells.Count != level.width * level.height)
            { Add(issues, "格子数量与地图尺寸不一致，请修复或重新导入。"); return issues; }
            var ruleIds = new HashSet<string>();
            foreach (var rule in book.rules)
            {
                if (rule == null) { Add(issues, "规则库包含空定义。"); continue; }
                if (string.IsNullOrWhiteSpace(rule.id) || !ruleIds.Add(rule.id)) Add(issues, "规则 ID 为空或重复：" + rule.name, -1, rule.id);
                ValidateDefinition(rule, book, issues);
            }
            var cellIds = new HashSet<string>();
            var entrances = new Dictionary<string, List<int>>();
            var exits = new Dictionary<string, List<int>>();
            int goals = 0;
            for (int i = 0; i < level.cells.Count; i++)
            {
                var cell = level.cells[i];
                if (cell == null) { Add(issues, "缺少格子数据。", i); continue; }
                if (string.IsNullOrWhiteSpace(cell.id) || !cellIds.Add(cell.id)) Add(issues, "格子 ID 为空或重复，会破坏关联关系。", i);
                var rule = book.Find(cell.ruleId);
                if (rule == null) { Add(issues, "地块引用了不存在的规则：" + cell.ruleId, i, cell.ruleId); continue; }
                if (rule.boundary) Add(issues, "边界规则不能直接作为地面。", i, rule.id);
                if (rule.visual == TerrainVisual.Goal) goals++;
                ValidateBindings(level, book, rule, cell.bindings, issues, i);
                if (rule.visual == TerrainVisual.PortalEntrance || rule.visual == TerrainVisual.PortalExit)
                {
                    if (string.IsNullOrWhiteSpace(cell.portalPair)) Add(issues, "传送门尚未配对：请设置同组入口和出口。", i, rule.id);
                    else
                    {
                        var collection = rule.visual == TerrainVisual.PortalEntrance ? entrances : exits;
                        List<int> values;
                        if (!collection.TryGetValue(cell.portalPair, out values)) collection[cell.portalPair] = values = new List<int>();
                        values.Add(i);
                    }
                }
            }
            // A ChangeTile replacement keeps the target cell's identity and bindings. Validate
            // the same possible definitions that the inspector exposes, including remote chains.
            var possible = RuleAnalysis.PossibleDefinitions(level, book);
            foreach (var entry in possible)
            {
                var cell = level.GetCell(entry.Key);
                if (cell == null) continue;
                foreach (var definition in entry.Value)
                    if (definition.id != cell.ruleId) ValidateBindings(level, book, definition, cell.bindings, issues, entry.Key, false);
            }
            if (!level.Contains(level.player)) Add(issues, "必须放置一个人物。", level.player);
            else CheckInitialActor(level, book, issues, level.player, ActorMask.Player);
            var occupied = new HashSet<int>();
            if (level.boxes == null || level.boxes.Count == 0) Add(issues, "必须至少放置一个箱子。");
            else foreach (int box in level.boxes)
            {
                if (!level.Contains(box)) Add(issues, "箱子在地图范围之外。", box);
                else
                {
                    if (!occupied.Add(box)) Add(issues, "同一格放置了多个箱子。", box);
                    if (box == level.player) Add(issues, "人物与箱子不能重叠。", box);
                    CheckInitialActor(level, book, issues, box, ActorMask.Box);
                }
            }
            if (goals == 0) Add(issues, "必须至少放置一个目标地块。");
            if (level.boxes != null && level.boxes.Count < goals) Add(issues, "箱子数（" + level.boxes.Count + "）不能少于目标数（" + goals + "）。");
            var pairs = new HashSet<string>(entrances.Keys); pairs.UnionWith(exits.Keys);
            foreach (string pair in pairs)
            {
                List<int> starts, ends;
                entrances.TryGetValue(pair, out starts); exits.TryGetValue(pair, out ends);
                if (starts == null || starts.Count != 1 || ends == null || ends.Count != 1)
                {
                    var affected = new List<int>(); if (starts != null) affected.AddRange(starts); if (ends != null) affected.AddRange(ends);
                    foreach (int at in affected) Add(issues, "传送组「" + pair + "」必须恰好有一个入口和一个出口。", at);
                }
            }
            var edgeIds = new HashSet<string>();
            if (level.edges != null) foreach (var edge in level.edges)
            {
                if (edge == null) { Add(issues, "存在空边界数据。"); continue; }
                if (!level.Contains(edge.cell) || (int)edge.direction < 0 || (int)edge.direction > 3)
                { Add(issues, "边界位置或方向无效。", edge.cell); continue; }
                int firstEdgeIssue = issues.Count;
                if (!Enum.IsDefined(typeof(EdgeKind), edge.kind)) Add(issues, "边界类型无效。", edge.cell);
                int cell = edge.cell; Direction direction = edge.direction; level.NormalizeEdge(ref cell, ref direction);
                if (!edgeIds.Add(cell + ":" + direction)) Add(issues, "同一条公共边界被定义了两次。", cell);
                if (edge.kind == EdgeKind.Gate)
                {
                    if (level.Neighbor(edge.cell, edge.direction) < 0) Add(issues, "门必须连接地图内的两个格子，不能通往地图外。", edge.cell);
                    var rule = book.Find(edge.ruleId);
                    if (rule == null || !rule.boundary) Add(issues, "门没有引用有效的边界规则。", edge.cell, edge.ruleId);
                    else ValidateBindings(level, book, rule, edge.bindings, issues, edge.cell);
                }
                for (int i = firstEdgeIssue; i < issues.Count; i++)
                { issues[i].cellIndex = cell; issues[i].edgeDirection = (int)direction; }
            }
            // No classic corner/dead-square rejection: user-authored teleport/turn/change-floor rules can invalidate it.
            return issues;
        }

        private static void CheckInitialActor(LevelData level, RuleBookData book, List<ValidationIssue> issues, int at, ActorMask actor)
        {
            var cell = level.GetCell(at); if (cell == null) return;
            var rule = book.Find(cell.ruleId); if (rule == null) return;
            if (rule.visual == TerrainVisual.Hole) Add(issues, "初始对象不能放在空洞里。请在试玩中验证掉落行为。", at, rule.id);
            else if ((rule.allowedActors & actor) == 0) Add(issues, "初始对象类型不符合该地块的进入限制。", at, rule.id);
        }

        private static void ValidateDefinition(RuleDefinition rule, RuleBookData book, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(rule.name)) Add(issues, "规则名称不能为空。", -1, rule.id);
            if (!Enum.IsDefined(typeof(TerrainVisual), rule.visual)) Add(issues, "地块表现类型无效。", -1, rule.id);
            if (((int)rule.allowedActors & ~3) != 0 || ((int)rule.signalActors & ~3) != 0) Add(issues, "规则的对象类型无效。", -1, rule.id);
            ValidateCondition(rule.canEnter, issues, rule.id, "进入条件");
            ValidateBranches(rule.onEnter, false, rule, book, issues);
            ValidateBranches(rule.onLeave, true, rule, book, issues);
            if (rule.boundary && ((rule.onEnter != null && rule.onEnter.Count > 0) || (rule.onLeave != null && rule.onLeave.Count > 0)))
                Add(issues, "边界规则只判断通行，不能配置地块进入/离开效果。", -1, rule.id);
            if (rule.visual == TerrainVisual.Goal && (rule.signalActors != ActorMask.None ||
                (rule.onEnter != null && rule.onEnter.Count > 0) || (rule.onLeave != null && rule.onLeave.Count > 0)))
                Add(issues, "目标地块不能叠加机关效果或压力板信号。", -1, rule.id);
        }

        private static void ValidateBranches(List<RuleBranch> branches, bool leave, RuleDefinition rule, RuleBookData book, List<ValidationIssue> issues)
        {
            if (branches == null) return;
            if (branches.Count > 32) Add(issues, "单次触发最多支持 32 个条件分支。", -1, rule.id);
            for (int b = 0; b < branches.Count; b++)
            {
                var branch = branches[b];
                if (branch == null) { Add(issues, "条件分支不能为空。", -1, rule.id); continue; }
                ValidateCondition(branch.condition, issues, rule.id, (leave ? "离开" : "进入") + "分支 " + (b + 1));
                if (branch.actions == null) continue;
                int movements = 0;
                foreach (var action in branch.actions)
                {
                    if (action == null) { Add(issues, "操作不能为空。", -1, rule.id); continue; }
                    if (!Enum.IsDefined(typeof(ActionKind), action.kind)) Add(issues, "未知操作类型。", -1, rule.id);
                    if (leave && action.kind != ActionKind.ChangeTile) Add(issues, "离开事件只允许改变地块，避免重复移动或移除离开的对象。", -1, rule.id);
                    if (action.kind == ActionKind.Move || action.kind == ActionKind.Teleport) movements++;
                    if (action.kind == ActionKind.Move)
                    {
                        if (action.distance < 1 || action.distance > 64) Add(issues, "额外移动格数必须在 1～64 之间。", -1, rule.id);
                        if (!Enum.IsDefined(typeof(DirectionMode), action.directionMode)) Add(issues, "移动操作的方向来源无效。", -1, rule.id);
                        if (!Enum.IsDefined(typeof(Direction), action.direction)) Add(issues, "移动操作的方向无效。", -1, rule.id);
                    }
                    if (!string.IsNullOrEmpty(action.targetBinding) && string.IsNullOrWhiteSpace(action.targetBinding))
                        Add(issues, "操作的目标关联键不能只包含空白；留空表示当前触发格。", -1, rule.id);
                    if (action.kind == ActionKind.ChangeTile)
                    {
                        var target = book.Find(action.targetRuleId);
                        if (target == null || target.boundary) Add(issues, "改变地块操作需要有效的地面规则。", -1, rule.id);
                        else if (target.visual == TerrainVisual.Goal || target.visual == TerrainVisual.PortalEntrance || target.visual == TerrainVisual.PortalExit)
                            Add(issues, "运行中不能创建目标或传送端点；请在地图编辑阶段放置。", -1, rule.id);
                    }
                    if (action.kind == ActionKind.Teleport)
                    {
                        if (rule.visual != TerrainVisual.PortalEntrance) Add(issues, "传送操作需要入口类型，以便校验与出口成对。", -1, rule.id);
                        if (!string.IsNullOrEmpty(action.targetBinding)) Add(issues, "传送仅使用配对出口，不能保留额外的目标关联键；请清空该字段或重新添加传送操作。", -1, rule.id);
                    }
                }
                if (movements > 1) Add(issues, "同一分支只能指定一个移动/传送效果；请使用条件分支选择效果。", -1, rule.id);
            }
        }

        private static void ValidateCondition(ConditionSet condition, List<ValidationIssue> issues, string rule, string label)
        {
            if (condition == null || condition.nodes == null || condition.nodes.Count == 0)
            { Add(issues, label + "缺少条件根节点。", -1, rule); return; }
            if (condition.nodes.Count > 128) Add(issues, label + "超过 128 个条件节点。", -1, rule);
            for (int i = 0; i < condition.nodes.Count; i++)
            {
                var node = condition.nodes[i];
                if (node == null) { Add(issues, label + "含有空节点。", -1, rule); continue; }
                if (!Enum.IsDefined(typeof(ConditionKind), node.kind)) Add(issues, label + "含有未知判断。", -1, rule);
                if (((int)node.actorMask & ~3) != 0) Add(issues, label + "的对象类型无效。", -1, rule);
                if (!string.IsNullOrEmpty(node.binding) && string.IsNullOrWhiteSpace(node.binding)) Add(issues, label + "的关联键不能只包含空白。", -1, rule);
                if (i == 0 && node.parent != -1) Add(issues, label + "根节点不能有父节点。", -1, rule);
                if (i > 0)
                {
                    if (node.parent < 0 || node.parent >= i) Add(issues, label + "父节点必须是前面的条件组，不能循环引用。", -1, rule);
                    else
                    {
                        var parent = condition.nodes[node.parent];
                        if (parent == null || !IsGroup(parent.kind)) Add(issues, label + "子条件必须属于全部/任意/数量条件组。", -1, rule);
                    }
                }
                if ((node.kind == ConditionKind.SignalCount || node.kind == ConditionKind.Occupied || node.kind == ConditionKind.AtLeast) && node.threshold < 1)
                    Add(issues, label + "满足数量至少为 1。", -1, rule);
                int ancestor = i, depth = 0;
                while (ancestor > 0 && condition.nodes[ancestor] != null && condition.nodes[ancestor].parent >= 0 && condition.nodes[ancestor].parent < ancestor)
                { ancestor = condition.nodes[ancestor].parent; depth++; }
                if (depth > 64) Add(issues, label + "嵌套超过 64 层，超出运行内核支持范围。", -1, rule);
                if (IsGroup(node.kind))
                {
                    int count = 0; foreach (var other in condition.nodes) if (other != null && other.parent == i) count++;
                    if (count == 0) Add(issues, label + "存在没有子条件的条件组。", -1, rule);
                    if (node.kind == ConditionKind.AtLeast && node.threshold > count) Add(issues, label + "要求的满足数量超过子条件数量。", -1, rule);
                }
            }
        }

        private static bool IsGroup(ConditionKind kind) { return kind == ConditionKind.All || kind == ConditionKind.Any || kind == ConditionKind.AtLeast; }

        private static void ValidateBindings(LevelData level, RuleBookData book, RuleDefinition rule, List<BindingData> bindings, List<ValidationIssue> issues, int cell, bool validateRawBindings = true)
        {
            var keys = new HashSet<string>();
            if (validateRawBindings && bindings != null) foreach (var binding in bindings)
            {
                if (binding == null) { Add(issues, "存在空的实例关联。", cell, rule.id); continue; }
                if (string.IsNullOrWhiteSpace(binding.key) || !keys.Add(binding.key)) Add(issues, "关联名称为空或重复。", cell, rule.id);
                var ids = new HashSet<string>();
                if (binding.cellIds != null) foreach (string id in binding.cellIds)
                {
                    if (level.FindCellIndex(id) < 0) Add(issues, "关联「" + binding.key + "」引用了已删除或裁剪的格子。", cell, rule.id);
                    if (!ids.Add(id ?? "")) Add(issues, "关联「" + binding.key + "」包含重复格子。", cell, rule.id);
                }
            }
            var required = RuleAnalysis.BindingKeys(rule);
            foreach (var branches in new[] { rule.onEnter, rule.onLeave }) if (branches != null) foreach (var branch in branches)
            {
                if (branch == null) continue;
                if (branch.actions != null) foreach (var action in branch.actions)
                {
                    if (action == null || action.kind != ActionKind.ChangeTile) continue;
                    foreach (int targetCell in RuleEngine.ResolveBinding(level, cell, bindings, action.targetBinding))
                    {
                        var target = level.GetCell(targetCell);
                        var targetDefinition = target == null ? null : book.Find(target.ruleId);
                        if (targetDefinition != null && IsProtected(targetDefinition.visual))
                            Add(issues, "改变地块操作不能覆盖已放置的目标或传送端点，目标位置 " + level.Position(targetCell) + "。", cell, rule.id);
                    }
                }
            }
            foreach (string key in required)
            {
                BindingData found = bindings == null ? null : bindings.Find(x => x != null && x.key == key);
                if (found == null || found.cellIds == null || found.cellIds.Count == 0) Add(issues, "请为规则「" + rule.name + "」所需的关联「" + key + "」选择至少一个格子。", cell, rule.id);
            }
        }

        private static bool IsProtected(TerrainVisual visual)
        { return visual == TerrainVisual.Goal || visual == TerrainVisual.PortalEntrance || visual == TerrainVisual.PortalExit; }

        private static void Add(List<ValidationIssue> issues, string message, int cell = -1, string rule = "")
        { issues.Add(new ValidationIssue { severity = ValidationSeverity.Error, message = message, cellIndex = cell, ruleId = rule }); }
    }
}
