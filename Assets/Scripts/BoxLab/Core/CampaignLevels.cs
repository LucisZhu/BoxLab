using System;
using System.Collections.Generic;

namespace BoxLab
{
    /// <summary>Authored campaign puzzles, separate from the stable introductory collection.</summary>
    public static class CampaignLevels
    {
        /// <summary>Recorded solutions checked against the actual rules by RunAll; not all claim shortest paths.</summary>
        public static List<Direction> GetVerifiedSolution(string levelId)
        {
            string encoded;
            switch (levelId)
            {
                case "boxlab-player-campaign-01": encoded = "NESENSSENNWNEEEWWWN"; break;
                case "boxlab-player-campaign-02": encoded = "EEEEWWWNNNNEESWNWSSWSSEEEENSWWWNNN"; break;
                case "boxlab-player-campaign-03": encoded = "NNNNEEESWNENNWSWSWSSENSWSSENNN"; break;
                case "boxlab-player-campaign-04": encoded = CampaignFour.Solution; break;
                case "boxlab-player-campaign-05": encoded = CampaignFive.Solution; break;
                case "boxlab-player-campaign-06": encoded = CampaignSix.Solution; break;
                default: throw new ArgumentException("未找到这张正式关卡的验证解法。", nameof(levelId));
            }
            var result = new List<Direction>();
            foreach (char command in encoded)
            {
                int direction = "NESW".IndexOf(command);
                if (direction < 0) throw new InvalidOperationException("正式关卡的验证解法包含无效方向。");
                result.Add((Direction)direction);
            }
            return result;
        }

        public static List<LevelData> Create()
        {
            var first = LevelData.Create(7, 6, "01 · 最后一个岗位");
            first.id = "boxlab-player-campaign-01";
            first.description = "两个目标都要完成。近处的目标很诱人，但先想清楚：谁还需要守住通路，什么时候才能离开岗位？";
            first.player = first.Index(0, 1);
            DemoLevels.Box(first, 2, 1); DemoLevels.Box(first, 1, 2);
            DemoLevels.Tile(first, 2, 4, PlayerRules.Plate); first.cells[first.Index(2, 4)].signalStyle = 3;
            DemoLevels.Tile(first, 2, 5, PlayerRules.Goal); DemoLevels.Tile(first, 6, 3, PlayerRules.Goal);
            for (int y = 0; y < first.height; y++) DemoLevels.Wall(first, 3, y, Direction.East);
            DemoLevels.Door(first, 3, 3, Direction.East, PlayerRules.Gate, first.Index(2, 4));

            var second = Router(6, "02 · 两次发车", "boxlab-player-campaign-02");
            second.description = "同一条运输线要送达左右两个终点。装货之前先设置方向；送出的箱子无法回来，下一批也不能照搬上一批的路线。";
            second.player = second.Index(0, 0);
            DemoLevels.Box(second, 1, 1); DemoLevels.Box(second, 2, 3);
            DemoLevels.Tile(second, 4, 0, PlayerRules.RotationPlate); second.cells[second.Index(4, 0)].signalStyle = 8;
            second.cells[second.Index(4, 2)].bindings.Add(DemoLevels.Binding(second, PlayerRules.Switches, second.Index(4, 0)));

            var third = Router(7, "03 · 岗位与时序", "boxlab-player-campaign-03");
            third.description = "三只箱子、两个终点。有人要留守开门，装货路线本身也会改变运输方向。先安排岗位，再考虑每一批出发时箭头会朝哪里。";
            third.player = third.Index(0, 0);
            DemoLevels.Box(third, 1, 1); DemoLevels.Box(third, 2, 3); DemoLevels.Box(third, 2, 5);
            DemoLevels.Tile(third, 1, 2, PlayerRules.RotationPlate); third.cells[third.Index(1, 2)].signalStyle = 8;
            third.cells[third.Index(4, 2)].bindings.Add(DemoLevels.Binding(third, PlayerRules.Switches, third.Index(1, 2)));
            DemoLevels.Tile(third, 2, 4, PlayerRules.Plate); third.cells[third.Index(2, 4)].signalStyle = 3;
            DemoLevels.Door(third, 4, 4, Direction.South, PlayerRules.Gate, third.Index(2, 4));
            return new List<LevelData> { first, second, third, CampaignFour.Create(), CampaignFive.Create(), CampaignSix.Create() };
        }

        static LevelData Router(int height, string name, string id)
        {
            var level = LevelData.Create(7, height, name); level.id = id;
            DemoLevels.Tile(level, 2, 2, PlayerRules.Goal); DemoLevels.Tile(level, 6, 2, PlayerRules.Goal);
            DemoLevels.Tile(level, 1, 4, PlayerRules.PortalEntrance); DemoLevels.Tile(level, 4, 4, PlayerRules.PortalExit);
            level.cells[level.Index(1, 4)].rotation = level.cells[level.Index(4, 4)].rotation = (int)Direction.South;
            level.cells[level.Index(1, 4)].portalPair = level.cells[level.Index(4, 4)].portalPair = "delivery-line";
            DemoLevels.Tile(level, 4, 3, PlayerRules.Ice); DemoLevels.Tile(level, 3, 2, PlayerRules.Ice);
            DemoLevels.Tile(level, 5, 2, PlayerRules.Ice); DemoLevels.Tile(level, 4, 2, PlayerRules.Arrow);
            for (int x = 2; x <= 6; x++)
            {
                DemoLevels.Wall(level, x, 2, Direction.South);
                if (x != 4) DemoLevels.Wall(level, x, 2, Direction.North);
            }
            DemoLevels.Wall(level, 2, 2, Direction.West);
            DemoLevels.Wall(level, 4, 4, Direction.North);
            for (int y = 3; y <= 4; y++)
            {
                DemoLevels.Wall(level, 4, y, Direction.East);
                DemoLevels.Wall(level, 4, y, Direction.West);
            }
            return level;
        }
    }
}
