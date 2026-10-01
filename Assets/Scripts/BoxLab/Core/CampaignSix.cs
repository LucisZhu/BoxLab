namespace BoxLab
{
    /// <summary>A two-person-equivalent loading station: the player and a parked crate share gate duty.</summary>
    public static class CampaignSix
    {
        public const string Solution =
            "ENSSSNNEE" +             // Park the attendant, turn north and dispatch the first crate.
            "SESSEENNNNNEESSS" +      // Walk through the person-only corridor and empty the receiving goal.
            "ESW" +                   // Park the first delivery at the rear goal.
            "SWWWWWNNWNEE" +          // Return, load and dispatch the second delivery.
            "NENNWSS" +               // Bring the final crate down from the upper stock area.
            "WSSSNSNNEE";             // Turn the fork south, then complete the third goal.

        public static LevelData Create()
        {
            var level = LevelData.Create(9, 8, "06 · 双岗交接");
            level.id = "boxlab-player-campaign-06";
            level.description = "四只箱子，三个终点。两块压力板缺一不可，箱子和人物各走自己的通道。先安排留守与接货，再为最后一批选择方向。";
            level.player = level.Index(0, 4);

            // The two rooms are joined only by the person's lower passage. The centre column
            // is a sealed cargo fork, so the player cannot bypass its direction choice by hand.
            for (int y = 0; y < level.height; y++)
                for (int x = 0; x < level.width; x++)
                    if (!Floor(x, y)) DemoLevels.Tile(level, x, y, PlayerRules.Hole);
            for (int y = 0; y < level.height; y++)
                for (int x = 0; x < level.width; x++)
                    if (Floor(x, y))
                        for (int d = 0; d < 4; d++)
                        {
                            int dx = d == 1 ? 1 : d == 3 ? -1 : 0;
                            int dy = d == 0 ? 1 : d == 2 ? -1 : 0;
                            if (!Floor(x + dx, y + dy)) DemoLevels.Wall(level, x, y, (Direction)d);
                        }
            for (int y = 0; y < level.height; y++)
                if (y != 1) DemoLevels.Wall(level, 3, y, Direction.East);
            for (int y = 3; y <= 5; y++) DemoLevels.Wall(level, 4, y, Direction.East);
            DemoLevels.Wall(level, 3, 4, Direction.North);
            DemoLevels.Wall(level, 3, 4, Direction.South);

            DemoLevels.Box(level, 1, 5); // The fourth crate becomes the remote gate attendant.
            DemoLevels.Box(level, 2, 4);
            DemoLevels.Box(level, 2, 2);
            DemoLevels.Box(level, 2, 6);
            DemoLevels.Tile(level, 1, 6, PlayerRules.Plate);
            DemoLevels.Tile(level, 2, 4, PlayerRules.Plate);
            level.cells[level.Index(1, 6)].signalStyle = 3;
            level.cells[level.Index(2, 4)].signalStyle = 1;
            DemoLevels.Tile(level, 3, 4, PlayerRules.BoxOnly);
            DemoLevels.Tile(level, 4, 1, PlayerRules.Selective);
            DemoLevels.Door(level, 3, 4, Direction.East, PlayerRules.Gate, level.Index(1, 6), level.Index(2, 4));

            DemoLevels.Tile(level, 1, 2, PlayerRules.RotationPlate);
            level.cells[level.Index(1, 2)].signalStyle = 8;
            DemoLevels.Tile(level, 4, 4, PlayerRules.Arrow);
            level.cells[level.Index(4, 4)].rotation = (int)Direction.West;
            level.cells[level.Index(4, 4)].bindings.Add(DemoLevels.Binding(level, PlayerRules.Switches, level.Index(1, 2)));
            DemoLevels.Tile(level, 4, 5, PlayerRules.PortalEntrance);
            DemoLevels.Tile(level, 7, 6, PlayerRules.PortalExit);
            level.cells[level.Index(4, 5)].rotation = level.cells[level.Index(7, 6)].rotation = (int)Direction.South;
            level.cells[level.Index(4, 5)].portalPair = level.cells[level.Index(7, 6)].portalPair = "final-depot";
            DemoLevels.Tile(level, 4, 3, PlayerRules.Goal);
            DemoLevels.Tile(level, 6, 2, PlayerRules.Goal);
            DemoLevels.Tile(level, 7, 5, PlayerRules.Goal);
            return level;
        }

        private static bool Floor(int x, int y)
        {
            return x >= 0 && x <= 3 && y >= 0 && y <= 7 ||
                x >= 5 && x <= 8 && y >= 1 && y <= 6 ||
                x == 4 && (y == 1 || y >= 3 && y <= 5);
        }
    }
}
