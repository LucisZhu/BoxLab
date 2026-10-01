using System.Collections.Generic;

namespace BoxLab
{
    public static class DemoLevels
    {
        public static List<LevelData> Create()
        {
            var levels = new List<LevelData>();
            var basic = LevelData.Create(5, 5, "01 · 第一次推动");
            basic.description = "方向键 / WASD 移动；把箱子推上金色目标。Z 撤销，R 重开。";
            basic.player = At(basic, 2, 1); Box(basic, 2, 2); Tile(basic, 2, 3, Presets.Goal);
            Wall(basic, 1, 2, Direction.East); Wall(basic, 2, 2, Direction.East);
            levels.Add(basic);

            var ice = LevelData.Create(7, 5, "02 · 惯性路线");
            ice.description = "推入蓝色冰面后，箱子沿进入方向额外滑三格。人物不会滑行。";
            ice.player = At(ice, 1, 2); Box(ice, 2, 2); Tile(ice, 3, 2, Presets.Ice); Tile(ice, 6, 2, Presets.Goal);
            for (int x = 2; x <= 6; x++) { Wall(ice, x, 2, Direction.North); Wall(ice, x, 2, Direction.South); }
            levels.Add(ice);

            var gate = LevelData.Create(7, 5, "03 · 临时岗位");
            gate.description = "先让一个箱子压住压力板，维持通路；送过另一个箱子后，回收压板上的箱子。";
            gate.player = At(gate, 1, 1); Box(gate, 2, 1); Box(gate, 4, 2);
            Tile(gate, 3, 1, Presets.Plate); Tile(gate, 6, 2, Presets.Goal); Tile(gate, 3, 3, Presets.Goal);
            for (int y = 0; y < gate.height; y++) Wall(gate, 4, y, Direction.East);
            Door(gate, 4, 2, Direction.East, Presets.Gate, At(gate, 3, 1));
            levels.Add(gate);

            var alternate = gate.Clone(); alternate.id = System.Guid.NewGuid().ToString("N");
            alternate.name = "04 · 一开一合";
            alternate.description = "两扇门读取同一块压力板，一扇正向、一扇反向。状态由当前占用实时决定。";
            Door(alternate, 4, 3, Direction.East, Presets.InverseGate, At(alternate, 3, 1));
            levels.Add(alternate);

            var turn = LevelData.Create(7, 5, "05 · 设置运输方向");
            turn.description = "按钮受压：转向格将箱子向上送一格；按钮释放：向右送一格。设置路线后再送箱子。";
            turn.player = At(turn, 1, 1); Box(turn, 1, 2); Box(turn, 3, 1);
            Tile(turn, 1, 3, Presets.Plate); Tile(turn, 1, 4, Presets.Goal); Tile(turn, 4, 2, Presets.Goal); Tile(turn, 4, 1, Presets.Turn);
            turn.cells[At(turn, 4, 1)].bindings.Add(Binding(turn, "switches", At(turn, 1, 3)));
            levels.Add(turn);

            var selective = LevelData.Create(5, 5, "06 · 谁能经过");
            selective.description = "绿色地块只允许人物进入。箱子需要走其他路线；进入条件由规则表单配置。";
            selective.player = At(selective, 1, 2); Box(selective, 2, 2); Tile(selective, 3, 2, Presets.Selective); Tile(selective, 4, 2, Presets.Goal);
            levels.Add(selective);

            var portal = LevelData.Create(7, 5, "07 · 两端相连");
            portal.description = "箱子进入紫色入口会到达同组出口；人物可以走过两端，但不会传送。出口被占用时传送受阻。";
            portal.player = At(portal, 1, 2); Box(portal, 2, 2);
            Tile(portal, 3, 2, Presets.PortalEntrance); Tile(portal, 5, 2, Presets.PortalExit); Tile(portal, 6, 2, Presets.Goal);
            portal.cells[At(portal, 3, 2)].portalPair = "A"; portal.cells[At(portal, 5, 2)].portalPair = "A";
            Wall(portal, 3, 2, Direction.East);
            levels.Add(portal);

            var hole = LevelData.Create(5, 5, "08 · 空洞绕行");
            hole.description = "人物不能进入空洞。箱子进入后消失；剩余箱子少于目标时失败，可按 Z 撤销。先调整路线再归位。";
            hole.player = At(hole, 1, 2); Box(hole, 2, 2); Tile(hole, 3, 2, Presets.Hole); Tile(hole, 4, 2, Presets.Goal);
            levels.Add(hole);

            var fragile = LevelData.Create(5, 5, "09 · 回不去的路");
            fragile.description = "箱子离开橙色道路，道路立即坍塌：这次推动人物不会跟进。绕路调整箱子的下一次推动方向。";
            fragile.player = At(fragile, 1, 2); Box(fragile, 2, 2); Tile(fragile, 2, 2, Presets.Fragile); Tile(fragile, 4, 2, Presets.Goal);
            levels.Add(fragile);
            return levels;
        }
        public static int At(LevelData level, int x, int y) { return level.Index(x, y); }
        public static void Tile(LevelData level, int x, int y, string rule) { level.cells[level.Index(x, y)].ruleId = rule; }
        public static void Box(LevelData level, int x, int y) { level.boxes.Add(level.Index(x, y)); }
        public static BindingData Binding(LevelData level, string key, params int[] indices)
        {
            var binding = new BindingData { key = key };
            foreach (int i in indices) binding.cellIds.Add(level.cells[i].id);
            return binding;
        }
        public static void Wall(LevelData level, int x, int y, Direction direction)
        {
            int cell = level.Index(x, y); level.NormalizeEdge(ref cell, ref direction);
            var old = level.FindEdge(cell, direction); if (old != null) level.edges.Remove(old);
            level.edges.Add(new EdgeData { cell = cell, direction = direction, kind = EdgeKind.Wall });
        }
        public static void Door(LevelData level, int x, int y, Direction direction, string ruleId, params int[] switches)
        {
            int cell = level.Index(x, y); level.NormalizeEdge(ref cell, ref direction);
            var old = level.FindEdge(cell, direction); if (old != null) level.edges.Remove(old);
            var edge = new EdgeData { cell = cell, direction = direction, kind = EdgeKind.Gate, ruleId = ruleId };
            edge.bindings.Add(Binding(level, "switches", switches)); level.edges.Add(edge);
        }
    }
}
