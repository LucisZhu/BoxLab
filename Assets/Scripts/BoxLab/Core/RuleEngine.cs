using System;
using System.Collections.Generic;
using System.Text;

namespace BoxLab
{
    /// <summary>All gameplay is evaluated here. Presentation, editor preview and solver share this pure engine.</summary>
    public static class RuleEngine
    {
        public const int MaxActionsPerInput = 512;

        public static BoardState CreateState(LevelData level)
        {
            var state = new BoardState { player = level.player, boxes = new List<int>(level.boxes), tiles = new string[level.width * level.height], rotations = new int[level.width * level.height], status = GameStatus.Playing };
            for (int i = 0; i < state.tiles.Length; i++)
            {
                state.tiles[i] = level.GetCell(i) == null ? Presets.Plain : level.GetCell(i).ruleId;
                state.rotations[i] = level.GetCell(i) == null ? 0 : NormalizeRotation(level.GetCell(i).rotation);
            }
            return state;
        }

        public static BoardState CreateState(LevelData level, RuleBookData book)
        {
            var state = CreateState(level); state.status = GetStatus(level, book, state); return state;
        }

        public static StepResult Step(LevelData level, RuleBookData book, BoardState state, Direction direction, bool captureFrames = true)
        {
            var result = new StepResult { state = state.Clone() };
            if (state.status != GameStatus.Playing) { result.message = "本局已经结束，可撤销或重开。"; return result; }
            var context = new Execution(level, book, result, captureFrames);
            int start = state.player;
            int destination = level.Neighbor(start, direction);
            if (destination < 0) { result.message = "地图边界阻挡。"; return result; }
            int box = result.state.BoxAt(destination);
            if (box >= 0)
            {
                int next = level.Neighbor(destination, direction);
                // The player must be able to initiate a push across the boundary. Following is checked again after leave effects.
                if (!CanCrossBoundary(level, book, result.state, start, destination, ActorMask.Player) ||
                    !CanEnter(level, book, result.state, destination, next, ActorMask.Box))
                {
                    result.message = "箱子无法沿该方向移动。"; return result;
                }
                context.Log("推动箱子：" + Label(level, destination) + " → " + Label(level, next));
                var signalsBeforePush = context.CaptureRotationSignals();
                context.Relocate(box, next, direction, false);
                // The crossing was permitted at the start of this compound push. Do not let
                // the temporary box-to-player handoff release its own gate. Leave effects
                // (in particular collapse) still change the destination before following.
                if (!context.failed && CanOccupy(level, book, result.state, destination, ActorMask.Player))
                {
                    context.Relocate(-1, destination, direction, false);
                }
                else if (!context.failed) context.Log("原格不再允许进入，人物留在原位。");
                if (!context.failed) { context.ResolveRotationSignals(signalsBeforePush); context.Capture(); }
                if (!context.failed) context.ResolveEntry(box, direction);
                if (!context.failed && result.state.status != GameStatus.Lost && result.state.player != start) context.ResolveEntry(-1, direction);
                result.state.pushes++;
            }
            else
            {
                if (!CanEnter(level, book, result.state, start, destination, ActorMask.Player))
                {
                    result.message = "该方向不可进入。"; return result;
                }
                context.Relocate(-1, destination, direction);
                if (!context.failed) context.ResolveEntry(-1, direction);
            }
            if (context.failed)
            {
                result.state = state.Clone(); result.changed = false; result.interrupted = true; result.frames.Clear();
                result.message = context.failure; context.Log("已撤回整次输入，地图保持操作前状态。"); return result;
            }
            result.state.moves++;
            result.state.status = GetStatus(level, book, result.state);
            result.changed = true;
            result.message = result.state.status == GameStatus.Won ? "所有目标都已被箱子覆盖，关卡完成！" : result.state.status == GameStatus.Lost ? "箱子已掉入空洞或被移除，剩余数量不足。可撤销或重开。" : "";
            context.Capture();
            return result;
        }

        public static GameStatus GetStatus(LevelData level, RuleBookData book, BoardState state)
        {
            int goals = 0, filled = 0, alive = 0;
            for (int i = 0; i < state.tiles.Length; i++)
            {
                var rule = book.Find(state.tiles[i]);
                if (rule != null && rule.visual == TerrainVisual.Goal) { goals++; if (state.BoxAt(i) >= 0) filled++; }
            }
            foreach (int position in state.boxes) if (position >= 0) alive++;
            if (alive < goals) return GameStatus.Lost;
            if (goals > 0 && filled == goals) return GameStatus.Won;
            return GameStatus.Playing;
        }

