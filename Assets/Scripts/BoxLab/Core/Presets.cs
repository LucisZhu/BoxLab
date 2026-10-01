using System.Collections.Generic;

namespace BoxLab
{
    public static class Presets
    {
        public const string Plain = "plain", Goal = "goal", Ice = "ice", Plate = "plate", Turn = "turn", Selective = "selective";
        public const string PortalEntrance = "portal-entrance", PortalExit = "portal-exit", Hole = "hole", Fragile = "fragile";
        public const string Gate = "gate", InverseGate = "inverse-gate";

        public static RuleBookData CreateBook()
        {
            var book = new RuleBookData();
            book.rules.Add(Definition(Plain, "常规地面", TerrainVisual.Plain, "#334155"));
            book.rules.Add(Definition(Goal, "目标", TerrainVisual.Goal, "#FBBF24"));
            var ice = Definition(Ice, "冰面 · 额外滑行 3 格", TerrainVisual.Ice, "#38BDF8");
            ice.onEnter.Add(Branch(ConditionSet.Actor(ActorMask.Box), new RuleAction { kind = ActionKind.Move, distance = 3 }));
            book.rules.Add(ice);
            var plate = Definition(Plate, "压力板", TerrainVisual.Plate, "#F59E0B");
            plate.signalActors = ActorMask.Both; book.rules.Add(plate);
            var turn = Definition(Turn, "受控转向 · 上 / 右", TerrainVisual.Turn, "#A78BFA");
            turn.onEnter.Add(Branch(ActorAndSignals(ActorMask.Box, "switches"), new RuleAction { kind = ActionKind.Move, directionMode = DirectionMode.Fixed, direction = Direction.North, distance = 1 }));
            turn.onEnter.Add(Branch(ConditionSet.Actor(ActorMask.Box), new RuleAction { kind = ActionKind.Move, directionMode = DirectionMode.Fixed, direction = Direction.East, distance = 1 }));
            book.rules.Add(turn);
            var selective = Definition(Selective, "人物通道", TerrainVisual.Selective, "#34D399");
            selective.allowedActors = ActorMask.Player; book.rules.Add(selective);
            var entrance = Definition(PortalEntrance, "传送入口", TerrainVisual.PortalEntrance, "#8B5CF6");
            entrance.onEnter.Add(Branch(ConditionSet.Actor(ActorMask.Box), new RuleAction { kind = ActionKind.Teleport }));
            book.rules.Add(entrance);
            book.rules.Add(Definition(PortalExit, "传送出口", TerrainVisual.PortalExit, "#D8B4FE"));
            var hole = Definition(Hole, "空洞", TerrainVisual.Hole, "#0F172A");
            hole.allowedActors = ActorMask.Box;
            hole.onEnter.Add(Branch(ConditionSet.Actor(ActorMask.Box), new RuleAction { kind = ActionKind.RemoveBox }));
            book.rules.Add(hole);
            var fragile = Definition(Fragile, "坍塌道路", TerrainVisual.Fragile, "#FB923C");
            fragile.onLeave.Add(Branch(ConditionSet.Actor(ActorMask.Both), new RuleAction { kind = ActionKind.ChangeTile, targetRuleId = Hole }));
            book.rules.Add(fragile);
            var gate = Definition(Gate, "受控门 · 条件成立开放", TerrainVisual.Gate, "#F59E0B");
            gate.boundary = true; gate.canEnter = ConditionSet.Signals("switches"); book.rules.Add(gate);
            var inverse = Definition(InverseGate, "反向门 · 条件不成立开放", TerrainVisual.Gate, "#FB7185");
            inverse.boundary = true; inverse.canEnter = ConditionSet.Signals("switches", 1, true); book.rules.Add(inverse);
            return book;
        }

        static RuleDefinition Definition(string id, string name, TerrainVisual visual, string color)
        {
            return new RuleDefinition { id = id, name = name, visual = visual, colorHex = color, builtIn = true };
        }
        static RuleBranch Branch(ConditionSet condition, params RuleAction[] actions)
        {
            return new RuleBranch { condition = condition, actions = new List<RuleAction>(actions) };
        }
        static ConditionSet ActorAndSignals(ActorMask mask, string key)
        {
            return new ConditionSet { nodes = new List<ConditionNode>
            {
                new ConditionNode { kind = ConditionKind.All },
                new ConditionNode { parent = 0, kind = ConditionKind.ActorIs, actorMask = mask },
                new ConditionNode { parent = 0, kind = ConditionKind.SignalCount, binding = key, threshold = 1 }
            } };
        }
    }
}
