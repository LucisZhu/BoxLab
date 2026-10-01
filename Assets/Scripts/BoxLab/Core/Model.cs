using System;
using System.Collections.Generic;
using System.Text;

namespace BoxLab
{
    public enum Direction { North, East, South, West }
    [Flags] public enum ActorMask { None = 0, Player = 1, Box = 2, Both = 3 }
    public enum EdgeKind { Open, Wall, Gate }
    public enum TerrainVisual { Plain, Goal, Ice, Plate, Turn, Selective, PortalEntrance, PortalExit, Hole, Fragile, Gate, RotationPlate }
    public enum ConditionKind { Always, All, Any, AtLeast, ActorIs, Occupied, SignalCount, AllSignals }
    public enum ActionKind { Move, Teleport, ChangeTile, RemoveBox }
    public enum DirectionMode { Incoming, Fixed }
    public enum GameStatus { Playing, Won, Lost }

    [Serializable]
    public struct GridPos
    {
        public int x, y;
        public GridPos(int x, int y) { this.x = x; this.y = y; }
        public override string ToString() { return "(" + x + ", " + y + ")"; }
    }

    [Serializable]
    public class BindingData
    {
        public string key = "targets";
        public List<string> cellIds = new List<string>();
        public BindingData Clone() { return new BindingData { key = key, cellIds = new List<string>(cellIds ?? new List<string>()) }; }
    }

    [Serializable]
    public class ConditionNode
    {
        public int parent = -1;
        public ConditionKind kind = ConditionKind.Always;
        public bool negate;
        public ActorMask actorMask = ActorMask.Both;
        public string binding = "";
        public int threshold = 1;
        public ConditionNode Clone() { return (ConditionNode)MemberwiseClone(); }
    }

    [Serializable]
    public class ConditionSet
    {
        public List<ConditionNode> nodes = new List<ConditionNode> { new ConditionNode() };
        public ConditionSet Clone()
        {
            var result = new ConditionSet(); result.nodes.Clear();
            if (nodes != null) foreach (var node in nodes) result.nodes.Add(node == null ? null : node.Clone());
            return result;
        }
        public static ConditionSet Always() { return new ConditionSet(); }
        public static ConditionSet Actor(ActorMask mask) { return new ConditionSet { nodes = new List<ConditionNode> { new ConditionNode { kind = ConditionKind.ActorIs, actorMask = mask } } }; }
        public static ConditionSet Signals(string binding, int count = 1, bool negate = false)
        {
            return new ConditionSet { nodes = new List<ConditionNode> { new ConditionNode { kind = ConditionKind.SignalCount, binding = binding, threshold = count, negate = negate } } };
        }
    }

    [Serializable]
    public class RuleAction
    {
        public ActionKind kind = ActionKind.Move;
        public DirectionMode directionMode = DirectionMode.Incoming;
        public Direction direction = Direction.North;
        public int distance = 3;
        public string targetRuleId = "";
        public string targetBinding = "";
        // Player portals require entry through their front and a clear cell beyond the exit.
        // False preserves existing author-defined/V1 teleport actions.
        public bool directionalPortal;
        public RuleAction Clone() { return (RuleAction)MemberwiseClone(); }
    }

    [Serializable]
    public class RuleBranch
    {
        public ConditionSet condition = new ConditionSet();
        public List<RuleAction> actions = new List<RuleAction>();
        public RuleBranch Clone()
        {
            var result = new RuleBranch { condition = condition == null ? null : condition.Clone() };
            if (actions != null) foreach (var action in actions) result.actions.Add(action == null ? null : action.Clone());
            return result;
        }
    }