        public static bool CanEnter(LevelData level, RuleBookData book, BoardState state, int from, int to, ActorMask actor)
        {
            return CanTraverse(level, book, state, from, to, actor, false);
        }

        static bool CanTraverse(LevelData level, RuleBookData book, BoardState state, int from, int to, ActorMask actor, bool ignoreOccupant)
        {
            return CanCrossBoundary(level, book, state, from, to, actor) && CanOccupy(level, book, state, to, actor, ignoreOccupant);
        }

        public static bool CanCrossBoundary(LevelData level, RuleBookData book, BoardState state, int from, int to, ActorMask actor)
        {
            if (!level.Contains(from) || !level.Contains(to)) return false;
            Direction direction;
            if (!TryDirection(level, from, to, out direction)) return false;
            var edge = level.FindEdge(from, direction);
            if (edge != null && (edge.kind == EdgeKind.Wall || (edge.kind == EdgeKind.Gate && !IsGateOpen(level, book, state, edge, actor)))) return false;
            return true;
        }

        public static bool CanOccupy(LevelData level, RuleBookData book, BoardState state, int cell, ActorMask actor, bool ignoreOccupant = false)
        {
            if (!level.Contains(cell) || cell >= state.tiles.Length) return false;
            var rule = book.Find(state.tiles[cell]);
            if (rule == null || rule.boundary || (rule.allowedActors & actor) == 0) return false;
            // Holes always exclude the player, even if an author accidentally enables its actor flag.
            if (rule.visual == TerrainVisual.Hole && (actor & ActorMask.Player) != 0) return false;
            if (!Evaluate(level, book, state, rule.canEnter, cell, actor, level.GetCell(cell).bindings)) return false;
            return ignoreOccupant || state.Occupant(cell) == ActorMask.None;
        }

        public static bool IsGateOpen(LevelData level, RuleBookData book, BoardState state, EdgeData edge, ActorMask actor = ActorMask.Both)
        {
            if (edge == null || edge.kind == EdgeKind.Open) return true;
            if (edge.kind == EdgeKind.Wall) return false;
            if (actor == ActorMask.Both) return IsGateOpen(level, book, state, edge, ActorMask.Player) || IsGateOpen(level, book, state, edge, ActorMask.Box);
            var rule = book.Find(edge.ruleId);
            return rule != null && rule.boundary && (rule.allowedActors & actor) != 0 && Evaluate(level, book, state, rule.canEnter, edge.cell, actor, edge.bindings);
        }

        public static bool IsSignalActive(LevelData level, RuleBookData book, BoardState state, int cell)
        {
            if (!level.Contains(cell) || cell >= state.tiles.Length) return false;
            var rule = book.Find(state.tiles[cell]);
            return rule != null && (rule.signalActors & state.Occupant(cell)) != 0;
        }

        public static int GetRotation(LevelData level, BoardState state, int cell)
        {
            if (state != null && state.rotations != null && cell >= 0 && cell < state.rotations.Length) return NormalizeRotation(state.rotations[cell]);
            var data = level == null ? null : level.GetCell(cell);
            return data == null ? 0 : NormalizeRotation(data.rotation);
        }

        static int NormalizeRotation(int rotation) { return ((rotation % 4) + 4) % 4; }

        public static bool Evaluate(LevelData level, RuleBookData book, BoardState state, ConditionSet condition, int self, ActorMask actor, List<BindingData> bindings = null)
        {
            if (condition == null || condition.nodes == null || condition.nodes.Count == 0) return true;
            if (bindings == null && level.GetCell(self) != null) bindings = level.GetCell(self).bindings;
            return EvaluateNode(level, book, state, condition, 0, self, actor, bindings, 0);
        }

