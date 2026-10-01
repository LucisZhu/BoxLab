using System;
using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>Managed regressions for the player-only, directional two-stage portal move.</summary>
    public static class DirectionalPortalVerification
    {
        sealed class Fixture
        {
            public LevelData level;
            public int entrance, exit, landing, boxStart, playerStart;
            public Direction incoming;
        }

        public static List<string> RunAll()
        {
            var passed = new List<string>(); var failed = new List<string>();
            Run("定向传送四向入口和四向出口", Directions, passed, failed);
            Run("定向传送拒绝背面与侧面，人物不触发", FrontAndPlayer, passed, failed);
            Run("定向传送预检出口与落点，不移动阻挡者", OccupantsAndTerrain, passed, failed);
            Run("定向传送遵守公共边墙和即时门信号", BoundariesAndGates, passed, failed);
            Run("定向传送落点触发机关并使用出口方向", LandingEffects, passed, failed);
            Run("定向传送截断旧效果且循环整体回退", ReplacementAndLoop, passed, failed);
            Run("定向传送旧规则兼容、快照与撤销一致", CompatibilityAndSnapshots, passed, failed);
            Run("定向传送边界警告与教学求解回放", ValidationAndSolver, passed, failed);
            if (failed.Count > 0) throw new InvalidOperationException(string.Join("\n", failed.ToArray()));
            return passed;
        }

        static void Run(string name, Action test, List<string> passed, List<string> failed)
        { try { test(); passed.Add("通过：" + name); } catch (Exception error) { failed.Add(name + "：" + error.Message); } }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static Direction Opposite(Direction value) { return (Direction)(((int)value + 2) % 4); }

        static Fixture Make(Direction facing = Direction.West, Direction outgoing = Direction.East)
        {
            var level = LevelData.Create(9, 9, "定向传送验证");
            var f = new Fixture { level = level, entrance = level.Index(4, 4), exit = level.Index(6, 6), incoming = Opposite(facing) };
            f.boxStart = level.Neighbor(f.entrance, facing);
            f.playerStart = level.Neighbor(f.boxStart, facing);
            level.player = f.playerStart; level.boxes.Add(f.boxStart);
            level.cells[level.Index(8, 8)].ruleId = PlayerRules.Goal;
            Pair(level, f.entrance, facing, f.exit, outgoing, "A");
            f.landing = level.Neighbor(f.exit, outgoing);
            return f;
        }

        static void Pair(LevelData level, int entrance, Direction facing, int exit, Direction outgoing, string id)
        {
            level.cells[entrance].ruleId = PlayerRules.PortalEntrance;
            level.cells[entrance].portalPair = id; level.cells[entrance].rotation = (int)facing;
            level.cells[exit].ruleId = PlayerRules.PortalExit;
            level.cells[exit].portalPair = id; level.cells[exit].rotation = (int)outgoing;
        }

        static StepResult Push(Fixture f, RuleBookData book = null, bool frames = true)
        {
            book = book ?? PlayerRules.CreateBook();
            return RuleEngine.Step(f.level, book, RuleEngine.CreateState(f.level, book), f.incoming, frames);
        }

        static void Stays(Fixture f, RuleBookData book = null)
        {
            var result = Push(f, book);
            Check(result.changed && !result.interrupted && result.state.boxes[0] == f.entrance, "传送受阻仍应完成普通推动，箱子留入口。");
            Check(result.state.player == f.boxStart && result.state.moves == 1 && result.state.pushes == 1, "受阻传送不应撤回人物跟进或额外计步。");
            foreach (var frame in result.frames) Check(frame.boxes[0] == f.entrance, "预检失败时不能出现箱子先到出口再退回的帧。");
        }

        static void Directions()
        {
            for (int entry = 0; entry < 4; entry++) for (int output = 0; output < 4; output++)
            {
                var f = Make((Direction)entry, (Direction)output); var result = Push(f);
                Check(result.changed && !result.interrupted && result.state.boxes[0] == f.landing, "四向组合落点错误：" + entry + "/" + output);
                Check(result.state.player == f.boxStart && result.state.moves == 1 && result.state.pushes == 1, "整段传送只能计一次普通推动。");
                Check(Push(f, null, false).state.GetKey() == result.state.GetKey(), "动画帧开关不能改变传送逻辑。");
            }
        }

        static void FrontAndPlayer()
        {
            for (int facing = 0; facing < 4; facing++) for (int arrival = 0; arrival < 4; arrival++)
            {
                if (arrival == (int)Opposite((Direction)facing)) continue;
                var f = Make((Direction)facing);
                f.incoming = (Direction)arrival;
                f.boxStart = f.level.Neighbor(f.entrance, Opposite(f.incoming));
                f.playerStart = f.level.Neighbor(f.boxStart, Opposite(f.incoming));
                f.level.player = f.playerStart; f.level.boxes[0] = f.boxStart;
                Stays(f);
            }
            var walk = Make(); var book = PlayerRules.CreateBook();
            walk.level.boxes[0] = 0; walk.level.player = walk.boxStart;
            var state = RuleEngine.Step(walk.level, book, RuleEngine.CreateState(walk.level, book), Direction.East).state;
            Check(state.player == walk.entrance, "人物可以走到入口，但不得传送。");
            walk.level.player = walk.level.Neighbor(walk.exit, Direction.West);
            state = RuleEngine.Step(walk.level, book, RuleEngine.CreateState(walk.level, book), Direction.East).state;
            Check(state.player == walk.exit, "人物可以站在出口。");
        }

        static void OccupantsAndTerrain()
        {
            var occupiedExit = Make(); occupiedExit.level.boxes.Add(occupiedExit.exit); Stays(occupiedExit);
            var occupiedLanding = Make(); occupiedLanding.level.boxes.Add(occupiedLanding.landing); Stays(occupiedLanding);
            Check(Push(occupiedLanding).state.boxes[1] == occupiedLanding.landing, "不得从传送出口连推另一箱。");
            var playerExit = Make();
            playerExit.level.cells[playerExit.exit].ruleId = PlayerRules.Plain; playerExit.level.cells[playerExit.exit].portalPair = "";
            playerExit.exit = playerExit.boxStart;
            Pair(playerExit.level, playerExit.entrance, Direction.West, playerExit.exit, Direction.North, "A");
            Stays(playerExit); // A normal push makes the player occupy the destination portal.
            var playerLanding = Make();
            playerLanding.level.cells[playerLanding.exit].ruleId = PlayerRules.Plain; playerLanding.level.cells[playerLanding.exit].portalPair = "";
            playerLanding.exit = playerLanding.playerStart;
            Pair(playerLanding.level, playerLanding.entrance, Direction.West, playerLanding.exit, Direction.East, "A");
            Stays(playerLanding); // The exit clears, but its next cell is occupied by the following player.
            var selective = Make(); selective.level.cells[selective.landing].ruleId = PlayerRules.Selective; Stays(selective);
            var forbiddenExit = Make(); var book = PlayerRules.CreateBook(); book.Find(PlayerRules.PortalExit).allowedActors = ActorMask.Player;
            Stays(forbiddenExit, book);
            var missing = Make(); missing.level.cells[missing.exit].portalPair = "other"; Stays(missing);
            var outside = Make();
            outside.level.cells[outside.exit].ruleId = PlayerRules.Plain; outside.level.cells[outside.exit].portalPair = "";
            outside.exit = outside.level.Index(8, 6);
            Pair(outside.level, outside.entrance, Direction.West, outside.exit, Direction.East, "A"); Stays(outside);
        }

        static void BoundariesAndGates()
        {
            var front = Make(); front.level.edges.Add(new EdgeData { cell = front.exit, direction = Direction.East, kind = EdgeKind.Wall }); Stays(front);
            var back = Make(); back.level.edges.Add(new EdgeData { cell = back.landing, direction = Direction.West, kind = EdgeKind.Wall }); Stays(back);
            var input = Make(); input.level.edges.Add(new EdgeData { cell = input.boxStart, direction = input.incoming, kind = EdgeKind.Wall });
            var result = Push(input); Check(!result.changed && result.state.boxes[0] == input.boxStart, "入口正面有墙时连普通推动也不能执行。");
            var closed = Make(); closed.level.cells[0].ruleId = PlayerRules.Plate;
            var edge = new EdgeData { cell = closed.exit, direction = Direction.East, kind = EdgeKind.Gate, ruleId = PlayerRules.Gate };
            edge.bindings.Add(new BindingData { key = PlayerRules.Switches, cellIds = new List<string> { closed.level.cells[0].id } });
            closed.level.edges.Add(edge); Stays(closed);
            // Source box leaves a plate but the player replaces it atomically: exit gate stays open.
            closed.level.cells[closed.boxStart].ruleId = PlayerRules.Plate;
            edge.bindings[0].cellIds[0] = closed.level.cells[closed.boxStart].id;
            result = Push(closed);
            Check(result.state.boxes[0] == closed.landing, "出口门必须读取完成普通推动交接后的实时信号。");
        }

        static void LandingEffects()
        {
            var ice = Make(); ice.level.cells[ice.landing].ruleId = PlayerRules.Ice;
            var result = Push(ice);
            Check(result.state.boxes[0] == ice.level.Neighbor(ice.landing, Direction.East), "出口冰面应沿出口方向继续，不能沿入口来向。");
            var northIce = Make(Direction.West, Direction.North); northIce.level.cells[northIce.landing].ruleId = PlayerRules.Ice;
            Check(Push(northIce).state.boxes[0] == northIce.level.Neighbor(northIce.landing, Direction.North), "旋转出口必须改变后续冰面来向。");
            var arrow = Make(); arrow.level.cells[arrow.landing].ruleId = PlayerRules.Arrow;
            Check(Push(arrow).state.boxes[0] == arrow.level.Neighbor(arrow.landing, Direction.North), "传送落到箭头必须执行新方向。");
            var hole = Make(); hole.level.cells[hole.landing].ruleId = PlayerRules.Hole;
            result = Push(hole); Check(result.state.boxes[0] == -1 && result.state.status == GameStatus.Lost, "箱子可以传出掉进空洞，并按剩余数量失败。");
            hole.level.boxes.Add(0); result = Push(hole);
            Check(result.state.boxes[0] == -1 && result.state.status == GameStatus.Playing, "多余箱传出掉落不应无条件判负。");
            var rotation = Make(); rotation.level.cells[rotation.landing].ruleId = PlayerRules.RotationPlate;
            rotation.level.cells[1].ruleId = PlayerRules.Arrow;
            rotation.level.cells[1].bindings.Add(new BindingData { key = PlayerRules.Switches, cellIds = new List<string> { rotation.level.cells[rotation.landing].id } });
            Check(Push(rotation).state.rotations[1] == 1, "传送落在旋转板必须只触发一次新受压。");
        }

        static void ReplacementAndLoop()
        {
            var replace = Make(); replace.level.player = replace.level.Index(0, 4); replace.level.boxes[0] = replace.level.Index(1, 4);
            replace.level.cells[replace.level.Index(2, 4)].ruleId = PlayerRules.Ice;
            replace.level.cells[replace.level.Index(3, 4)].ruleId = PlayerRules.Ice;
            var book = PlayerRules.CreateBook(); book.Find(PlayerRules.Ice).onEnter[0].actions[0].distance = 3;
            Check(Push(replace, book).state.boxes[0] == replace.landing, "传送后不得继续消费旧冰面的剩余步数。");
            var level = LevelData.Create(7, 6, "传送循环"); level.player = level.Index(0, 2); level.boxes.Add(level.Index(1, 2));
            level.cells[level.Index(6, 5)].ruleId = PlayerRules.Goal;
            level.cells[level.Index(2, 2)].ruleId = PlayerRules.Ice;
            level.cells[level.Index(3, 2)].ruleId = PlayerRules.Arrow; level.cells[level.Index(3, 2)].rotation = (int)Direction.East;
            level.cells[level.Index(3, 1)].ruleId = PlayerRules.Arrow;
            Pair(level, level.Index(4, 2), Direction.West, level.Index(1, 4), Direction.East, "A");
            Pair(level, level.Index(2, 4), Direction.West, level.Index(3, 0), Direction.North, "B");
            book = PlayerRules.CreateBook(); var initial = RuleEngine.CreateState(level, book);
            var loop = RuleEngine.Step(level, book, initial, Direction.East);
            Check(loop.interrupted && !loop.changed && loop.frames.Count == 0 && loop.state.GetKey() == initial.GetKey(), "定向传送循环必须回退整次输入。");
        }

        static void CompatibilityAndSnapshots()
        {
            var f = Make(Direction.North, Direction.South); f.incoming = Direction.East;
            f.level.player = f.level.Index(2, 4); f.level.boxes[0] = f.level.Index(3, 4);
            var legacy = Push(f, Presets.CreateBook());
            Check(legacy.state.boxes[0] == f.exit, "旧作者传送应保持全方向进入并停在出口。");
            var book = PlayerRules.CreateBook();
            Check(book.Clone().Find(PlayerRules.PortalEntrance).onEnter[0].actions[0].directionalPortal, "规则深克隆不能丢定向标记。");
            Check(!Presets.CreateBook().Find(PlayerRules.PortalEntrance).onEnter[0].actions[0].directionalPortal, "旧预设默认应关闭定向模式。");
            f = Make(); var initial = RuleEngine.CreateState(f.level, book); string original = initial.GetKey();
            var snapshot = initial.Clone(); var result = RuleEngine.Step(f.level, book, initial, f.incoming);
            Check(initial.GetKey() == original && snapshot.GetKey() == original && f.level.boxes[0] == f.boxStart, "传送不能修改输入、撤销快照或作者地图。");
            Check(RuleEngine.Step(f.level, book, snapshot, f.incoming).state.GetKey() == result.state.GetKey(), "从撤销快照重放结果必须相同。");
        }

        static void ValidationAndSolver()
        {
            var f = Make(); f.level.cells[f.exit].ruleId = PlayerRules.Plain; f.level.cells[f.exit].portalPair = "";
            f.exit = f.level.Index(8, 6); Pair(f.level, f.entrance, Direction.West, f.exit, Direction.East, "A");
            var issues = PlayerRules.Validate(f.level);
            Check(!LevelValidator.HasErrors(issues) && issues.Exists(x => x.cellIndex == f.exit && x.severity == ValidationSeverity.Warning && x.message.Contains("朝向地图外")), "出口越界应定位警告，不封死草稿或结构校验。");
            var book = PlayerRules.CreateBook(); var lesson = PlayerRules.CreateDemoLevels().Find(level => level.id == "boxlab-player-lesson-07");
            Check(lesson != null, "必须保留传送教程的原有稳定编号。");
            var solver = Solve(lesson, book);
            Check(solver.Status == SolveStatus.Solved && solver.Solution.Count == 1, "传送教学关应一推正确传出到目标。");
            var state = RuleEngine.CreateState(lesson, book);
            foreach (var direction in solver.Solution) state = RuleEngine.Step(lesson, book, state, direction).state;
            Check(state.status == GameStatus.Won, "求解路线必须能用同一引擎真正完成。");
            var narrow = LevelData.Create(7, 2, "唯一传送通路"); narrow.player = 1; narrow.boxes.Add(2); narrow.cells[6].ruleId = PlayerRules.Goal;
            for (int i = 7; i < 14; i++) narrow.cells[i].ruleId = PlayerRules.Hole;
            Pair(narrow, 3, Direction.West, 5, Direction.East, "A");
            narrow.edges.Add(new EdgeData { cell = 3, direction = Direction.East, kind = EdgeKind.Wall });
            Check(Solve(narrow, book).Status == SolveStatus.Solved, "方向正确的唯一传送通路应可解。");
            narrow.cells[3].rotation = (int)Direction.East;
            Check(Solve(narrow, book).Status == SolveStatus.Unsolvable, "求解不能无视入口朝向或穿墙到另一侧。");
        }

        static SokobanSolver Solve(LevelData level, RuleBookData book)
        {
            var solver = new SokobanSolver(level, book, 20000, 10000);
            while (solver.Status == SolveStatus.Searching) solver.Step(1000);
            return solver;
        }
    }
}
