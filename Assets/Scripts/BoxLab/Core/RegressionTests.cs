using System;
using System.Collections.Generic;
using System.Reflection;

namespace BoxLab
{
    /// <summary>Runs both from the Unity verification menu and the standalone Mono harness.</summary>
    public static class RegressionTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            var failures = new List<string>();
            Run("普通行走与一次推动", RegularMovement, passed, failures);
            Run("公共边墙双向阻挡与地图边界", SharedWalls, passed, failures);
            Run("选择性地块推动后人物留在原位", SelectivePush, passed, failures);
            Run("冰面额外三格与反向进入", IceDistance, passed, failures);
            Run("新冰面重启距离，转向替换旧滑行", ReplacementMovement, passed, failures);
            Run("自动移动遇箱停止，不连推", AutomaticBlock, passed, failures);
            Run("三个按钮数量条件与反向门", ArbitraryGates, passed, failures);
            Run("嵌套分组、取反与实例关联", NestedConditions, passed, failures);
            Run("配对传送、人物不传送与出口占用", PairedPortals, passed, failures);
            Run("空洞移除箱子并失败，人物禁止进入", HoleBehavior, passed, failures);
            Run("坍塌推动顺序与受阻不坍塌", FragilePush, passed, failures);
            Run("快照深克隆与完整撤销恢复", ImmutableSnapshots, passed, failures);
            Run("连锁循环整体回退", LoopRollback, passed, failures);
            Run("禁止运行中覆盖目标或使人物悬空", ChangeTileSafety, passed, failures);
            Run("编译器定位缺失配对与失效引用", ValidationCases, passed, failures);
            Run("编译器拒绝枚举、掩码与数量错误", ValidationInputBounds, passed, failures);
            Run("编译器保护目标、端点与传送配对", ValidationProtectedTargets, passed, failures);
            Run("地形变换后的关联闭包与编辑校验一致", DynamicBindingAnalysis, passed, failures);
            Run("穷举找到路径并能实际执行", SolverSolved, passed, failures);
            Run("穷举无解、上限未知、循环未知、取消", SolverConclusions, passed, failures);
            Run("搜索状态包含地面变化并忽略计数", SearchStateKeys, passed, failures);
            Run("普通推动压力板交接为原子状态", AtomicPressureHandoff, passed, failures);
            Run("玩家规则连续冰与固定箭头", PlayerMovementRules, passed, failures);
            Run("人物通道与箱子通道独立准入、推出不跟进", PlayerSelectiveChannels, passed, failures);
            Run("冰面进入箱子通道、碰撞与撤销重放", BoxOnlyAutomaticMovement, passed, failures);
            Run("箱子通道教程绕行完成且旧教程持久ID不变", BoxOnlyLesson, passed, failures);
            Run("旋转踏板只在新受压时旋转", RotationPressEdges, passed, failures);
            Run("旋转状态可撤销并参与搜索", RotationSnapshots, passed, failures);
            Run("玩家门全部受压与最多三个关联", PlayerConnectionValidation, passed, failures);
            Run("玩家版十二个新手教程求解并回放", PlayerLessons, passed, failures);
            Run("额外箱子可压板，全部目标覆盖即胜利", SurplusGoalRules, passed, failures);
            Run("额外箱子可掉落，剩余不足才失败", SurplusDrop, passed, failures);
            Run("额外死角箱与消耗箱不妨碍正确求解", SurplusSolver, passed, failures);
            Run("删除层级随草稿克隆且不影响运行状态", AuthoringLayerState, passed, failures);
            try { passed.AddRange(DirectionalPortalVerification.RunAll()); }
            catch (Exception error) { failures.Add("定向传送：" + error.Message); }
            if (failures.Count > 0) throw new InvalidOperationException("回归验证失败 " + failures.Count + " 项：\n" + string.Join("\n", failures.ToArray()));
            return passed;
        }

        static void Run(string name, Action test, List<string> passed, List<string> failures)
        {
            try { test(); passed.Add("通过：" + name); }
            catch (Exception error) { failures.Add(name + "：" + error.Message); }
        }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static LevelData Board(int width, int height, int player, int[] boxes, params int[] goals)
        {
            var level = LevelData.Create(width, height, "回归测试"); level.player = player; level.boxes = new List<int>(boxes);
            foreach (int goal in goals) level.cells[goal].ruleId = Presets.Goal;
            return level;
        }
        static BindingData Bind(LevelData level, string name, params int[] positions)
        {
            var data = new BindingData { key = name };
            foreach (int position in positions) data.cellIds.Add(level.cells[position].id);
            return data;
        }
        static RuleDefinition Movement(RuleBookData book, string id, Direction direction, int distance)
        {
            var rule = new RuleDefinition { id = id, name = id, visual = TerrainVisual.Turn };
            rule.onEnter.Add(new RuleBranch { condition = ConditionSet.Actor(ActorMask.Box), actions = new List<RuleAction>
            { new RuleAction { kind = ActionKind.Move, directionMode = DirectionMode.Fixed, direction = direction, distance = distance } } });
            book.rules.Add(rule); return rule;
        }
        static RuleDefinition Change(RuleBookData book, string id, string replacement, string binding)
        {
            var rule = new RuleDefinition { id = id, name = id };
            rule.onEnter.Add(new RuleBranch { actions = new List<RuleAction>
            { new RuleAction { kind = ActionKind.ChangeTile, targetRuleId = replacement, targetBinding = binding } } });
            book.rules.Add(rule); return rule;
        }
        static StepResult Step(LevelData level, RuleBookData book, BoardState state, Direction direction)
        { return RuleEngine.Step(level, book, state, direction); }

        static void RegularMovement()
        {
            var book = Presets.CreateBook(); var level = Board(4, 3, 0, new[] { 5 }, 11); var initial = RuleEngine.CreateState(level);
            var walk = Step(level, book, initial, Direction.North);
            Check(walk.changed && walk.state.player == 4 && walk.state.boxes[0] == 5, "一次行走应移动一格。");
            Check(walk.state.moves == 1 && walk.state.pushes == 0 && initial.player == 0, "计步或输入不可变性错误。");
            var push = Step(level, book, walk.state, Direction.East);
            Check(push.state.player == 5 && push.state.boxes[0] == 6 && push.state.pushes == 1 && push.state.moves == 2, "普通推动应箱子先行、人物跟进一格。");
            var blocked = Step(level, book, initial, Direction.West);
            Check(!blocked.changed && blocked.state.moves == 0, "撞边界不能消耗步数。");
        }

        static void SurplusGoalRules()
        {
            var book = PlayerRules.CreateBook(); var level = Board(4, 3, 4, new[] { 5, 9 }, 6);
            level.cells[9].ruleId = PlayerRules.Plate;
            var gate = new EdgeData { cell = 5, direction = Direction.East, kind = EdgeKind.Gate, ruleId = PlayerRules.Gate };
            gate.bindings.Add(Bind(level, PlayerRules.Switches, 9)); level.edges.Add(gate);
            Check(!LevelValidator.HasErrors(PlayerRules.Validate(level)), "两箱一目标和额外压板箱应通过结构校验。");
            var initial = RuleEngine.CreateState(level, book);
            var result = Step(level, book, initial, Direction.East);
            Check(initial.status == GameStatus.Playing && result.state.status == GameStatus.Won, "目标全部覆盖后必须通关，不能要求额外箱也归位。");
            Check(result.state.boxes[0] == 6 && result.state.boxes[1] == 9 && RuleEngine.IsGateOpen(level, book, result.state, gate), "通关时额外箱应继续留在压力板上。");
            var covered = level.Clone(); covered.boxes[0] = 6;
            Check(RuleEngine.CreateState(covered, book).status == GameStatus.Won, "开局覆盖全部目标且有额外箱也已通关。");
            var shortage = Board(4, 3, 4, new[] { 5 }, 6, 10);
            Check(LevelValidator.HasErrors(PlayerRules.Validate(shortage)), "箱子少于目标必须报错。");
            var noGoals = Board(4, 3, 4, new[] { 5, 9 });
            Check(LevelValidator.HasErrors(PlayerRules.Validate(noGoals)) && RuleEngine.GetStatus(noGoals, book, RuleEngine.CreateState(noGoals)) != GameStatus.Won, "零目标不能校验或自动获胜。");
        }

        static LevelData SurplusDropBoard(bool secondHole)
        {
            var level = Board(4, 3, 0, new[] { 1, 6 }, secondHole ? 11 : 7);
            level.cells[2].ruleId = PlayerRules.Hole;
            if (secondHole) level.cells[7].ruleId = PlayerRules.Hole;
            level.edges.Add(new EdgeData { cell = 0, direction = Direction.North, kind = EdgeKind.Wall });
            return level;
        }

        static void SurplusDrop()
        {
            var book = PlayerRules.CreateBook(); var level = SurplusDropBoard(false);
            var initial = RuleEngine.CreateState(level, book); var dropped = Step(level, book, initial, Direction.East).state;
            Check(dropped.boxes[0] == -1 && dropped.boxes[1] == 6 && dropped.status == GameStatus.Playing, "丢掉额外箱但剩余仍够填目标时不能失败。");
            var finish = Step(level, book, Step(level, book, dropped, Direction.North).state, Direction.East).state;
            Check(finish.status == GameStatus.Won && finish.boxes[0] == -1 && finish.boxes[1] == 7, "消耗额外箱后应能用剩余箱完成目标。");
            Check(initial.boxes[0] == 1 && initial.status == GameStatus.Playing, "撤销所用的初始快照不能因掉落被修改。");
            var losing = SurplusDropBoard(true); var state = RuleEngine.CreateState(losing, book);
            foreach (var direction in new[] { Direction.East, Direction.North, Direction.East }) state = Step(losing, book, state, direction).state;
            Check(state.status == GameStatus.Lost && state.boxes.TrueForAll(x => x == -1), "继续损失直到剩余箱不足目标时必须失败。");
        }

        static void SurplusSolver()
        {
            var book = PlayerRules.CreateBook(); var corner = Board(4, 3, 4, new[] { 5, 0 }, 6);
            var solver = new SokobanSolver(corner, book, 1000, 30000); Drain(solver);
            Check(solver.Status == SolveStatus.Solved && solver.Solution.Count == 1, "额外箱困在非目标角落不能导致无解误判。");
            corner.boxes[0] = 6;
            var already = new SokobanSolver(corner, book, 1000, 30000);
            Check(already.Status == SolveStatus.Solved && already.Solution.Count == 0, "全部目标已覆盖时应找到零指令解。");
            var disposal = SurplusDropBoard(false); solver = new SokobanSolver(disposal, book, 1000, 30000); Drain(solver);
            Check(solver.Status == SolveStatus.Solved && solver.Solution.Count == 3, "求解器不能剪掉先销毁额外箱再通关的路径。");
            var state = RuleEngine.CreateState(disposal, book);
            foreach (var direction in solver.Solution) state = Step(disposal, book, state, direction).state;
            Check(state.status == GameStatus.Won && state.boxes.Contains(-1), "求解路径必须实际消耗额外箱并通关。");
        }

        static void AuthoringLayerState()
        {
            var level = Board(4, 3, 4, new[] { 5, 0 }, 6); var copy = level.Clone(); copy.cells[5].editTopLayer = 1;
            Check(level.cells[5].editTopLayer == 0 && copy.Clone().cells[5].editTopLayer == 1, "编辑层级必须独立随地图克隆。");
            var a = RuleEngine.CreateState(level); var b = RuleEngine.CreateState(copy);
            Check(a.GetKey() == b.GetKey(), "编辑删除顺序不能改变运行状态键。");
            var solver = new SokobanSolver(level, PlayerRules.CreateBook(), 1000, 30000);
            var key = typeof(SokobanSolver).GetMethod("Key", BindingFlags.Instance | BindingFlags.NonPublic);
            Check((string)key.Invoke(solver, new object[] { a }) == (string)key.Invoke(solver, new object[] { b }), "编辑层级不能改变求解器去重状态。");
        }

        static void SharedWalls()
        {
            var book = Presets.CreateBook(); var level = Board(3, 2, 0, new[] { 4 }, 5);
            level.edges.Add(new EdgeData { cell = 1, direction = Direction.West, kind = EdgeKind.Wall });
            var state = RuleEngine.CreateState(level);
            Check(!Step(level, book, state, Direction.East).changed, "墙左侧未阻挡。");
            state.player = 1;
            Check(!Step(level, book, state, Direction.West).changed, "墙右侧未阻挡。");
            Check(object.ReferenceEquals(level.FindEdge(0, Direction.East), level.FindEdge(1, Direction.West)), "相邻格没有引用同一边界。");
            Check(!RuleEngine.CanCrossBoundary(level, book, state, 0, -1, ActorMask.Box), "不能跨出地图。");
        }

        static void SelectivePush()
        {
            var book = Presets.CreateBook(); var rule = book.Find(Presets.Selective).Clone(); rule.id = "box-only"; rule.allowedActors = ActorMask.Box; book.rules.Add(rule);
            var level = Board(4, 1, 0, new[] { 1 }, 3); level.cells[1].ruleId = rule.id;
            var state = RuleEngine.CreateState(level); var result = Step(level, book, state, Direction.East);
            Check(result.changed && result.state.boxes[0] == 2 && result.state.player == 0, "可推离箱子专用格，但人物不跟进。");
            level.edges.Add(new EdgeData { cell = 0, direction = Direction.East, kind = EdgeKind.Wall });
            Check(!Step(level, book, state, Direction.East).changed, "不能隔着墙推箱子。");
        }

        static void IceDistance()
        {
            var book = Presets.CreateBook(); var level = Board(8, 1, 0, new[] { 1 }, 7); level.cells[2].ruleId = Presets.Ice;
            var result = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(result.state.boxes[0] == 5 && result.state.player == 1, "推入冰面后应额外移动三格。");
            var reverse = Board(8, 1, 6, new[] { 5 }, 0); reverse.cells[4].ruleId = Presets.Ice;
            var back = Step(reverse, book, RuleEngine.CreateState(reverse), Direction.West);
            Check(back.state.boxes[0] == 1 && back.state.player == 5, "反方向进入冰面也应额外滑三格。");
            level.player = 1; level.boxes[0] = 6;
            var player = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(player.state.player == 2, "默认冰面不滑动人物。");
        }

        static void ReplacementMovement()
        {
            var book = Presets.CreateBook(); var level = Board(8, 3, 0, new[] { 1 }, 23);
            level.cells[2].ruleId = Presets.Ice; level.cells[3].ruleId = Presets.Ice;
            var repeat = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(repeat.state.boxes[0] == 6, "进入第二块冰面应重新开始该格三步效果。");
            var turn = Movement(book, "turn-up-two", Direction.North, 2); level.cells[3].ruleId = turn.id;
            var redirected = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(redirected.state.boxes[0] == 19, "转向后不应继续原来向右的剩余滑行。");
            level.cells[3].rotation = 1;
            var rotated = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(rotated.state.boxes[0] == 5, "固定朝向应随实例旋转。");
        }

        static void AutomaticBlock()
        {
            var book = Presets.CreateBook(); var level = Board(8, 1, 0, new[] { 1, 4 }, 6, 7); level.cells[2].ruleId = Presets.Ice;
            var result = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(result.state.boxes[0] == 3 && result.state.boxes[1] == 4, "自动滑行不能连推另一箱子。");
            level.edges.Add(new EdgeData { cell = 2, direction = Direction.East, kind = EdgeKind.Wall });
            var wall = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(wall.state.boxes[0] == 2 && wall.changed, "滑行撞墙停在墙前，原始推动仍然有效。");
        }

        static void ArbitraryGates()
        {
            var book = Presets.CreateBook(); var level = Board(4, 3, 0, new[] { 4, 5, 7 }, 8, 9, 10);
            foreach (int cell in new[] { 4, 5, 6 }) level.cells[cell].ruleId = Presets.Plate;
            var gateRule = book.Find(Presets.Gate).Clone(); gateRule.id = "two-of-three"; gateRule.canEnter = ConditionSet.Signals("plates", 2); book.rules.Add(gateRule);
            var inverseRule = gateRule.Clone(); inverseRule.id = "inverse-two-of-three"; inverseRule.canEnter.nodes[0].negate = true; book.rules.Add(inverseRule);
            var gate = new EdgeData { cell = 0, direction = Direction.East, kind = EdgeKind.Gate, ruleId = gateRule.id };
            gate.bindings.Add(Bind(level, "plates", 4, 5, 6)); level.edges.Add(gate);
            var state = RuleEngine.CreateState(level);
            Check(RuleEngine.IsGateOpen(level, book, state, gate, ActorMask.Player), "三个关联中两个受压应该开门。");
            Check(Step(level, book, state, Direction.East).state.player == 1, "满足条件后应可穿门。");
            gate.ruleId = inverseRule.id; Check(!RuleEngine.IsGateOpen(level, book, state, gate), "反向门在同条件下应关闭。");
            state.boxes[1] = 11;
            Check(RuleEngine.IsGateOpen(level, book, state, gate), "只剩一个按钮受压时反向门应打开。");
            state.player = 5; Check(!RuleEngine.IsGateOpen(level, book, state, gate), "默认人物也可以压板。");
            book.Find(Presets.Plate).signalActors = ActorMask.Box;
            Check(RuleEngine.IsGateOpen(level, book, state, gate), "仅箱子压力板不应被人物触发。");
        }

        static void NestedConditions()
        {
            var book = Presets.CreateBook(); var level = Board(4, 2, 0, new[] { 4 }, 7); level.cells[4].ruleId = Presets.Plate; level.cells[5].ruleId = Presets.Plate;
            var bindings = new List<BindingData> { Bind(level, "A", 4), Bind(level, "B", 5) };
            var set = new ConditionSet { nodes = new List<ConditionNode>
            {
                new ConditionNode { kind = ConditionKind.All },
                new ConditionNode { parent = 0, kind = ConditionKind.ActorIs, actorMask = ActorMask.Box },
                new ConditionNode { parent = 0, kind = ConditionKind.Any },
                new ConditionNode { parent = 2, kind = ConditionKind.SignalCount, binding = "A" },
                new ConditionNode { parent = 2, kind = ConditionKind.SignalCount, binding = "B", negate = true }
            } };
            var state = RuleEngine.CreateState(level);
            Check(RuleEngine.Evaluate(level, book, state, set, 1, ActorMask.Box, bindings), "嵌套 All/Any 条件失败。");
            Check(!RuleEngine.Evaluate(level, book, state, set, 1, ActorMask.Player, bindings), "类型判断不应放行人物。");
            state.boxes[0] = 5;
            Check(!RuleEngine.Evaluate(level, book, state, set, 1, ActorMask.Box, bindings), "A 未压且 B 已压，取反后应不成立。");
            set.nodes[2].negate = true;
            Check(RuleEngine.Evaluate(level, book, state, set, 1, ActorMask.Box, bindings), "整个条件组取反应生效。");
        }

        static void PairedPortals()
        {
            var book = Presets.CreateBook(); var level = Board(6, 2, 0, new[] { 1 }, 11);
            level.cells[2].ruleId = Presets.PortalEntrance; level.cells[2].portalPair = "pair";
            level.cells[8].ruleId = Presets.PortalExit; level.cells[8].portalPair = "pair";
            var initial = RuleEngine.CreateState(level); var result = Step(level, book, initial, Direction.East);
            Check(result.state.boxes[0] == 8 && result.state.player == 1, "箱子应进入配对出口。");
            var player = Step(level, book, result.state, Direction.East);
            Check(player.state.player == 2, "人物可以站入口，但不传送。");
            var temp = level.cells[8]; level.cells[8] = level.cells[9]; level.cells[9] = temp;
            Check(Step(level, book, RuleEngine.CreateState(level), Direction.East).state.boxes[0] == 9, "移动出口后配对应随格子身份保留。");
            level.boxes.Add(9); level.cells[10].ruleId = Presets.Goal;
            var occupied = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(occupied.state.boxes[0] == 2 && occupied.state.boxes[1] == 9, "出口有箱子时应停入口。");
            var byPlayer = Board(5, 1, 0, new[] { 1 }, 4);
            byPlayer.cells[1].ruleId = Presets.PortalExit; byPlayer.cells[1].portalPair = "p";
            byPlayer.cells[2].ruleId = Presets.PortalEntrance; byPlayer.cells[2].portalPair = "p";
            var blocked = Step(byPlayer, book, RuleEngine.CreateState(byPlayer), Direction.East);
            Check(blocked.state.player == 1 && blocked.state.boxes[0] == 2, "跟进人物占住出口时应阻止箱子传送。");
        }

        static void HoleBehavior()
        {
            var book = Presets.CreateBook(); var level = Board(5, 1, 0, new[] { 1 }, 4); level.cells[2].ruleId = Presets.Hole;
            var initial = RuleEngine.CreateState(level); var result = Step(level, book, initial, Direction.East);
            Check(result.state.boxes[0] == -1 && result.state.status == GameStatus.Lost && result.state.player == 1, "推入空洞应移除箱子并失败。");
            Check(initial.boxes[0] == 1 && initial.status == GameStatus.Playing, "失败必须可通过原快照恢复。");
            initial.player = 1; initial.boxes[0] = 3;
            Check(!Step(level, book, initial, Direction.East).changed, "人物不能进入空洞。");
            book.Find(Presets.Hole).allowedActors = ActorMask.Both;
            Check(!Step(level, book, initial, Direction.East).changed, "人物禁止进入空洞属于固定约束。");
        }

        static void FragilePush()
        {
            var book = Presets.CreateBook(); var level = Board(4, 2, 0, new[] { 1 }, 7); level.cells[1].ruleId = Presets.Fragile;
            var initial = RuleEngine.CreateState(level); var result = Step(level, book, initial, Direction.East);
            Check(result.changed && result.state.player == 0 && result.state.boxes[0] == 2 && result.state.tiles[1] == Presets.Hole, "A1 人/A2 箱/A3 空必须变为 A1 人/A2 洞/A3 箱。");
            Check(!result.interrupted && initial.tiles[1] == Presets.Fragile, "坍塌应正常执行并保留撤销快照。");
            level.edges.Add(new EdgeData { cell = 1, direction = Direction.East, kind = EdgeKind.Wall });
            var blocked = Step(level, book, initial, Direction.East);
            Check(!blocked.changed && blocked.state.tiles[1] == Presets.Fragile, "箱子不能离开时不能坍塌。");
            level.edges.Clear(); level.cells[0].ruleId = Presets.Fragile; level.boxes[0] = 3;
            var person = Step(level, book, RuleEngine.CreateState(level), Direction.North);
            Check(person.state.player == 4 && person.state.tiles[0] == Presets.Hole, "人物离开后同样可以触发坍塌。");
        }

        static void ImmutableSnapshots()
        {
            var book = Presets.CreateBook(); var level = Board(5, 1, 0, new[] { 1 }, 4); level.cells[1].ruleId = Presets.Fragile;
            var state = RuleEngine.CreateState(level); string before = state.GetKey();
            var result = Step(level, book, state, Direction.East);
            Check(state.GetKey() == before && level.cells[1].ruleId == Presets.Fragile, "运行不能修改输入快照或编辑数据。");
            var undo = state.Clone(); Check(undo.player == 0 && undo.boxes[0] == 1 && undo.tiles[1] == Presets.Fragile && undo.moves == 0, "撤销快照不完整。");
            result.state.boxes[0] = 4; result.state.tiles[1] = Presets.Plain;
            Check(undo.GetKey() == before && result.frames.Count > 0 && result.frames[result.frames.Count - 1].boxes[0] == 2, "帧或撤销快照共享了可变数组。");
            level.cells[0].bindings.Add(Bind(level, "reference", 1)); var copy = level.Clone(); copy.cells[0].bindings[0].cellIds.Clear(); copy.boxes[0] = 3;
            Check(level.cells[0].bindings[0].cellIds.Count == 1 && level.boxes[0] == 1, "关卡深拷贝失效。");
            var cloneBook = book.Clone(); cloneBook.Find(Presets.Ice).onEnter[0].actions[0].distance = 9;
            Check(book.Find(Presets.Ice).onEnter[0].actions[0].distance == 3, "规则库深拷贝失效。");
        }

        static LevelData LoopBoard(RuleBookData book)
        {
            Movement(book, "loop-east", Direction.East, 1); Movement(book, "loop-west", Direction.West, 1);
            var level = Board(5, 1, 0, new[] { 1 }, 4); level.cells[2].ruleId = "loop-east"; level.cells[3].ruleId = "loop-west"; return level;
        }
        static void LoopRollback()
        {
            var book = Presets.CreateBook(); var level = LoopBoard(book); var initial = RuleEngine.CreateState(level);
            var result = Step(level, book, initial, Direction.East);
            Check(result.interrupted && !result.changed && result.state.GetKey() == initial.GetKey(), "循环必须回退整个输入。");
            Check(result.frames.Count == 0 && result.state.moves == 0 && result.message.Length > 0, "循环不能留下动画帧或消耗步数。");
        }

        static void ChangeTileSafety()
        {
            var book = Presets.CreateBook(); var level = Board(4, 2, 0, new[] { 6 }, 7);
            var rule = Change(book, "unsafe-change", Presets.Hole, ""); level.cells[1].ruleId = rule.id;
            var initial = RuleEngine.CreateState(level); var stranded = Step(level, book, initial, Direction.East);
            Check(stranded.interrupted && stranded.state.player == 0 && stranded.state.tiles[1] == rule.id, "不能将人物脚下改成空洞。");
            rule.onEnter[0].actions[0].targetBinding = "target"; level.cells[1].bindings.Add(Bind(level, "target", 7));
            var goal = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(goal.interrupted && goal.state.tiles[7] == Presets.Goal, "不能覆盖现有目标。");
            rule.onEnter[0].actions[0].targetRuleId = Presets.Goal; level.cells[1].bindings[0] = Bind(level, "target", 2);
            Check(Step(level, book, RuleEngine.CreateState(level), Direction.East).interrupted, "不能运行中新建目标。");
        }

        static void ValidationCases()
        {
            var book = Presets.CreateBook(); var level = Board(5, 2, 0, new[] { 1 }, 9);
            Check(!LevelValidator.HasErrors(LevelValidator.Validate(level, book)), "合法基础地图不应报错。");
            level.cells[2].ruleId = Presets.PortalEntrance; level.cells[2].portalPair = "missing";
            var missing = LevelValidator.Validate(level, book);
            Check(missing.Exists(x => x.severity == ValidationSeverity.Error && x.cellIndex == 2 && x.message.Contains("传送")), "缺失出口应定位到入口。");
            level.cells[3].ruleId = Presets.PortalExit; level.cells[3].portalPair = "missing";
            Check(!LevelValidator.HasErrors(LevelValidator.Validate(level, book)), "完整配对不应报错。");
            level.cells[4].ruleId = Presets.Turn;
            level.cells[4].bindings.Add(new BindingData { key = "switches", cellIds = new List<string> { "deleted-cell" } });
            Check(LevelValidator.Validate(level, book).Exists(x => x.cellIndex == 4 && x.message.Contains("已删除")), "裁剪或删除关联应报告失效引用。");
            level.cells[4].bindings[0] = Bind(level, "switches", 0);
            level.player = 1;
            Check(LevelValidator.Validate(level, book).Exists(x => x.message.Contains("重叠")), "人物箱子重叠必须阻止编译。");
        }

        static void ValidationInputBounds()
        {
            var level = Board(5, 2, 0, new[] { 1 }, 9);
            Action<Action<RuleBookData>, string> rejects = (mutate, message) =>
            {
                var candidate = Presets.CreateBook(); mutate(candidate);
                Check(LevelValidator.HasErrors(LevelValidator.Validate(level, candidate)), message);
            };
            rejects(b => b.Find(Presets.Ice).onEnter[0].actions[0].direction = (Direction)99, "无效移动方向必须被拒绝。");
            rejects(b => b.Find(Presets.Ice).onEnter[0].actions[0].directionMode = (DirectionMode)(-1), "无效方向来源必须被拒绝。");
            rejects(b => b.Find(Presets.Plain).allowedActors = (ActorMask)8, "无效进入者掩码必须被拒绝。");
            rejects(b => b.Find(Presets.Plate).signalActors = (ActorMask)(-1), "无效信号掩码必须被拒绝。");
            rejects(b => b.Find(Presets.Ice).onEnter[0].condition.nodes[0].actorMask = (ActorMask)4, "无效条件对象掩码必须被拒绝。");
            rejects(b => { var n = b.Find(Presets.Plain).canEnter.nodes[0]; n.kind = ConditionKind.Occupied; n.threshold = 0; }, "占用数量不能为零。");
            rejects(b => { var n = b.Find(Presets.Plain).canEnter.nodes[0]; n.kind = ConditionKind.SignalCount; n.threshold = -1; }, "信号数量不能为负数。");
            rejects(b => b.Find(Presets.Plain).visual = (TerrainVisual)123, "未知地面类型必须被拒绝。");
            rejects(b => { var n = b.Find(Presets.Plain).canEnter.nodes[0]; n.kind = ConditionKind.Occupied; n.binding = " "; }, "隐藏的空白关联不能通过校验。");
            var edges = level.Clone(); edges.edges.Add(new EdgeData { cell = 2, direction = Direction.North, kind = (EdgeKind)123 });
            Check(LevelValidator.Validate(edges, Presets.CreateBook()).Exists(x => x.message.Contains("边界类型")), "未知边界类型必须被拒绝。");
            edges.edges[0].kind = EdgeKind.Wall; edges.edges[0].direction = (Direction)(-1);
            Check(LevelValidator.Validate(edges, Presets.CreateBook()).Exists(x => x.message.Contains("方向无效")), "未知边界方向必须被拒绝。");
            var allowed = Presets.CreateBook(); var disabled = new RuleDefinition { id = "closed-tile", name = "关闭地块", allowedActors = ActorMask.None }; allowed.rules.Add(disabled);
            Check(!LevelValidator.HasErrors(LevelValidator.Validate(level, allowed)), "None 是合法的禁止进入配置，不应误报。");
        }

        static void ValidationProtectedTargets()
        {
            var book = Presets.CreateBook(); var level = Board(6, 2, 0, new[] { 1 }, 11);
            var change = Change(book, "change-linked", Presets.Hole, "targets"); level.cells[2].ruleId = change.id;
            level.cells[2].bindings.Add(Bind(level, "targets", 11));
            Check(LevelValidator.Validate(level, book).Exists(x => x.cellIndex == 2 && x.message.Contains("不能覆盖")), "覆盖目标的操作应定位到规则来源。");
            level.cells[3].ruleId = Presets.PortalEntrance; level.cells[3].portalPair = "pair";
            level.cells[4].ruleId = Presets.PortalExit; level.cells[4].portalPair = "pair";
            level.cells[2].bindings[0] = Bind(level, "targets", 3, 4);
            Check(LevelValidator.Validate(level, book).FindAll(x => x.cellIndex == 2 && x.message.Contains("不能覆盖")).Count == 2, "入口与出口均应受到保护。");
            level.cells[2].bindings[0] = Bind(level, "targets", 5);
            Check(!LevelValidator.HasErrors(LevelValidator.Validate(level, book)), "合法的关联地形变化不应被过度限制。");
            var exit = book.Find(Presets.PortalExit).Clone(); exit.id = "self-overwrite-exit";
            exit.onEnter.Add(new RuleBranch { actions = new List<RuleAction> { new RuleAction { kind = ActionKind.ChangeTile, targetRuleId = Presets.Plain } } });
            book.rules.Add(exit); level.cells[4].ruleId = exit.id;
            Check(LevelValidator.Validate(level, book).Exists(x => x.cellIndex == 4 && x.message.Contains("不能覆盖")), "自我覆写传送端点也应报错。");
            level.cells[4].ruleId = Presets.PortalExit; book.rules.Remove(exit);
            book.Find(Presets.PortalEntrance).onEnter[0].actions[0].targetBinding = "invisible-old-binding";
            Check(LevelValidator.Validate(level, book).Exists(x => x.message.Contains("传送仅使用配对出口")), "隐藏目标引用不能绕过传送配对。");
        }

        static void DynamicBindingAnalysis()
        {
            var book = Presets.CreateBook(); var level = Board(6, 2, 0, new[] { 6 }, 11);
            var a = Change(book, "replacement-a", "replacement-b", "next");
            var b = Change(book, "replacement-b", Presets.Turn, "third");
            level.cells[1].ruleId = a.id; level.cells[1].bindings.Add(Bind(level, "next", 2));
            level.cells[2].bindings.Add(Bind(level, "third", 3));
            level.cells[4].ruleId = Presets.Plate;
            var possible = RuleAnalysis.PossibleDefinitions(level, book);
            Check(possible[2].Exists(x => x.id == b.id) && possible[3].Exists(x => x.id == Presets.Turn), "远程连续变换应传播潜在定义。");
            Check(RuleAnalysis.BindingKeysForInstance(level, book, 2).Contains("third"), "第二个变换目标应暴露其后续关联键。");
            Check(RuleAnalysis.BindingKeysForInstance(level, book, 3).Contains("switches"), "尚为普通地面的第三个格子应显示未来转向规则的按钮关联。");
            Check(LevelValidator.Validate(level, book).Exists(x => x.cellIndex == 3 && x.ruleId == Presets.Turn && x.message.Contains("switches")), "编译器应在实际目标格定位未来缺失的关联。");
            level.cells[3].bindings.Add(Bind(level, "switches", 4));
            Check(!LevelValidator.HasErrors(LevelValidator.Validate(level, book)), "补齐未来定义的关联后应可编译。");
            // A future leave event returns to the original source: analysis must terminate on this graph cycle.
            book.Find(Presets.Turn).onLeave.Add(new RuleBranch { actions = new List<RuleAction>
            { new RuleAction { kind = ActionKind.ChangeTile, targetRuleId = a.id, targetBinding = "back" } } });
            level.cells[3].bindings.Add(Bind(level, "back", 1));
            possible = RuleAnalysis.PossibleDefinitions(level, book);
            Check(possible[1].FindAll(x => x.id == a.id).Count == 1, "环状变换图不能重复访问同一格子/规则。");
            Check(!LevelValidator.HasErrors(LevelValidator.Validate(level, book)), "静态分析不能因为存在定义循环就断言运行必然循环。");
            level.cells[2].bindings[0] = Bind(level, "third", 11);
            Check(LevelValidator.Validate(level, book).Exists(x => x.cellIndex == 2 && x.ruleId == b.id && x.message.Contains("不能覆盖")), "潜在规则也不能覆写固定目标。");
            var selfBook = Presets.CreateBook(); var selfLevel = Board(5, 2, 0, new[] { 1 }, 9);
            var self = Change(selfBook, "self-becomes-turn", Presets.Turn, ""); selfLevel.cells[2].ruleId = self.id;
            Check(RuleAnalysis.BindingKeysForInstance(selfLevel, selfBook, 2).Contains("switches"), "自我替换应继承实例关联配置需求。");
            Check(LevelValidator.Validate(selfLevel, selfBook).Exists(x => x.cellIndex == 2 && x.ruleId == Presets.Turn), "自我替换缺少关联应在本格报告。");
        }

        static void Drain(SokobanSolver solver)
        {
            for (int i = 0; i < 1000 && solver.Status == SolveStatus.Searching; i++) solver.Step(100);
            Check(solver.Status != SolveStatus.Searching, "小测试求解未能结束。");
        }
        static void SolverSolved()
        {
            var book = Presets.CreateBook(); var level = Board(5, 1, 0, new[] { 1 }, 4);
            var solver = new SokobanSolver(level, book, 1000, 30000); Drain(solver);
            Check(solver.Status == SolveStatus.Solved && solver.Solution.Count == 3, "简单三次推动应找到最短解。");
            var state = RuleEngine.CreateState(level);
            foreach (var direction in solver.Solution) state = Step(level, book, state, direction).state;
            Check(state.status == GameStatus.Won, "求解路径必须能在同一运行内核实际通关。");
            var fragile = Board(4, 2, 0, new[] { 1 }, 2); fragile.cells[1].ruleId = Presets.Fragile;
            var altered = new SokobanSolver(fragile, book, 1000, 30000); Drain(altered);
            Check(altered.Status == SolveStatus.Solved && altered.Solution.Count == 1, "求解器必须包含坍塌规则。");
        }

        static void SolverConclusions()
        {
            var book = Presets.CreateBook(); var unsolved = Board(3, 1, 0, new[] { 2 }, 1);
            var search = new SokobanSolver(unsolved, book, 1000, 30000); Drain(search);
            Check(search.Status == SolveStatus.Unsolvable, "穷尽有限状态后才判无解。");
            var possible = Board(5, 1, 0, new[] { 1 }, 4);
            var limit = new SokobanSolver(possible, book, 1, 30000); Drain(limit);
            Check(limit.Status == SolveStatus.Unknown, "状态数量上限必须返回未知，不是无解。");
            var loop = new SokobanSolver(LoopBoard(book), book, 1000, 30000); Drain(loop);
            Check(loop.Status == SolveStatus.Unknown, "循环保护中止的分支不能当作无解证明。");
            var cancelled = new SokobanSolver(possible, book, 1000, 30000); cancelled.Cancel(); cancelled.Step();
            Check(cancelled.Status == SolveStatus.Cancelled, "取消不能变成无解。");
            var invalid = possible.Clone(); invalid.player = -1;
            Check(new SokobanSolver(invalid, book).Status == SolveStatus.Invalid, "无效关卡不能开始搜索。");
        }

        static void SearchStateKeys()
        {
            var book = Presets.CreateBook(); var level = Board(5, 2, 0, new[] { 1, 3 }, 8, 9);
            var initial = RuleEngine.CreateState(level); var different = initial.Clone(); different.tiles[2] = Presets.Hole;
            Check(initial.GetKey() != different.GetKey(), "不同坍塌状态不能合并。");
            var equivalent = initial.Clone(); equivalent.boxes.Reverse(); equivalent.moves = 99; equivalent.pushes = 55;
            Check(initial.GetKey() == equivalent.GetKey(), "相同箱子换序或计数变化不应创造新局面。");
            var solver = new SokobanSolver(level, book, 1000, 30000);
            var key = typeof(SokobanSolver).GetMethod("Key", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(key != null, "缺少搜索状态键。");
            string a = (string)key.Invoke(solver, new object[] { initial });
            string b = (string)key.Invoke(solver, new object[] { different });
            string c = (string)key.Invoke(solver, new object[] { equivalent });
            Check(a != b && a == c, "求解器去重必须保留地块状态并忽略计数、箱子顺序。");
        }

        static void AtomicPressureHandoff()
        {
            var book = Presets.CreateBook(); var level = Board(6, 2, 0, new[] { 1 }, 5); level.cells[1].ruleId = Presets.Plate;
            var gate = new EdgeData { cell = 0, direction = Direction.East, kind = EdgeKind.Gate, ruleId = Presets.Gate };
            gate.bindings.Add(Bind(level, "switches", 1)); level.edges.Add(gate);
            var result = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(result.changed && result.state.player == 1 && result.state.boxes[0] == 2, "人物应穿过本次推动开始时已打开的门，接替箱子压板。");
            foreach (var frame in result.frames)
                Check(RuleEngine.IsSignalActive(level, book, frame, 1) && RuleEngine.IsGateOpen(level, book, frame, gate), "普通接替不能生成松板或关门的中间帧。");
            level.edges.Clear(); level.cells[1].ruleId = Presets.Fragile;
            result = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(result.state.player == 0 && result.state.tiles[1] == Presets.Hole && result.state.boxes[0] == 2, "原子压力判定不能破坏脆地离开立即坍塌的规则。");
        }

        static void PlayerMovementRules()
        {
            var book = PlayerRules.CreateBook();
            Check(book.Find(PlayerRules.Ice).onEnter[0].actions[0].distance == 1 && Presets.CreateBook().Find(Presets.Ice).onEnter[0].actions[0].distance == 3, "玩家冰面不能改动旧版模板。");
            var level = Board(8, 2, 0, new[] { 1 }, 7);
            level.cells[2].ruleId = PlayerRules.Ice;
            var single = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(single.state.boxes[0] == 3, "单块冰只额外前进一格。");
            level.cells[3].ruleId = PlayerRules.Ice; level.cells[4].ruleId = PlayerRules.Ice;
            var chain = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(chain.state.boxes[0] == 5, "连续冰应逐格触发，并停在第一块普通地面。");
            level.cells[3].ruleId = PlayerRules.Arrow;
            var redirected = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(redirected.state.boxes[0] == 11, "遇到向上箭头应替换滑行方向并前进一步。");
            level.player = 2; level.boxes[0] = 6;
            var player = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(player.state.player == 3, "人物进入箭头不额外移动。");
        }

        static void PlayerSelectiveChannels()
        {
            var book = PlayerRules.CreateBook(); var human = book.Find(PlayerRules.Selective); var cargo = book.Find(PlayerRules.BoxOnly);
            Check(PlayerRules.Selective == "selective" && human.allowedActors == ActorMask.Player && cargo.allowedActors == ActorMask.Box,
                "人物通道必须保留旧ID与准入；箱子通道必须使用独立预设。");
            Check(cargo.visual == TerrainVisual.Selective && !cargo.boundary && Presets.CreateBook().Find(PlayerRules.BoxOnly) == null,
                "箱子通道复用选择性地面能力，不改动旧作者模板。");
            var level = Board(5, 2, 0, new[] { 1 }, 9); level.cells[2].ruleId = PlayerRules.Selective;
            Check(!Step(level, book, RuleEngine.CreateState(level, book), Direction.East).changed, "箱子不能被推入人物通道。");
            var walker = level.Clone(); walker.player = 1; walker.boxes[0] = 8;
            Check(Step(walker, book, RuleEngine.CreateState(walker, book), Direction.East).state.player == 2, "人物应能正常进入人物通道。");
            level.cells[2].ruleId = PlayerRules.BoxOnly;
            var entered = Step(level, book, RuleEngine.CreateState(level, book), Direction.East);
            Check(entered.changed && entered.state.player == 1 && entered.state.boxes[0] == 2, "箱子可进入箱子通道，人物先跟进原普通格。");
            var left = Step(level, book, entered.state, Direction.East);
            Check(left.changed && left.state.player == 1 && left.state.boxes[0] == 3 && left.state.tiles[2] == PlayerRules.BoxOnly,
                "箱子离开通道后地形保持不变，人物不能跟进。");
            foreach (var frame in left.frames) Check(frame.player != 2, "自动表现快照也不能让人物短暂进入箱子通道。");
            Check(!Step(level, book, left.state, Direction.East).changed, "通道空置后人物仍不能步行进入。");
            var occupied = level.Clone(); occupied.boxes[0] = 2;
            Check(!LevelValidator.HasErrors(PlayerRules.Validate(occupied)), "箱子初始位于箱子通道应通过结构校验。");
            occupied.player = 2; occupied.boxes[0] = 1;
            Check(LevelValidator.HasErrors(PlayerRules.Validate(occupied)), "人物初始位于箱子通道必须报错。");
            occupied.player = 0; occupied.boxes[0] = 2; occupied.cells[2].ruleId = PlayerRules.Selective;
            Check(LevelValidator.HasErrors(PlayerRules.Validate(occupied)), "箱子初始位于人物通道必须报错。");
        }

        static void BoxOnlyAutomaticMovement()
        {
            var book = PlayerRules.CreateBook(); var level = Board(6, 2, 0, new[] { 1 }, 5);
            level.cells[2].ruleId = PlayerRules.Ice; level.cells[3].ruleId = PlayerRules.BoxOnly;
            var initial = RuleEngine.CreateState(level, book); var undo = initial.Clone(); string original = initial.GetKey();
            var moved = Step(level, book, initial, Direction.East);
            Check(moved.changed && moved.state.boxes[0] == 3 && moved.state.player == 1 && moved.state.pushes == 1,
                "冰面自动移动应能进入箱子通道，并按一格额外移动停止。");
            Check(initial.GetKey() == original && undo.GetKey() == original && undo.moves == 0 && undo.tiles[3] == PlayerRules.BoxOnly,
                "进入通道不能修改原状态或撤销快照。");
            Check(Step(level, book, undo, Direction.East).state.GetKey() == moved.state.GetKey(), "撤销后重放必须恢复同一通道局面。");
            level.edges.Add(new EdgeData { cell = 2, direction = Direction.East, kind = EdgeKind.Wall });
            Check(Step(level, book, initial, Direction.East).state.boxes[0] == 2, "箱子通道不能使自动移动穿过公共边墙。");
            level.edges.Clear(); level.boxes.Add(3);
            var occupied = Step(level, book, RuleEngine.CreateState(level, book), Direction.East);
            Check(occupied.state.boxes[0] == 2 && occupied.state.boxes[1] == 3, "自动进入箱子通道遇箱应停下，不连推。");
            level.boxes.RemoveAt(1); level.cells[3].ruleId = PlayerRules.Selective;
            Check(Step(level, book, RuleEngine.CreateState(level, book), Direction.East).state.boxes[0] == 2,
                "人物通道的箱子禁入也必须约束冰面自动移动。");
        }

        static void BoxOnlyLesson()
        {
            var levels = PlayerRules.CreateDemoLevels(); var ids = new HashSet<string>();
            foreach (var level in levels) Check(ids.Add(level.id), "新增教程不能产生重复的存档编号。");
            for (int old = 1; old <= 9; old++)
            {
                string id = "boxlab-player-lesson-" + old.ToString("00");
                int display = old <= 6 ? old : old + 1;
                var preserved = levels.Find(level => level.id == id);
                Check(preserved != null && preserved.name.StartsWith(display.ToString("00") + " · "), "原教程ID必须保持、显示编号应顺延。");
            }
            for (int old = 1; old <= 2; old++)
                Check(levels.Find(level => level.id == "boxlab-player-challenge-" + old.ToString("00")).name.StartsWith((old + 10).ToString("00") + " · "),
                    "组合教程只改变显示编号，不能改变存档ID。");
            var lesson = levels.Find(level => level.id == "boxlab-player-lesson-box-only");
            Check(lesson != null && levels.IndexOf(lesson) == 6 && lesson.name.StartsWith("07 · "), "箱子通道应在人物通道后单独教学。");
            var book = PlayerRules.CreateBook(); var state = RuleEngine.CreateState(lesson, book);
            state = Step(lesson, book, state, Direction.East).state;
            Check(state.boxes[0] == lesson.Index(2, 1), "教程第一推应先展示箱子进入专用通道。");
            state = Step(lesson, book, state, Direction.East).state;
            Check(state.boxes[0] == lesson.Index(3, 1) && state.player == lesson.Index(1, 1) && state.status == GameStatus.Playing,
                "第二推必须展示人物留在原格，而且关卡尚未完成。");
            Check(!Step(lesson, book, state, Direction.East).changed, "教程应明确挡住人物跟随箱子的直线路线。");
            foreach (var direction in new[] { Direction.West, Direction.South, Direction.East, Direction.East, Direction.East, Direction.North })
            { var step = Step(lesson, book, state, direction); Check(step.changed, "教程绕行解法不应包含受阻步骤。"); state = step.state; }
            Check(state.status == GameStatus.Won && state.pushes == 3, "绕行换侧后应将箱子送上目标。");
        }

        static LevelData RotationBoard()
        {
            var level = Board(5, 3, 0, new[] { 4 }, 14);
            level.cells[1].ruleId = PlayerRules.RotationPlate; level.cells[1].signalStyle = 3;
            level.cells[7].ruleId = PlayerRules.Arrow; level.cells[7].rotation = 3;
            level.cells[7].bindings.Add(Bind(level, PlayerRules.Switches, 1)); return level;
        }

        static void RotationPressEdges()
        {
            var book = PlayerRules.CreateBook(); var level = RotationBoard(); var initial = RuleEngine.CreateState(level);
            var press = Step(level, book, initial, Direction.East);
            Check(RuleEngine.GetRotation(level, press.state, 7) == 0, "首次压下应从西顺时针转向北。");
            var release = Step(level, book, press.state, Direction.West);
            Check(RuleEngine.GetRotation(level, release.state, 7) == 0, "离开踏板不能再转一次。");
            var again = Step(level, book, release.state, Direction.East);
            Check(RuleEngine.GetRotation(level, again.state, 7) == 1, "释放后重新压下应再次转90度。");
            level.player = 1;
            var occupiedInitially = RuleEngine.CreateState(level);
            Check(RuleEngine.GetRotation(level, occupiedInitially, 7) == 3, "开局已压住不算一次触发。");
            Check(RuleEngine.GetRotation(level, Step(level, book, occupiedInitially, Direction.West).state, 7) == 3, "开局压住再离开也不应旋转。");
            level.player = 0; level.boxes[0] = 1;
            var handoff = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(handoff.state.player == 1 && handoff.state.boxes[0] == 2 && RuleEngine.GetRotation(level, handoff.state, 7) == 3, "箱子离板人物接替应保持受压，不触发旋转。");
            foreach (var frame in handoff.frames) Check(RuleEngine.GetRotation(level, frame, 7) == 3 && RuleEngine.IsSignalActive(level, book, frame, 1), "接替帧不能闪烁或重新旋转。");
            level.cells[1].ruleId = PlayerRules.Plain; level.cells[2].ruleId = PlayerRules.RotationPlate;
            level.cells[7].bindings[0] = Bind(level, PlayerRules.Switches, 2);
            var boxPress = Step(level, book, RuleEngine.CreateState(level), Direction.East);
            Check(RuleEngine.GetRotation(level, boxPress.state, 7) == 0, "箱子新压旋转板应与人物一致。");
            var boxLeave = Step(level, book, boxPress.state, Direction.East);
            Check(RuleEngine.GetRotation(level, boxLeave.state, 7) == 0, "箱子离板后人物接替不能再次触发。");
        }

        static void RotationSnapshots()
        {
            var book = PlayerRules.CreateBook(); var level = RotationBoard(); var initial = RuleEngine.CreateState(level);
            var result = Step(level, book, initial, Direction.East);
            Check(initial.rotations[7] == 3 && result.state.rotations[7] == 0 && level.cells[7].rotation == 3, "旋转不能修改旧快照或作者方向。");
            var clone = result.state.Clone(); clone.rotations[7] = 2;
            Check(result.state.rotations[7] == 0, "撤销/保存快照必须深复制方向数组。");
            var samePosition = initial.Clone(); samePosition.rotations[7] = 0;
            Check(initial.GetKey() != samePosition.GetKey(), "方向不同的局面不能合并。");
            var solver = new SokobanSolver(level, book, 100, 30000);
            var key = typeof(SokobanSolver).GetMethod("Key", BindingFlags.Instance | BindingFlags.NonPublic);
            Check((string)key.Invoke(solver, new object[] { initial }) != (string)key.Invoke(solver, new object[] { samePosition }), "BFS键必须包含动态箭头朝向。");
        }

        static void PlayerConnectionValidation()
        {
            var book = PlayerRules.CreateBook(); var level = Board(5, 3, 0, new[] { 5, 6, 7 }, 10, 11, 12);
            foreach (int at in new[] { 5, 6, 7, 8 }) { level.cells[at].ruleId = PlayerRules.Plate; level.cells[at].signalStyle = at; }
            var gate = new EdgeData { cell = 0, direction = Direction.East, kind = EdgeKind.Gate, ruleId = PlayerRules.Gate };
            gate.bindings.Add(Bind(level, PlayerRules.Switches, 5, 6, 7)); level.edges.Add(gate);
            var state = RuleEngine.CreateState(level);
            Check(!LevelValidator.HasErrors(PlayerRules.Validate(level)) && RuleEngine.IsGateOpen(level, book, state, gate), "三个压力板全压下应可编译并开门。");
            state.boxes[2] = 9;
            Check(!RuleEngine.IsGateOpen(level, book, state, gate), "任意一个板没压住就应关门。");
            gate.bindings[0] = Bind(level, PlayerRules.Switches, 5, 6, 7, 8);
            Check(LevelValidator.HasErrors(PlayerRules.Validate(level)), "门不能关联四块板。");
            gate.bindings.Clear(); Check(LevelValidator.HasErrors(PlayerRules.Validate(level)), "没有关联的门只能保存草稿，不能试玩。");
            gate.bindings.Add(Bind(level, PlayerRules.Switches, 5)); level.cells[5].ruleId = PlayerRules.RotationPlate;
            Check(LevelValidator.HasErrors(PlayerRules.Validate(level)), "普通门不能接旋转踏板。");
            level.edges.Clear(); level.cells[2].ruleId = PlayerRules.Arrow;
            Check(!LevelValidator.HasErrors(PlayerRules.Validate(level)), "零连接箭头为合法固定方向箭头。");
            level.cells[2].bindings.Add(Bind(level, PlayerRules.Switches, 6));
            Check(LevelValidator.HasErrors(PlayerRules.Validate(level)), "箭头不能接普通压力板。");
            foreach (int at in new[] { 5, 6, 7, 8 }) level.cells[at].ruleId = PlayerRules.RotationPlate;
            level.cells[2].bindings[0] = Bind(level, PlayerRules.Switches, 5, 6, 7);
            Check(!LevelValidator.HasErrors(PlayerRules.Validate(level)), "箭头可连接最多三个旋转踏板。");
            level.cells[2].bindings[0] = Bind(level, PlayerRules.Switches, 5, 6, 7, 8);
            Check(LevelValidator.HasErrors(PlayerRules.Validate(level)), "箭头不能连接四个源。");
        }

        static void PlayerLessons()
        {
            var book = PlayerRules.CreateBook(); var levels = PlayerRules.CreateDemoLevels();
            Check(levels.Count == 12, "应有十二个新手教程，正式关卡由独立合集提供。");
            foreach (var level in levels)
            {
                var issues = PlayerRules.Validate(level);
                Check(!LevelValidator.HasErrors(issues), level.name + " 结构错误：" + string.Join(" / ", issues.ConvertAll(x => x.message).ToArray()));
                var solver = new SokobanSolver(level, book, 100000, 30000); Drain(solver);
                Check(solver.Status == SolveStatus.Solved, level.name + " 未找到解：" + solver.Message);
                var state = RuleEngine.CreateState(level);
                foreach (var direction in solver.Solution) state = Step(level, book, state, direction).state;
                Check(state.status == GameStatus.Won, level.name + " 解法回放不能通关。");
            }
        }
    }
}