        static bool EvaluateNode(LevelData level, RuleBookData book, BoardState state, ConditionSet set, int index, int self, ActorMask actor, List<BindingData> bindings, int depth)
        {
            if (depth > 64 || index < 0 || index >= set.nodes.Count || set.nodes[index] == null) return false;
            var node = set.nodes[index]; bool value = false;
            switch (node.kind)
            {
                case ConditionKind.Always: value = true; break;
                case ConditionKind.ActorIs: value = (node.actorMask & actor) != 0; break;
                case ConditionKind.Occupied:
                case ConditionKind.SignalCount:
                case ConditionKind.AllSignals:
                    int matching = 0;
                    var targets = ResolveBinding(level, self, bindings, node.binding);
                    foreach (int target in targets)
                    {
                        if (node.kind != ConditionKind.Occupied ? IsSignalActive(level, book, state, target) : (state.Occupant(target) & node.actorMask) != 0) matching++;
                    }
                    value = node.kind == ConditionKind.AllSignals ? targets.Count > 0 && matching == targets.Count : matching >= node.threshold; break;
                case ConditionKind.All:
                case ConditionKind.Any:
                case ConditionKind.AtLeast:
                    int children = 0, passed = 0;
                    // Editor stores parents before their children; this also prevents malformed cyclic trees from recursing forever.
                    for (int i = index + 1; i < set.nodes.Count; i++) if (set.nodes[i] != null && set.nodes[i].parent == index)
                    {
                        children++;
                        if (EvaluateNode(level, book, state, set, i, self, actor, bindings, depth + 1)) passed++;
                    }
                    value = children > 0 && (node.kind == ConditionKind.All ? passed == children : node.kind == ConditionKind.Any ? passed > 0 : passed >= node.threshold);
                    break;
            }
            return node.negate ? !value : value;
        }

