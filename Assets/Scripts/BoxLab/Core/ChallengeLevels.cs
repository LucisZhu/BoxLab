using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>Optional authored challenges. Kept separate from the nine introductory lessons.</summary>
    public static class ChallengeLevels
    {
        public static List<LevelData> Create()
        {
            var helper = LevelData.Create(7, 7, "10 · 留守与运送");
            helper.id = "boxlab-player-challenge-01";
            helper.description = "两只箱子，只有一个目标。安排一只箱子维持通路，让另一只完成运送；不用把帮手一起运走。";
            helper.player = helper.Index(0, 1);
            DemoLevels.Box(helper, 2, 1); DemoLevels.Box(helper, 1, 2);
            DemoLevels.Tile(helper, 2, 5, PlayerRules.Plate);
            helper.cells[helper.Index(2, 5)].signalStyle = 3;
            DemoLevels.Tile(helper, 6, 5, PlayerRules.Goal);
            for (int y = 0; y < helper.height; y++) DemoLevels.Wall(helper, 3, y, Direction.East);
            DemoLevels.Door(helper, 3, 4, Direction.East, PlayerRules.Gate, helper.Index(2, 5));

            var route = LevelData.Create(7, 7, "11 · 接力路线");
            route.id = "boxlab-player-challenge-02";
            route.description = "人物从下方通道绕行，箱子走传送与滑行路线。先把箱子送到入口正面，再去另一侧接手。";
            route.player = route.Index(0, 1); DemoLevels.Box(route, 1, 4);
            DemoLevels.Tile(route, 6, 5, PlayerRules.Goal);
            for (int y = 1; y < route.height; y++) DemoLevels.Wall(route, 3, y, Direction.East);
            DemoLevels.Tile(route, 4, 0, PlayerRules.Selective);
            DemoLevels.Tile(route, 3, 2, PlayerRules.PortalEntrance);
            DemoLevels.Tile(route, 5, 4, PlayerRules.PortalExit);
            route.cells[route.Index(3, 2)].rotation = (int)Direction.West;
            route.cells[route.Index(5, 4)].rotation = (int)Direction.South;
            route.cells[route.Index(3, 2)].portalPair = route.cells[route.Index(5, 4)].portalPair = "challenge-route";
            DemoLevels.Tile(route, 5, 3, PlayerRules.Ice);
            DemoLevels.Tile(route, 5, 2, PlayerRules.Arrow);
            route.cells[route.Index(5, 2)].rotation = (int)Direction.East;
            DemoLevels.Wall(route, 5, 4, Direction.North);
            DemoLevels.Wall(route, 5, 4, Direction.East);
            DemoLevels.Wall(route, 5, 4, Direction.West);
            DemoLevels.Wall(route, 5, 3, Direction.East);
            DemoLevels.Wall(route, 5, 3, Direction.West);
            DemoLevels.Wall(route, 5, 2, Direction.West);
            return new List<LevelData> { helper, route };
        }
    }
}
