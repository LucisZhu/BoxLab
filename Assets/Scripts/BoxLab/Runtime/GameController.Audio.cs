using System;

namespace BoxLab
{
    [Flags]
    internal enum FrameAudioCue { None = 0, Mechanism = 1, Rotation = 2, Teleport = 4, Fall = 8, IceSlide = 16 }

    /// <summary>Read-only presentation cues. Explicit controller hooks keep load, render and preview silent.</summary>
    internal static class GameControllerAudio
    {
        internal static void MoveStarted(BoardState before, BoardState after)
        {
            if (before == null || after == null) return;
            WorkshopAudio.Play(after.pushes > before.pushes ? WorkshopSound.Push : WorkshopSound.Step);
        }
        internal static void Completed(BoardState before, BoardState after)
        {
            if (before != null && after != null && before.status != GameStatus.Won && after.status == GameStatus.Won)
                WorkshopAudio.Play(WorkshopSound.Win);
        }
        internal static void FrameChanged(LevelData level, RuleBookData book, BoardState before, BoardState after, bool initialMove)
        {
            FrameAudioCue cues = DescribeFrame(level, book, before, after, initialMove);
            if ((cues & FrameAudioCue.Mechanism) != 0) WorkshopAudio.Play(WorkshopSound.Gate);
            if ((cues & FrameAudioCue.Rotation) != 0) WorkshopAudio.Play(WorkshopSound.Rotate);
            if ((cues & FrameAudioCue.IceSlide) != 0) WorkshopAudio.Play(WorkshopSound.IceSlide);
            if ((cues & FrameAudioCue.Teleport) != 0) WorkshopAudio.Play(WorkshopSound.Teleport);
            if ((cues & FrameAudioCue.Fall) != 0) WorkshopAudio.Play(WorkshopSound.Fall);
        }
        internal static FrameAudioCue DescribeFrame(LevelData level, RuleBookData book, BoardState before, BoardState after, bool initialMove)
        {
            if (level == null || book == null || before == null || after == null || ReferenceEquals(before, after)) return FrameAudioCue.None;
            FrameAudioCue cues = FrameAudioCue.None;
            if (level.edges != null) foreach (var edge in level.edges)
                if (edge != null && edge.kind == EdgeKind.Gate &&
                    RuleEngine.IsGateOpen(level, book, before, edge) != RuleEngine.IsGateOpen(level, book, after, edge))
                { cues |= FrameAudioCue.Mechanism; break; }
            int cellCount = Math.Min(before.tiles.Length, after.tiles.Length);
            for (int cell = 0; cell < cellCount; cell++)
            {
                var oldRule = book.Find(before.tiles[cell]); var rule = book.Find(after.tiles[cell]);
                if (rule == null) continue;
                if (rule.visual == TerrainVisual.Turn && RuleEngine.GetRotation(level, before, cell) != RuleEngine.GetRotation(level, after, cell))
                    cues |= FrameAudioCue.Rotation;
                if (rule.visual == TerrainVisual.Plate && RuleEngine.IsSignalActive(level, book, before, cell) != RuleEngine.IsSignalActive(level, book, after, cell))
                    cues |= FrameAudioCue.Mechanism;
                if (oldRule != null && oldRule.visual == TerrainVisual.Fragile && rule.visual == TerrainVisual.Hole)
                    cues |= FrameAudioCue.Mechanism;
                if (rule.visual == TerrainVisual.Gate)
                {
                    bool oldOpen = RuleEngine.CanOccupy(level, book, before, cell, ActorMask.Player, true) || RuleEngine.CanOccupy(level, book, before, cell, ActorMask.Box, true);
                    bool newOpen = RuleEngine.CanOccupy(level, book, after, cell, ActorMask.Player, true) || RuleEngine.CanOccupy(level, book, after, cell, ActorMask.Box, true);
                    if (oldOpen != newOpen) cues |= FrameAudioCue.Mechanism;
                }
            }
            int boxes = Math.Min(before.boxes.Count, after.boxes.Count);
            for (int box = 0; box < boxes; box++)
            {
                int from = before.boxes[box], to = after.boxes[box];
                if (from >= 0 && to < 0) { cues |= FrameAudioCue.Fall; continue; }
                if (initialMove || from == to || !level.Contains(from) || !level.Contains(to)) continue;
                var a = level.Position(from); var b = level.Position(to);
                if (Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) > 1) { cues |= FrameAudioCue.Teleport; continue; }
                // Adjacent endpoints still teleport, but the ordinary first push is never treated as a jump.
                var source = book.Find(before.tiles[from]); var destination = book.Find(after.tiles[to]);
                if (source != null && source.visual == TerrainVisual.Ice)
                    cues |= FrameAudioCue.IceSlide;
                if (source != null && destination != null && source.visual == TerrainVisual.PortalEntrance && destination.visual == TerrainVisual.PortalExit)
                {
                    var sourceCell = level.GetCell(from); var targetCell = level.GetCell(to);
                    if (sourceCell != null && targetCell != null && !string.IsNullOrEmpty(sourceCell.portalPair) && sourceCell.portalPair == targetCell.portalPair)
                        cues |= FrameAudioCue.Teleport;
                }
            }
            return cues;
        }
    }
}
