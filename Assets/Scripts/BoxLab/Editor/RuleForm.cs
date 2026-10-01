#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BoxLab.Editor
{
    /// <summary>Typed rule grammar editor, including nested condition groups, branches and operations.</summary>
    public static class RuleForm
    {
        static readonly string[] ConditionLabels = { "始终成立", "全部子条件成立", "任一子条件成立", "至少 N 个子条件成立", "本次作用对象是", "格子占用数量", "激活信号数量" };
        static readonly string[] ActorLabels = { "无", "人物", "箱子", "人物与箱子" };
        static readonly string[] ActionLabels = { "额外移动", "传送到配对出口", "改变地块定义", "移除箱子" };
        static readonly string[] VisualLabels = { "普通地面", "目标点", "冰面", "压力板", "转向格", "选择性通行", "传送入口", "传送出口", "空洞", "坍塌道路", "通行门" };
        static readonly string[] DirectionLabels = { "上 / 北", "右 / 东", "下 / 南", "左 / 西" };
        static bool structural;

        public static bool Draw(RuleDefinition rule, RuleBookData book)
        {
            structural = false;
            float oldWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 102;
            EditorGUI.BeginChangeCheck();
            rule.name = EditorGUILayout.TextField("显示名称", rule.name);
            EditorGUILayout.LabelField("作用位置", rule.boundary ? "两个相邻格子的边界" : "格子内部");
            rule.visual = (TerrainVisual)EditorGUILayout.Popup("表现类型", (int)rule.visual, VisualLabels);
            Color color;
            if (!ColorUtility.TryParseHtmlString(rule.colorHex, out color)) color = new Color(.4f, .5f, .6f);
            EditorGUI.BeginChangeCheck(); color = EditorGUILayout.ColorField("标识颜色", color);
            if (EditorGUI.EndChangeCheck()) { rule.colorHex = "#" + ColorUtility.ToHtmlStringRGB(color); structural = true; }
            EditorGUILayout.Space(5);
            rule.allowedActors = ActorField("允许进入者", rule.allowedActors);
            if (!rule.boundary)
            {
                rule.signalActors = ActorField("产生受压信号", rule.signalActors);
                EditorGUILayout.LabelField("“人物与箱子”表示任一对象占据时激活；“无”表示不输出信号。", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(rule.boundary ? "通过边界的条件" : "进入格子的条件", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("先检查对象类型，再检查以下条件。不会穿过墙、关闭的门或已占用的格子。", EditorStyles.wordWrappedMiniLabel);
            if (rule.canEnter == null) { rule.canEnter = new ConditionSet(); structural = true; }
            DrawConditions(rule.canEnter);
            if (rule.boundary)
                EditorGUILayout.HelpBox("边界规则只判断能否通过。墙被门规则覆写后，运行时只执行当前边界定义。", MessageType.None);
            else
            {
                if (rule.visual == TerrainVisual.Hole) EditorGUILayout.HelpBox("空洞始终禁止人物进入；箱子可进入，移除行为由进入事件配置。", MessageType.Info);
                if (rule.visual == TerrainVisual.Goal) EditorGUILayout.HelpBox("目标点不叠加特殊效果；请保持进入 / 离开事件为空。", MessageType.Info);
                if (rule.onEnter == null) { rule.onEnter = new List<RuleBranch>(); structural = true; }
                if (rule.onLeave == null) { rule.onLeave = new List<RuleBranch>(); structural = true; }
                DrawBranches("进入后", rule.onEnter, book, false);
                DrawBranches("离开后", rule.onLeave, book, true);
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("规则标识 · " + rule.id, EditorStyles.wordWrappedMiniLabel);
            bool changed = EditorGUI.EndChangeCheck() || structural;
            EditorGUIUtility.labelWidth = oldWidth;
            return changed;
        }

        static ActorMask ActorField(string label, ActorMask value)
        { return (ActorMask)EditorGUILayout.Popup(label, Mathf.Clamp((int)value, 0, 3), ActorLabels); }
        static bool Group(ConditionKind kind) => kind == ConditionKind.All || kind == ConditionKind.Any || kind == ConditionKind.AtLeast;

        static void DrawConditions(ConditionSet set)
        {
            if (set.nodes == null) { set.nodes = new List<ConditionNode>(); structural = true; }
            if (set.nodes.Count == 0) { set.nodes.Add(new ConditionNode()); structural = true; }
            DrawNode(set, 0, 0);
        }
        static void DrawNode(ConditionSet set, int index, int depth)
        {
            if (index < 0 || index >= set.nodes.Count || depth > 12) { EditorGUILayout.HelpBox("条件树层级过深或结构异常，请校验。", MessageType.Warning); return; }
            var node = set.nodes[index];
            if (node == null) { set.nodes[index] = node = new ConditionNode { parent = index == 0 ? -1 : 0 }; structural = true; }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool removed = false;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var next = (ConditionKind)EditorGUILayout.Popup((int)node.kind, ConditionLabels);
                    if (next != node.kind)
                    {
                        if (Group(node.kind) && !Group(next)) RemoveDescendants(set, index, false);
                        node.kind = next; structural = true;
                    }
                    if (index != 0 && GUILayout.Button("×", GUILayout.Width(20))) { RemoveDescendants(set, index, true); removed = true; structural = true; }
                }
                if (removed) return;
                node.negate = EditorGUILayout.Toggle(new GUIContent("条件取反", "将该条件或整组条件的结果反转。"), node.negate);
                if (node.kind == ConditionKind.ActorIs) node.actorMask = ActorField("对象类型", node.actorMask);
                if (node.kind == ConditionKind.Occupied || node.kind == ConditionKind.SignalCount)
                {
                    node.binding = EditorGUILayout.TextField(new GUIContent("关联键", "留空表示当前格子；填写名称后，在地图实例上选择该名称对应的格子列表。"), node.binding);
                    if (node.kind == ConditionKind.Occupied) node.actorMask = ActorField("占用者类型", node.actorMask);
                    node.threshold = EditorGUILayout.IntField("至少数量", node.threshold);
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(node.binding) ? "读取当前格子。" : "读取实例关联「" + node.binding + "」中的所有格子。", EditorStyles.wordWrappedMiniLabel);
                }
                if (Group(node.kind))
                {
                    if (node.kind == ConditionKind.AtLeast) node.threshold = EditorGUILayout.IntField("至少满足数", node.threshold);
                    int before = set.nodes.Count;
                    var children = Enumerable.Range(index + 1, Math.Max(0, set.nodes.Count - index - 1)).Where(i => set.nodes[i] != null && set.nodes[i].parent == index).ToList();
                    foreach (int child in children)
                    {
                        DrawNode(set, child, depth + 1);
                        if (set.nodes.Count != before) break;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("＋ 条件")) { set.nodes.Add(new ConditionNode { parent = index, kind = ConditionKind.ActorIs }); structural = true; }
                        if (GUILayout.Button("＋ 条件组")) { set.nodes.Add(new ConditionNode { parent = index, kind = ConditionKind.All }); structural = true; }
                    }
                }
            }
        }
        static void RemoveDescendants(ConditionSet set, int root, bool includeRoot)
        {
            var removed = new HashSet<int>(); removed.Add(root);
            for (int i = root + 1; i < set.nodes.Count; i++) if (set.nodes[i] != null && removed.Contains(set.nodes[i].parent)) removed.Add(i);
            if (!includeRoot) removed.Remove(root);
            var mapping = new Dictionary<int, int>(); var result = new List<ConditionNode>();
            for (int i = 0; i < set.nodes.Count; i++) if (!removed.Contains(i)) { mapping[i] = result.Count; result.Add(set.nodes[i]); }
            foreach (var n in result) if (n != null && n.parent >= 0) n.parent = mapping.ContainsKey(n.parent) ? mapping[n.parent] : 0;
            set.nodes = result;
        }
        static void DrawBranches(string title, List<RuleBranch> branches, RuleBookData book, bool leave)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(title + " · 条件分支", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(leave ? "对象离开后、人物跟进之前执行。可改变原地块或关联地块。" : "按顺序选择第一条成立的分支。末尾添加“始终成立”即为否则。", EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                if (branch == null) { branch = branches[i] = new RuleBranch(); structural = true; }
                bool removed = false, reorder = false;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("分支 " + (i + 1), EditorStyles.boldLabel);
                        using (new EditorGUI.DisabledScope(i == 0)) if (GUILayout.Button("↑", GUILayout.Width(22))) { branches[i] = branches[i - 1]; branches[i - 1] = branch; structural = reorder = true; }
                        using (new EditorGUI.DisabledScope(i == branches.Count - 1)) if (GUILayout.Button("↓", GUILayout.Width(22))) { branches[i] = branches[i + 1]; branches[i + 1] = branch; structural = reorder = true; }
                        if (GUILayout.Button("删除", GUILayout.Width(40))) { branches.RemoveAt(i); structural = removed = true; }
                    }
                    if (!removed && !reorder)
                    {
                        if (branch.condition == null) { branch.condition = new ConditionSet(); structural = true; }
                        DrawConditions(branch.condition);
                        EditorGUILayout.LabelField("执行操作（自上而下）", EditorStyles.miniBoldLabel);
                        if (branch.actions == null) { branch.actions = new List<RuleAction>(); structural = true; }
                        DrawActions(branch.actions, book, leave);
                    }
                }
                if (removed || reorder) break;
            }
            if (GUILayout.Button("＋ 添加" + title + "分支")) { branches.Add(new RuleBranch()); structural = true; }
        }
        static void DrawActions(List<RuleAction> actions, RuleBookData book, bool leave)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action == null) { action = actions[i] = new RuleAction(); structural = true; }
                bool removed = false, reordered = false;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (leave) { GUILayout.Label("改变地块定义"); if (action.kind != ActionKind.ChangeTile) EditorGUILayout.LabelField("不支持的离开操作", EditorStyles.miniLabel); }
                        else
                        {
                            var nextKind = (ActionKind)EditorGUILayout.Popup((int)action.kind, ActionLabels);
                            if (nextKind != action.kind)
                            {
                                action.kind = nextKind;
                                if (nextKind == ActionKind.Teleport) action.targetBinding = "";
                                structural = true;
                            }
                        }
                        using (new EditorGUI.DisabledScope(i == 0)) if (GUILayout.Button("↑", GUILayout.Width(20))) { actions[i] = actions[i - 1]; actions[i - 1] = action; structural = reordered = true; }
                        if (GUILayout.Button("×", GUILayout.Width(20))) { actions.RemoveAt(i); structural = removed = true; }
                    }
                    if (!removed && !reordered)
                    {
                        if (action.kind == ActionKind.Move)
                        {
                            action.directionMode = (DirectionMode)EditorGUILayout.Popup("方向来源", (int)action.directionMode, new[] { "沿本次进入方向", "固定方向" });
                            if (action.directionMode == DirectionMode.Fixed) action.direction = (Direction)EditorGUILayout.Popup("基础方向", (int)action.direction, DirectionLabels);
                            action.distance = EditorGUILayout.IntField("额外移动格数", action.distance);
                            EditorGUILayout.LabelField("遇阻停止，不推动其他箱子；进入新效果格时，由新效果接替。固定方向随实例旋转。", EditorStyles.wordWrappedMiniLabel);
                        }
                        else if (action.kind == ActionKind.Teleport)
                        {
                            EditorGUILayout.HelpBox("仅箱子传送。使用地图实例的配对标识查找出口；目的格被占用或不允许进入时，留在入口。", MessageType.None);
                            if (!string.IsNullOrEmpty(action.targetBinding))
                            {
                                EditorGUILayout.HelpBox("该操作保留了旧的目标关联；传送只支持配对出口。", MessageType.Error);
                                if (GUILayout.Button("清除旧目标，使用配对出口")) { action.targetBinding = ""; structural = true; }
                            }
                        }
                        else if (action.kind == ActionKind.ChangeTile)
                        {
                            action.targetBinding = EditorGUILayout.TextField(new GUIContent("目标关联键", "留空改变触发本事件的格子；填名称则改变该实例绑定的所有目标格子。"), action.targetBinding);
                            var targets = book.rules.Where(r => !r.boundary && r.visual != TerrainVisual.Goal && r.visual != TerrainVisual.PortalEntrance && r.visual != TerrainVisual.PortalExit).ToList();
                            var labels = new[] { "请选择新的地块定义" }.Concat(targets.Select(r => r.name)).ToArray();
                            int selected = targets.FindIndex(r => r.id == action.targetRuleId) + 1;
                            int next = EditorGUILayout.Popup("变为", selected, labels);
                            if (next != selected) action.targetRuleId = next == 0 ? "" : targets[next - 1].id;
                            EditorGUILayout.LabelField(string.IsNullOrEmpty(action.targetBinding) ? "改变当前触发格；边墙、门和格子身份保持不变。" : "改变关联「" + action.targetBinding + "」中的地块。", EditorStyles.wordWrappedMiniLabel);
                        }
                        else if (action.kind == ActionKind.RemoveBox)
                            EditorGUILayout.HelpBox("移除本次进入的箱子。剩余箱子少于目标数时失败；人物没有生命或销毁行为。", MessageType.None);
                    }
                }
                if (removed || reordered) break;
            }
            if (GUILayout.Button("＋ 添加操作")) { actions.Add(new RuleAction { kind = leave ? ActionKind.ChangeTile : ActionKind.Move }); structural = true; }
        }
        public static List<string> BindingKeys(RuleDefinition rule)
        {
            return RuleAnalysis.BindingKeys(rule);
        }

        /// <summary>
        /// Find every definition this cell can acquire through self or remote ChangeTile operations.
        /// Conditions are deliberately not evaluated: all authorable branches must have valid bindings.
        /// Static cell identity/bindings survive a terrain change, so replacements reuse this instance's keys.
        /// </summary>
        public static List<string> BindingKeysForInstance(LevelData level, RuleBookData book, int cellIndex)
        {
            return RuleAnalysis.BindingKeysForInstance(level, book, cellIndex);
        }
    }
}
#endif