        public static List<int> ResolveBinding(LevelData level, int self, List<BindingData> bindings, string key)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(key)) { if (level.Contains(self)) result.Add(self); return result; }
            if (bindings == null) return result;
            foreach (var binding in bindings)
            {
                if (binding == null || binding.key != key || binding.cellIds == null) continue;
                foreach (string id in binding.cellIds)
                {
                    int index = level.FindCellIndex(id);
                    if (index >= 0 && !result.Contains(index)) result.Add(index);
                }
            }
            return result;
        }

        static bool TryDirection(LevelData level, int from, int to, out Direction direction)
        {
            for (int i = 0; i < 4; i++) if (level.Neighbor(from, (Direction)i) == to) { direction = (Direction)i; return true; }
            direction = Direction.North; return false;
        }
        static string Label(LevelData level, int cell) { return level.Contains(cell) ? level.Position(cell).ToString() : "地图外"; }

        sealed class Command
        {
            public RuleAction action;
            public int source, remaining;
            public Direction incoming;
            public Command(RuleAction action, int source, Direction incoming) { this.action = action; this.source = source; this.incoming = incoming; remaining = action.distance; }
        }

        sealed class Execution
        {
            readonly LevelData level;
            readonly RuleBookData book;
            readonly StepResult result;
            readonly bool capture;
            int count;
            public bool failed;
            public string failure;
            BoardState State { get { return result.state; } }
            public Execution(LevelData level, RuleBookData book, StepResult result, bool capture) { this.level = level; this.book = book; this.result = result; this.capture = capture; }
            public void Log(string value) { if (capture) result.trace.Add(value); }
            public void Capture() { if (capture) result.frames.Add(State.Clone()); }
            void Fail(string reason) { failed = true; failure = reason; Log(reason); }
            bool Budget()
            {
                if (++count <= MaxActionsPerInput) return true;
                Fail("连锁效果超过 " + MaxActionsPerInput + " 步，可能存在循环。请检查规则关联。"); return false;
            }
            int Position(int actor) { return actor < 0 ? State.player : actor < State.boxes.Count ? State.boxes[actor] : -1; }
            ActorMask Mask(int actor) { return actor < 0 ? ActorMask.Player : ActorMask.Box; }

            public void Relocate(int actor, int destination, Direction incoming, bool settle = true)
            {
                var signalsBefore = settle ? CaptureRotationSignals() : null;
                int source = Position(actor);
                string oldRule = State.tiles[source];
                if (actor < 0) State.player = destination; else State.boxes[actor] = destination;
                Log((actor < 0 ? "人物" : "箱子") + "移动 " + Label(level, source) + " → " + Label(level, destination));
                var definition = book.Find(oldRule);
                var leave = Select(definition == null ? null : definition.onLeave, source, actor, incoming);
                foreach (var command in leave)
                {
                    if (!Budget()) return;
                    if (command.action.kind != ActionKind.ChangeTile) { Fail("离开事件只允许改变地块，请检查 " + Label(level, source) + " 的规则。"); return; }
                    ChangeTile(command);
                    if (failed) return;
                }
                if (settle) { ResolveRotationSignals(signalsBefore); Capture(); }
            }

            public bool[] CaptureRotationSignals()
            {
                bool any = false;
                foreach (var definition in book.rules) if (definition != null && definition.visual == TerrainVisual.RotationPlate) { any = true; break; }
                if (!any) return null;
                var active = new bool[State.tiles.Length];
                for (int i = 0; i < active.Length; i++)
                {
                    var rule = book.Find(State.tiles[i]);
                    active[i] = rule != null && rule.visual == TerrainVisual.RotationPlate && IsSignalActive(level, book, State, i);
                }
                return active;
            }

            public void ResolveRotationSignals(bool[] before)
            {
                if (before == null) return;
                for (int plate = 0; plate < State.tiles.Length; plate++)
                {
                    var plateRule = book.Find(State.tiles[plate]);
                    if (plateRule == null || plateRule.visual != TerrainVisual.RotationPlate || (plate < before.Length && before[plate]) || !IsSignalActive(level, book, State, plate)) continue;
                    string id = level.GetCell(plate).id;
                    for (int arrow = 0; arrow < State.tiles.Length; arrow++)
                    {
                        var arrowRule = book.Find(State.tiles[arrow]);
                        if (arrowRule == null || arrowRule.visual != TerrainVisual.Turn) continue;
                        var bindings = level.GetCell(arrow).bindings;
                        bool connected = false;
                        if (bindings != null) foreach (var binding in bindings)
                            if (binding != null && binding.key == "switches" && binding.cellIds != null && binding.cellIds.Contains(id)) { connected = true; break; }
                        if (!connected) continue;
                        if (State.rotations == null || State.rotations.Length != State.tiles.Length)
                        {
                            var rotations = new int[State.tiles.Length];
                            for (int i = 0; i < rotations.Length; i++) rotations[i] = GetRotation(level, State, i);
                            State.rotations = rotations;
                        }
                        State.rotations[arrow] = (GetRotation(level, State, arrow) + 1) % 4;
                        Log("旋转踏板 " + Label(level, plate) + " 激活，箭头 " + Label(level, arrow) + " 顺时针旋转 90°。");
                    }
                }
            }

            List<Command> Select(List<RuleBranch> branches, int cell, int actor, Direction incoming)
            {
                var commands = new List<Command>();
                if (branches == null || !level.Contains(cell)) return commands;
                foreach (var branch in branches)
                {
                    if (branch == null || !Evaluate(level, book, State, branch.condition, cell, Mask(actor), level.GetCell(cell).bindings)) continue;
                    if (branch.actions != null) foreach (var action in branch.actions) if (action != null) commands.Add(new Command(action, cell, incoming));
                    break; // Ordered branches, one selected branch per event; Always is the explicit else branch.
                }
                return commands;
            }

            List<Command> Enter(int actor, Direction incoming)
            {
                int position = Position(actor);
                if (!level.Contains(position)) return new List<Command>();
                var definition = book.Find(State.tiles[position]);
                var commands = Select(definition == null ? null : definition.onEnter, position, actor, incoming);
                if (commands.Count > 0) Log("进入 " + Label(level, position) + "，触发「" + definition.name + "」。");
                return commands;
            }

            void PrependEntry(List<Command> queue, int actor, Direction incoming)
            {
                var entered = Enter(actor, incoming);
                foreach (var command in entered) if (command.action.kind == ActionKind.Move || command.action.kind == ActionKind.Teleport) { queue.Clear(); break; }
                queue.InsertRange(0, entered);
            }

            public void ResolveEntry(int actor, Direction incoming)
            {
                var queue = Enter(actor, incoming);
                var visited = new HashSet<string>();
                while (queue.Count > 0 && !failed && State.status != GameStatus.Lost && Position(actor) >= 0)
                {
                    if (!Budget()) return;
                    var key = new StringBuilder(State.GetKey()).Append('|').Append(actor).Append('|');
                    foreach (var pending in queue) key.Append((int)pending.action.kind).Append(',').Append(pending.source).Append(',').Append(pending.remaining).Append(',').Append((int)pending.incoming).Append(',').Append((int)pending.action.directionMode).Append(',').Append((int)pending.action.direction).Append(',').Append(pending.action.targetRuleId).Append(',').Append(pending.action.targetBinding).Append(',').Append(pending.action.directionalPortal).Append(';');
                    if (!visited.Add(key.ToString())) { Fail("在 " + Label(level, Position(actor)) + " 检测到重复连锁状态，已停止循环。"); return; }
                    var command = queue[0]; queue.RemoveAt(0);
                    switch (command.action.kind)
                    {
                        case ActionKind.ChangeTile:
                            var signalsBeforeChange = CaptureRotationSignals();
                            ChangeTile(command);
                            if (!failed) ResolveRotationSignals(signalsBeforeChange);
                            Capture(); break;
                        case ActionKind.RemoveBox:
                            if (actor >= 0)
                            {
                                State.boxes[actor] = -1; State.status = GetStatus(level, book, State);
                                Log("箱子被移除。可撤销恢复。"); Capture(); queue.Clear();
                            }
                            break;
                        case ActionKind.Move:
                            if (command.remaining <= 0) break;
                            Direction direction = command.action.directionMode == DirectionMode.Incoming ? command.incoming : (Direction)(((int)command.action.direction + GetRotation(level, State, command.source)) % 4);
                            int destination = level.Neighbor(Position(actor), direction);
                            if (!CanEnter(level, book, State, Position(actor), destination, Mask(actor))) { Log("自动移动遇到阻挡，停止。"); break; }
                            Relocate(actor, destination, direction);
                            if (failed) return;
                            command.remaining--;
                            if (command.remaining > 0) queue.Insert(0, command);
                            PrependEntry(queue, actor, direction);
                            break;
                        case ActionKind.Teleport:
                            if (actor < 0) { Log("人物不触发传送。"); break; }
                            if (command.action.directionalPortal && command.incoming != (Direction)((GetRotation(level, State, command.source) + 2) % 4))
                            { Log("箱子未从入口正面进入，不触发传送。"); break; }
                            int target = PortalTarget(command);
                            if (target < 0 || !CanOccupy(level, book, State, target, Mask(actor))) { Log("传送出口被占用、不可进入或未配对，箱子留在入口。"); break; }
                            if (command.action.directionalPortal)
                            {
                                Direction outgoing = (Direction)GetRotation(level, State, target);
                                int landing = level.Neighbor(target, outgoing);
                                // Preflight the whole exit before moving anything: never strand a box
                                // on the exit, cross a closed shared edge, or push its next occupant.
                                if (!CanEnter(level, book, State, target, landing, Mask(actor)))
                                { Log("出口前方被占用、墙门阻挡、不可进入或越界，箱子留在入口。"); break; }
                                Log("定向传送：" + Label(level, Position(actor)) + " → " + Label(level, target) + " → " + Label(level, landing));
                                Relocate(actor, target, outgoing);
                                if (failed) return;
                                Relocate(actor, landing, outgoing);
                                if (!failed) PrependEntry(queue, actor, outgoing);
                                break;
                            }
                            Log("传送：" + Label(level, Position(actor)) + " → " + Label(level, target));
                            Relocate(actor, target, command.incoming);
                            if (!failed) PrependEntry(queue, actor, command.incoming);
                            break;
                    }
                }
            }

            int PortalTarget(Command command)
            {
                var source = level.GetCell(command.source);
                if (!string.IsNullOrEmpty(command.action.targetBinding))
                {
                    var targets = ResolveBinding(level, command.source, source.bindings, command.action.targetBinding);
                    return targets.Count == 1 ? targets[0] : -1;
                }
                if (string.IsNullOrEmpty(source.portalPair)) return -1;
                for (int i = 0; i < level.cells.Count; i++)
                {
                    if (i == command.source || level.cells[i].portalPair != source.portalPair) continue;
                    var rule = book.Find(State.tiles[i]);
                    if (rule != null && rule.visual == TerrainVisual.PortalExit) return i;
                }
                return -1;
            }

            void ChangeTile(Command command)
            {
                var replacement = book.Find(command.action.targetRuleId);
                if (replacement == null || replacement.boundary) { Fail("改变地块操作引用了不存在或不适用的地块定义。"); return; }
                if (replacement.visual == TerrainVisual.Goal || replacement.visual == TerrainVisual.PortalEntrance || replacement.visual == TerrainVisual.PortalExit)
                { Fail("运行中不能创建目标或传送端点，请在编辑器中放置。"); return; }
                var bindings = level.GetCell(command.source).bindings;
                var targets = ResolveBinding(level, command.source, bindings, command.action.targetBinding);
                // Check every target before applying any of them. An invalid custom effect rolls back this entire input.
                foreach (int cell in targets)
                {
                    var old = book.Find(State.tiles[cell]);
                    if (old != null && (old.visual == TerrainVisual.Goal || old.visual == TerrainVisual.PortalEntrance || old.visual == TerrainVisual.PortalExit))
                    { Fail("不能通过运行规则覆盖目标或传送端点：" + Label(level, cell)); return; }
                    var occupant = State.Occupant(cell);
                    if (occupant != ActorMask.None && (replacement.visual == TerrainVisual.Hole || (replacement.allowedActors & occupant) == 0))
                    { Fail("不能将仍被对象占据的格子改成空洞或禁止该对象的地面：" + Label(level, cell)); return; }
                }
                foreach (int cell in targets)
                {
                    State.tiles[cell] = replacement.id;
                    Log(Label(level, cell) + " 改为「" + replacement.name + "」。");
                }
            }
        }
    }
}
