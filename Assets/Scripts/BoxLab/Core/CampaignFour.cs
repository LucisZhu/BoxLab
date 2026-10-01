namespace BoxLab
{
    /// <summary>Two simultaneous holding jobs, a separate walking route, then a safe release order.</summary>
    public static class CampaignFour
    {
        public const string Solution = "SENNNESENSSSWNWNEEESSSENNENENNWWSEESSSSWWWNNNNNSWWN";

        public static LevelData Create()
        {
            var level = LevelData.Create(8, 7, "04 · 双岗接力");
            level.id = "boxlab-player-campaign-04";
            level.description = "三只箱子，三个目标。货运门需要两个岗位同时留守；箱子过门后，人物还要换路接手。先想清楚谁能归位，谁还不能离开岗位。";
            level.player = level.Index(0, 2);
            DemoLevels.Box(level, 1, 2); DemoLevels.Box(level, 2, 4); DemoLevels.Box(level, 2, 2);
            DemoLevels.Tile(level, 1, 5, PlayerRules.Plate); level.cells[level.Index(1, 5)].signalStyle = 3;
            DemoLevels.Tile(level, 3, 5, PlayerRules.Plate); level.cells[level.Index(3, 5)].signalStyle = 8;
            DemoLevels.Tile(level, 1, 6, PlayerRules.Goal); DemoLevels.Tile(level, 3, 6, PlayerRules.Goal);
            DemoLevels.Tile(level, 7, 4, PlayerRules.Goal);

            for (int y = 1; y < level.height; y++) DemoLevels.Wall(level, 3, y, Direction.East);
            DemoLevels.Door(level, 3, 3, Direction.East, PlayerRules.Gate, level.Index(1, 5), level.Index(3, 5));
            DemoLevels.Tile(level, 4, 3, PlayerRules.BoxOnly);
            DemoLevels.Tile(level, 4, 0, PlayerRules.Selective);
            return level;
        }
    }
}