    [Serializable]
    public class RuleDefinition
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "新地块规则";
        public TerrainVisual visual = TerrainVisual.Plain;
        public string colorHex = "#64748B";
        public bool builtIn, boundary;
        public ActorMask allowedActors = ActorMask.Both;
        public ActorMask signalActors = ActorMask.None;
        public ConditionSet canEnter = new ConditionSet();
        public List<RuleBranch> onEnter = new List<RuleBranch>();
        public List<RuleBranch> onLeave = new List<RuleBranch>();
        public RuleDefinition Clone()
        {
            var result = (RuleDefinition)MemberwiseClone();
            result.canEnter = canEnter == null ? null : canEnter.Clone();
            result.onEnter = new List<RuleBranch>(); result.onLeave = new List<RuleBranch>();
            if (onEnter != null) foreach (var branch in onEnter) result.onEnter.Add(branch == null ? null : branch.Clone());
            if (onLeave != null) foreach (var branch in onLeave) result.onLeave.Add(branch == null ? null : branch.Clone());
            return result;
        }
    }

    [Serializable]
    public class RuleBookData
    {
        public List<RuleDefinition> rules = new List<RuleDefinition>();
        public RuleDefinition Find(string id)
        {
            if (rules != null) foreach (var rule in rules) if (rule != null && rule.id == id) return rule;
            return null;
        }
        public RuleBookData Clone()
        {
            var result = new RuleBookData();
            if (rules != null) foreach (var rule in rules) result.rules.Add(rule == null ? null : rule.Clone());
            return result;
        }
    }

    [Serializable]
    public class CellData
    {
        public string id = Guid.NewGuid().ToString("N");
        public string ruleId = "plain";
        public int rotation;
        // Stable player-editor palette identity (colour + symbol), independent of runtime state.
        public int signalStyle = -1;
        // Authoring-only deletion order: 0 prefers an occupant (legacy default), 1 the floor.
        public int editTopLayer;
        public string portalPair = "";
        public List<BindingData> bindings = new List<BindingData>();
        public CellData Clone()
        {
            var result = (CellData)MemberwiseClone(); result.bindings = new List<BindingData>();
            if (bindings != null) foreach (var binding in bindings) result.bindings.Add(binding == null ? null : binding.Clone());
            return result;
        }
    }

    [Serializable]
    public class EdgeData
    {
        public int cell;
        public Direction direction = Direction.East;
        public EdgeKind kind;
        public string ruleId = "gate";
        public List<BindingData> bindings = new List<BindingData>();
        public EdgeData Clone()
        {
            var result = (EdgeData)MemberwiseClone(); result.bindings = new List<BindingData>();
            if (bindings != null) foreach (var binding in bindings) result.bindings.Add(binding == null ? null : binding.Clone());
            return result;
        }
    }

    [Serializable]
    public class LevelData
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "新关卡";
        public string description = "";
        public int width = 5, height = 5;
        public List<CellData> cells = new List<CellData>();
        public List<EdgeData> edges = new List<EdgeData>();
        public int player;
        public List<int> boxes = new List<int>();
        public int Index(int x, int y) { return Contains(x, y) ? y * width + x : -1; }
        public int Index(GridPos position) { return Index(position.x, position.y); }
        public GridPos Position(int index) { return new GridPos(index % width, index / width); }
        public bool Contains(int x, int y) { return x >= 0 && y >= 0 && x < width && y < height; }
        public bool Contains(GridPos position) { return Contains(position.x, position.y); }
        public bool Contains(int index) { return index >= 0 && index < width * height; }
        public CellData GetCell(int index) { return cells != null && index >= 0 && index < cells.Count ? cells[index] : null; }
        public int FindCellIndex(string cellId)
        {
            if (string.IsNullOrEmpty(cellId) || cells == null) return -1;
            for (int i = 0; i < cells.Count; i++) if (cells[i] != null && cells[i].id == cellId) return i;
            return -1;
        }
        public int Neighbor(int index, Direction direction)
        {
            if (!Contains(index)) return -1;
            int x = index % width, y = index / width;
            switch (direction) { case Direction.North: y++; break; case Direction.East: x++; break; case Direction.South: y--; break; case Direction.West: x--; break; }
            return Index(x, y);
        }
        public void NormalizeEdge(ref int cell, ref Direction direction)
        {
            if (direction == Direction.West || direction == Direction.South)
            {
                int other = Neighbor(cell, direction);
                if (other >= 0) { cell = other; direction = direction == Direction.West ? Direction.East : Direction.North; }
            }
        }
        public EdgeData FindEdge(int cell, Direction direction)
        {
            NormalizeEdge(ref cell, ref direction);
            if (edges != null) foreach (var edge in edges)
            {
                if (edge == null) continue;
                int c = edge.cell; Direction d = edge.direction; NormalizeEdge(ref c, ref d);
                if (c == cell && d == direction) return edge;
            }
            return null;
        }
        public LevelData Clone()
        {
            var result = (LevelData)MemberwiseClone();
            result.cells = new List<CellData>(); result.edges = new List<EdgeData>(); result.boxes = new List<int>(boxes ?? new List<int>());
            if (cells != null) foreach (var cell in cells) result.cells.Add(cell == null ? null : cell.Clone());
            if (edges != null) foreach (var edge in edges) result.edges.Add(edge == null ? null : edge.Clone());
            return result;
        }
        public static LevelData Create(int width = 5, int height = 5, string name = "新关卡")
        {
            var result = new LevelData { width = Math.Max(1, width), height = Math.Max(1, height), name = name };
            for (int i = 0; i < result.width * result.height; i++) result.cells.Add(new CellData());
            return result;
        }
    }

    [Serializable]
    public class BoardState
    {
        public int player;
        public List<int> boxes = new List<int>();
        public string[] tiles = new string[0];
        public int[] rotations = new int[0];
        public int moves, pushes;
        public GameStatus status;
        public BoardState Clone()
        {
            return new BoardState { player = player, boxes = new List<int>(boxes), tiles = (string[])tiles.Clone(), rotations = rotations == null ? new int[0] : (int[])rotations.Clone(), moves = moves, pushes = pushes, status = status };
        }
        public int BoxAt(int cell) { return cell < 0 ? -1 : boxes.IndexOf(cell); }
        public ActorMask Occupant(int cell)
        {
            if (cell < 0) return ActorMask.None;
            if (player == cell) return ActorMask.Player;
            return BoxAt(cell) >= 0 ? ActorMask.Box : ActorMask.None;
        }
        public string GetKey()
        {
            var builder = new StringBuilder(); builder.Append(player).Append('|');
            var sorted = new List<int>(boxes); sorted.Sort();
            foreach (int box in sorted) builder.Append(box).Append(',');
            builder.Append('|');
            foreach (string tile in tiles) builder.Append(tile == null ? 0 : tile.Length).Append(':').Append(tile).Append(';');
            builder.Append('|');
            if (rotations != null) foreach (int rotation in rotations) builder.Append(rotation).Append(',');
            return builder.ToString();
        }
    }

    public class StepResult
    {
        public BoardState state;
        public bool changed, interrupted;
        public string message = "";
        public List<string> trace = new List<string>();
        public List<BoardState> frames = new List<BoardState>();
    }
}
