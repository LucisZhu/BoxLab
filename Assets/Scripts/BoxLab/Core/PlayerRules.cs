using System;
using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>Fixed, player-facing vocabulary. Does not change the V1 authoring presets or assets.</summary>
    public static class PlayerRules
    {
        public const string Plain = "plain", Goal = "goal", Ice = "ice", Plate = "plate", RotationPlate = "rotation-plate";
        public const string Arrow = "turn", Selective = "selective", PortalEntrance = "portal-entrance", PortalExit = "portal-exit";
        public const string BoxOnly = "box-only";
        public const string Hole = "hole", Fragile = "fragile", Gate = "gate", Switches = "switches";
        public const int MaxConnections = 3, SignalStyleCount = 15;

        public static RuleBookData CreateBook()
        {
            var book = Presets.CreateBook();
            book.rules.RemoveAll(rule => rule.id == Presets.InverseGate);
            book.rules.Add(new RuleDefinition { id = BoxOnly, name = "箱子通道", visual = TerrainVisual.Selective, colorHex = "#E9AE58", builtIn = true, allowedActors = ActorMask.Box });
            var ice = book.Find(Ice); ice.name = "冰面"; ice.onEnter[0].actions[0].distance = 1;
            book.Find(PortalEntrance).onEnter[0].actions[0].directionalPortal = true;
            var arrow = book.Find(Arrow); arrow.name = "方向箭头"; arrow.onEnter.Clear();
            arrow.onEnter.Add(new RuleBranch
            {
                condition = ConditionSet.Actor(ActorMask.Box),
                actions = new List<RuleAction> { new RuleAction { kind = ActionKind.Move, directionMode = DirectionMode.Fixed, direction = Direction.North, distance = 1 } }
            });
            book.rules.Add(new RuleDefinition { id = RotationPlate, name = "旋转踏板", visual = TerrainVisual.RotationPlate, colorHex = "#E879F9", builtIn = true, signalActors = ActorMask.Both });
            var gate = book.Find(Gate); gate.name = "联动门";
            gate.canEnter = new ConditionSet { nodes = new List<ConditionNode> { new ConditionNode { kind = ConditionKind.AllSignals, binding = Switches } } };
            return book;
        }

        public static List<ValidationIssue> Validate(LevelData level)
        {
            var book = CreateBook();
            var issues = LevelValidator.Validate(level, book);
            if (level == null || level.cells == null) return issues;
            if (level.width < 2 || level.height < 2) Error(issues, "玩家地图宽高至少为 2 格。");
            for (int i = 0; i < level.cells.Count; i++)
            {
                var cell = level.cells[i]; if (cell == null) continue;
                if (cell.rotation < 0 || cell.rotation > 3) Error(issues, "地块方向必须为四个正交方向。", i, cell.ruleId);
                if (cell.signalStyle < -1 || cell.signalStyle >= SignalStyleCount) Error(issues, "按钮颜色与符号标识无效。", i, cell.ruleId);
                if (cell.ruleId == Arrow) ValidateConnections(level, cell.bindings, RotationPlate, 0, issues, i, cell.ruleId, "箭头");
                else if (HasConnections(cell.bindings)) Error(issues, "只有方向箭头可以在格子上关联旋转踏板。", i, cell.ruleId);
                if (cell.ruleId != PortalEntrance && cell.ruleId != PortalExit && !string.IsNullOrEmpty(cell.portalPair))
                    Error(issues, "传送配对标识只能设置在入口或出口。", i, cell.ruleId);
                if (cell.ruleId == PortalExit && cell.rotation >= 0 && cell.rotation < 4 && level.Neighbor(i, (Direction)cell.rotation) < 0)
                    issues.Add(new ValidationIssue { severity = ValidationSeverity.Warning, message = "传送出口朝向地图外，箱子无法走出；请旋转或移动出口。", cellIndex = i, ruleId = cell.ruleId });
            }
            if (level.edges != null) foreach (var edge in level.edges)
            {
                if (edge == null) continue;
                int firstEdgeIssue = issues.Count;
                if (edge.kind == EdgeKind.Gate)
                {
                    if (edge.ruleId != Gate) Error(issues, "玩家联动门只能使用固定的全部受压规则。", edge.cell, edge.ruleId);
                    ValidateConnections(level, edge.bindings, Plate, 1, issues, edge.cell, edge.ruleId, "联动门");
                }
                else if (HasConnections(edge.bindings)) Error(issues, "只有联动门可以在边界上关联压力板。", edge.cell, edge.ruleId);
                if (level.Contains(edge.cell) && (int)edge.direction >= 0 && (int)edge.direction < 4)
                {
                    int at = edge.cell; Direction side = edge.direction; level.NormalizeEdge(ref at, ref side);
                    for (int i = firstEdgeIssue; i < issues.Count; i++)
                    { issues[i].cellIndex = at; issues[i].edgeDirection = (int)side; }
                }
            }
            return issues;
        }

        static bool HasConnections(List<BindingData> bindings)
        {
            if (bindings != null) foreach (var binding in bindings) if (binding != null && binding.cellIds != null && binding.cellIds.Count > 0) return true;
            return false;
        }

        static void ValidateConnections(LevelData level, List<BindingData> bindings, string sourceRule, int minimum, List<ValidationIssue> issues, int at, string ruleId, string label)
        {
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            if (bindings != null) foreach (var binding in bindings)
            {
                if (binding == null) continue;
                if (binding.key != Switches) { Error(issues, label + "存在不支持的关联类型。", at, ruleId); continue; }
                if (binding.cellIds == null) continue;
                foreach (string id in binding.cellIds)
                {
                    if (!sourceIds.Add(id ?? "")) continue; // Base validation explains duplicates.
                    int source = level.FindCellIndex(id);
                    if (source < 0) continue; // Base validation explains missing stable references.
                    if (level.cells[source].ruleId != sourceRule)
                        Error(issues, label + "只能关联" + (sourceRule == Plate ? "普通压力板" : "旋转踏板") + "。", at, ruleId);
                }
            }
            if (sourceIds.Count < minimum || sourceIds.Count > MaxConnections)
                Error(issues, label + "需要关联 " + minimum + "～" + MaxConnections + " 个" + (sourceRule == Plate ? "压力板" : "旋转踏板") + "。", at, ruleId);
        }

        static void Error(List<ValidationIssue> issues, string message, int cell = -1, string ruleId = "")
        { issues.Add(new ValidationIssue { severity = ValidationSeverity.Error, message = message, cellIndex = cell, ruleId = ruleId }); }

        public static List<LevelData> CreateCampaignLevels() { return CampaignLevels.Create(); }

        /// <summary>The original eleven tutorial IDs remain stable; the box-only lesson has its own ID.</summary>
        public static List<LevelData> CreateDemoLevels()
        {
            var legacy = DemoLevels.Create();
            var levels = new List<LevelData>();
            var basic = legacy[0]; basic.name = "01 · 第一次推动"; levels.Add(basic);
            var ice = legacy[1]; ice.name = "02 · 连续冰面";
            ice.description = "箱子进入每块冰面后，沿推动方向额外前进一步。连续冰面逐格接力；人物正常行走。";
            DemoLevels.Tile(ice, 4, 2, Ice); DemoLevels.Tile(ice, 5, 2, Ice); levels.Add(ice);
            var gate = legacy[2]; gate.name = "03 · 留下一个帮手";
            gate.description = "箱子或人物压住压力板，关联门保持打开。关联多个板时，必须全部压住。先运送，再回收。"; levels.Add(gate);
            var arrow = LevelData.Create(5, 5, "04 · 方向交接");
            arrow.description = "箱子进入箭头，沿箭头方向额外移动一格。把向右的推动变成向上的运输。";
            arrow.player = arrow.Index(1, 1); DemoLevels.Box(arrow, 2, 1); DemoLevels.Tile(arrow, 3, 1, Arrow); DemoLevels.Tile(arrow, 3, 2, Goal); levels.Add(arrow);
            var rotation = LevelData.Create(5, 5, "05 · 转一次，再出发");
            rotation.description = "旋转踏板每次从空闲变为受压，会让关联箭头顺时针转 90°。离开不转，持续压住不转。先踩板，再推箱。";
            rotation.player = rotation.Index(0, 1); DemoLevels.Box(rotation, 2, 1); DemoLevels.Tile(rotation, 1, 1, RotationPlate);
            DemoLevels.Tile(rotation, 3, 1, Arrow); rotation.cells[rotation.Index(3, 1)].rotation = 3;
            rotation.cells[rotation.Index(3, 1)].bindings.Add(DemoLevels.Binding(rotation, Switches, rotation.Index(1, 1)));
            DemoLevels.Tile(rotation, 3, 2, Goal); levels.Add(rotation);
            var selective = legacy[5]; selective.name = "06 · 只让人物经过";
            selective.description = "人物通道只让人物通过，箱子不能进入。人物可以绕过去，换一侧继续推动。"; levels.Add(selective);
            var portal = legacy[6]; portal.name = "08 · 两端相连";
            portal.description = "箱子只从入口正面进入时传送，并沿出口朝向自动走出一格；出口或前方被挡则留在入口。人物正常步行、不传送。";
            portal.cells[portal.Index(3, 2)].rotation = (int)Direction.West;
            portal.cells[portal.Index(5, 2)].rotation = (int)Direction.East;
            levels.Add(portal);
            var hole = legacy[7]; hole.name = "09 · 空洞绕行"; levels.Add(hole);
            var fragile = legacy[8]; fragile.name = "10 · 回不去的路";
            fragile.description = "破裂石板上的物件离开后，地面立即坍塌。推走箱子时人物留在原地，需要从旁边绕行。"; levels.Add(fragile);
            for (int index = 0; index < levels.Count; index++)
            {
                var level = levels[index]; level.id = "boxlab-player-lesson-" + (index + 1).ToString("00");
                int style = 0;
                foreach (var cell in level.cells) if (cell.ruleId == Plate || cell.ruleId == RotationPlate) cell.signalStyle = (style++ + index * 2) % SignalStyleCount;
            }
            // Assign the original IDs before inserting lessons; saved progress must never move to a different map.
            var boxOnly = LevelData.Create(5, 4, "07 · 只让箱子通过");
            boxOnly.id = "boxlab-player-lesson-box-only";
            boxOnly.description = "先把箱子送过专用通道。推离通道时人物停在原地；退回再绕到下方，将箱子送上目标。";
            boxOnly.player = boxOnly.Index(0, 1); DemoLevels.Box(boxOnly, 1, 1);
            DemoLevels.Tile(boxOnly, 2, 1, BoxOnly); DemoLevels.Tile(boxOnly, 3, 2, Goal);
            for (int x = 1; x <= 2; x++)
            {
                DemoLevels.Wall(boxOnly, x, 1, Direction.North);
                DemoLevels.Wall(boxOnly, x, 1, Direction.South);
            }
            levels.Insert(6, boxOnly);
            var combinations = ChallengeLevels.Create();
            combinations[0].name = "11 · 留守与运送"; combinations[1].name = "12 · 接力路线";
            levels.AddRange(combinations);
            return levels;
        }
    }
}
