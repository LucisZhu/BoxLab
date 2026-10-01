using System;

namespace BoxLab
{
    /// <summary>One freight bridge, a separate gated walking route, and a shared upper loading lane.</summary>
    public static class CampaignFive
    {
        public const string Solution = "SWNNNENWSWNWNEEWSESSSSSWWNNNEESENWNNWWN";

        public static LevelData Create()
        {
            var level = LevelData.Create(8, 8, "05 · 最后一趟");
            level.id = "boxlab-player-campaign-05";
            level.description = "先安排守门的箱子，再清出上层的装卸位置。裂桥只够一趟，冰面会把货物送到最后的落点；别急着占住近处目标。";
            foreach (var cell in level.cells) cell.ruleId = PlayerRules.Hole;
            for (int y = 0; y <= 2; y++) for (int x = 1; x <= 4; x++) DemoLevels.Tile(level, x, y, PlayerRules.Plain);
            for (int y = 4; y <= 5; y++) for (int x = 1; x <= 4; x++) DemoLevels.Tile(level, x, y, PlayerRules.Plain);
            for (int x = 1; x <= 4; x++) DemoLevels.Tile(level, x, 6, PlayerRules.Plain);

            DemoLevels.Tile(level, 1, 3, PlayerRules.Fragile);
            DemoLevels.Tile(level, 1, 4, PlayerRules.Ice);
            DemoLevels.Tile(level, 2, 3, PlayerRules.Selective);
            DemoLevels.Tile(level, 3, 3, PlayerRules.Selective);
            DemoLevels.Tile(level, 2, 1, PlayerRules.Plate);
            level.cells[level.Index(2, 1)].signalStyle = 6;
            DemoLevels.Tile(level, 1, 6, PlayerRules.Goal);
            DemoLevels.Tile(level, 4, 6, PlayerRules.Goal);

            DemoLevels.Tile(level, 4, 3, PlayerRules.PortalEntrance);
            DemoLevels.Tile(level, 6, 5, PlayerRules.PortalExit);
            level.cells[level.Index(4, 3)].portalPair = "last-freight";
            level.cells[level.Index(6, 5)].portalPair = "last-freight";
            level.cells[level.Index(4, 3)].rotation = level.cells[level.Index(6, 5)].rotation = (int)Direction.South;
            DemoLevels.Tile(level, 6, 4, PlayerRules.Goal);

            level.player = level.Index(4, 2);
            DemoLevels.Box(level, 3, 4); // Must be moved into the common upper loading column.
            DemoLevels.Box(level, 1, 2); // The bridge's only freight crossing.
            DemoLevels.Box(level, 3, 1); // Reserve a helper on the pressure plate.
            DemoLevels.Box(level, 2, 2); // Cannot be pushed east until the bridge cargo vacates its loading stance.

            // The unused cells form visible voids. Static walls keep their perimeter unambiguous.
            for (int y = 0; y < level.height; y++) for (int x = 0; x < level.width; x++)
            {
                int at = level.Index(x, y); if (level.cells[at].ruleId == PlayerRules.Hole) continue;
                for (int d = 0; d < 4; d++)
                {
                    int neighbor = level.Neighbor(at, (Direction)d);
                    if (neighbor < 0 || level.cells[neighbor].ruleId == PlayerRules.Hole) DemoLevels.Wall(level, x, y, (Direction)d);
                }
            }
            DemoLevels.Wall(level, 2, 2, Direction.North);
            DemoLevels.Wall(level, 3, 3, Direction.East);
            DemoLevels.Wall(level, 4, 3, Direction.North);
            // The far upper goal must be loaded horizontally from column C. Sending the bridge
            // cargo too soon blocks access to the pusher's stance at the near corner goal.
            DemoLevels.Wall(level, 3, 5, Direction.North);
            DemoLevels.Wall(level, 4, 5, Direction.North);
            DemoLevels.Door(level, 3, 2, Direction.North, PlayerRules.Gate, level.Index(2, 1));
            return level;
        }
    }
}
