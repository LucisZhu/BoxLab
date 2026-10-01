using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoxLab
{
    public enum PlayerEditLayer { Recent, Ground, Actor }

    public struct PlayerEditTarget
    {
        public int cell;
        public Direction side;
        public bool edge;
        public bool valid;
        public static PlayerEditTarget Ground(int cell) => new PlayerEditTarget { cell = cell, valid = cell >= 0 };
        public static PlayerEditTarget Boundary(int cell, Direction side) => new PlayerEditTarget { cell = cell, side = side, edge = true, valid = cell >= 0 };
        public string Key => edge ? "e:" + cell + ":" + side : "c:" + cell;
    }

    public sealed class PlayerEditLink
    {
        public string sourceId;
        public int sourceCell;
        public PlayerEditTarget target;
        public string targetId;
        public string Key => sourceId + ":" + target.Key;
    }

    /// <summary>Reversible runtime authoring operations. Never writes assets or mutates the rule book.</summary>
    public sealed class PlayerEditing
    {
        public const string Player = "$player", Box = "$box", Wall = "$wall";
        public const int MaxDescriptionLength = 400;
        public LevelData Level { get; private set; }
        public RuleBookData Book { get; }
        public event Action Changed;
        readonly List<LevelData> undo = new List<LevelData>(), redo = new List<LevelData>();
        LevelData strokeStart;
        string saved;
        public bool Dirty => saved != JsonUtility.ToJson(Level);
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public PlayerEditing(LevelData level, RuleBookData book)
        {
            Level = level ?? LevelData.Create(5, 5); Book = book; saved = JsonUtility.ToJson(Level);
        }
        public void MarkSaved() { saved = JsonUtility.ToJson(Level); }
        public void Rename(string name)
        {
            name = (name ?? ""); if (name.Length > 80) name = name.Substring(0, 80);
            if (Level.name == name) return;
            string value = name; Mutate(() => Level.name = value);
        }
        public bool SetDescription(string description)
        {
            description = description ?? "";
            if (description.Length > MaxDescriptionLength)
                throw new ArgumentException("关卡说明最多 " + MaxDescriptionLength + " 字，请缩短后再应用。", nameof(description));
            if ((Level.description ?? "") == description) return false;
            string value = description; Mutate(() => Level.description = value); return true;
        }
        public void BeginStroke() { if (strokeStart == null) strokeStart = Level.Clone(); }
        public void EndStroke()
        {
            if (strokeStart == null) return;
            if (JsonUtility.ToJson(strokeStart) != JsonUtility.ToJson(Level))
            { undo.Add(strokeStart); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); }
            strokeStart = null;
        }
        void Mutate(Action change)
        {
            bool own = strokeStart == null; BeginStroke(); change(); if (own) EndStroke(); Changed?.Invoke();
        }
        public bool Undo()
        {
            EndStroke(); if (!CanUndo) return false; redo.Add(Level.Clone()); Level = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); Changed?.Invoke(); return true;
        }
        public bool Redo()
        {
            EndStroke(); if (!CanRedo) return false; undo.Add(Level.Clone()); Level = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); Changed?.Invoke(); return true;
        }
        public PlayerEditTarget Normalize(PlayerEditTarget target)
        {
            if (!target.valid || !Level.Contains(target.cell)) { target.valid = false; return target; }
            if (target.edge) Level.NormalizeEdge(ref target.cell, ref target.side);
            return target;
        }
        public string MaterialAt(PlayerEditTarget target, bool groundOnly = false)
        { return MaterialAt(target, groundOnly ? PlayerEditLayer.Ground : PlayerEditLayer.Recent); }
        public string MaterialAt(PlayerEditTarget target, PlayerEditLayer layer)
        {
            target = Normalize(target); if (!target.valid) return null;
            if (target.edge) { var e = Level.FindEdge(target.cell, target.side); return e == null || e.kind == EdgeKind.Open ? null : e.kind == EdgeKind.Wall ? Wall : e.ruleId; }
            var cell = Level.cells[target.cell];
            // Ordinary floor is the permanent base. A later special floor can be selected
            // above an existing actor, without changing how that actor behaves in play.
            if (layer != PlayerEditLayer.Ground && (layer == PlayerEditLayer.Actor || cell.editTopLayer != 1 || cell.ruleId == PlayerRules.Plain))
            { if (Level.player == target.cell) return Player; if (Level.boxes.Contains(target.cell)) return Box; }
            if (layer == PlayerEditLayer.Actor) return null;
            return Level.cells[target.cell].ruleId;
        }
        bool HasActor(int cell) => Level.player == cell || Level.boxes.Contains(cell);
        bool ContainsMaterial(PlayerEditTarget target, string material)
        {
            if (!target.valid || string.IsNullOrEmpty(material)) return false;
            if (target.edge) return MaterialAt(target) == material;
            if (material == Player) return Level.player == target.cell;
            if (material == Box) return Level.boxes.Contains(target.cell);
            return Level.cells[target.cell].ruleId == material;
        }
        public bool IsEdgeMaterial(string material) => material == Wall || (Book.Find(material)?.boundary ?? false);
        public bool IsActor(string material) => material == Player || material == Box;
        bool ActorAllowed(int cell, ActorMask actor)
        {
            var r = Book.Find(Level.cells[cell].ruleId);
            return r != null && r.visual != TerrainVisual.Hole && (r.allowedActors & actor) != 0;
        }
        public bool CanPlace(string material, PlayerEditTarget target, out string reason, PlayerEditTarget? source = null)
        {
            reason = ""; target = Normalize(target);
            if (!target.valid) { reason = "请放在地图内的格子或边缝。"; return false; }
            if (source.HasValue)
            {
                var origin = Normalize(source.Value);
                if (!ContainsMaterial(origin, material))
                { reason = "原物件已变化，请重新选取。"; return false; }
            }
            bool boundary = IsEdgeMaterial(material);
            if (boundary != target.edge) { reason = boundary ? "墙和门需要放在两格之间的边缝。" : "请放在格子内部。"; return false; }
            if (target.edge)
            {
                if (material != Wall && Level.Neighbor(target.cell, target.side) < 0) { reason = "地图外没有相邻格子，外圈只能放墙。"; return false; }
                var e = Level.FindEdge(target.cell, target.side);
                if (source.HasValue && Normalize(source.Value).Key != target.Key && e != null && e.kind == EdgeKind.Gate) { reason = "这里已有一扇门，请先移开或删除。"; return false; }
                return true;
            }
            if (IsActor(material))
            {
                bool own = source.HasValue && source.Value.cell == target.cell;
                if (Level.boxes.Contains(target.cell) && !(own && material == Box)) { reason = "这里已有箱子。"; return false; }
                if (Level.player == target.cell && material != Player) { reason = "这里已有人物。"; return false; }
                if (!ActorAllowed(target.cell, material == Player ? ActorMask.Player : ActorMask.Box)) { reason = "该地面不允许这个对象作为初始物件。"; return false; }
                return true;
            }
            var rule = Book.Find(material);
            if (rule == null || rule.boundary) { reason = "找不到这种地块。"; return false; }
            if (Level.player == target.cell && (rule.visual == TerrainVisual.Hole || (rule.allowedActors & ActorMask.Player) == 0)) { reason = "先移开人物，再放这种地面。"; return false; }
            if (Level.boxes.Contains(target.cell) && (rule.visual == TerrainVisual.Hole || (rule.allowedActors & ActorMask.Box) == 0)) { reason = "先移开箱子，再放这种地面。"; return false; }
            if (source.HasValue && source.Value.cell != target.cell && Level.cells[target.cell].ruleId != PlayerRules.Plain)
            { reason = "移动现有地块需要一块普通地面，原件会保留到放置成功。"; return false; }
            return true;
        }
        CellData Fresh(string material, int rotation)
        {
            var c = new CellData { ruleId = material, rotation = (rotation + 4) % 4 };
            if (material == PlayerRules.Plate || material == PlayerRules.RotationPlate) c.signalStyle = UnityEngine.Random.Range(0, 15);
            if (material == PlayerRules.PortalEntrance || material == PlayerRules.PortalExit)
            {
                string other = material == PlayerRules.PortalEntrance ? PlayerRules.PortalExit : PlayerRules.PortalEntrance;
                var unpaired = Level.cells.FirstOrDefault(t => t.ruleId == other && !string.IsNullOrEmpty(t.portalPair) && !Level.cells.Any(o => o.ruleId == material && o.portalPair == t.portalPair));
                c.portalPair = unpaired != null ? unpaired.portalPair : "P" + Guid.NewGuid().ToString("N").Substring(0, 7);
            }
            return c;
        }
        public bool Place(string material, PlayerEditTarget target, int rotation, out string reason, PlayerEditTarget? source = null)
        {
            target = Normalize(target); if (!CanPlace(material, target, out reason, source)) return false;
            var origin = source.HasValue ? Normalize(source.Value) : default(PlayerEditTarget);
            Mutate(() =>
            {
                if (target.edge)
                {
                    EdgeData moved = origin.valid ? Level.FindEdge(origin.cell, origin.side) : null;
                    var existing = Level.FindEdge(target.cell, target.side);
                    if (origin.valid && origin.Key == target.Key) return;
                    if (!origin.valid && existing != null && ((material == Wall && existing.kind == EdgeKind.Wall) || (material != Wall && existing.kind == EdgeKind.Gate && existing.ruleId == material))) return;
                    if (moved != null) Level.edges.Remove(moved);
                    if (existing != null) Level.edges.Remove(existing);
                    var edge = moved ?? new EdgeData { kind = material == Wall ? EdgeKind.Wall : EdgeKind.Gate, ruleId = material == Wall ? "" : material };
                    edge.cell = target.cell; edge.direction = target.side; Level.edges.Add(edge);
                }
                else if (material == Player)
                {
                    if (Level.player == target.cell) return;
                    if (Level.Contains(Level.player)) Level.cells[Level.player].editTopLayer = 0;
                    Level.player = target.cell; Level.cells[target.cell].editTopLayer = 0;
                }
                else if (material == Box)
                {
                    if (origin.valid && origin.cell == target.cell) return;
                    if (origin.valid) { Level.boxes.Remove(origin.cell); Level.cells[origin.cell].editTopLayer = 0; }
                    if (!Level.boxes.Contains(target.cell)) Level.boxes.Add(target.cell);
                    Level.cells[target.cell].editTopLayer = 0;
                }
                else if (origin.valid)
                {
                    var moving = Level.cells[origin.cell]; moving.rotation = (rotation + 4) % 4;
                    if (origin.cell != target.cell)
                    {
                        var empty = Level.cells[target.cell]; Level.cells[target.cell] = moving; Level.cells[origin.cell] = empty;
                        moving.editTopLayer = HasActor(target.cell) && moving.ruleId != PlayerRules.Plain ? 1 : 0;
                        empty.editTopLayer = 0;
                    }
                }
                else if (Level.cells[target.cell].ruleId == material)
                    Level.cells[target.cell].rotation = (rotation + 4) % 4;
                else
                {
                    ClearReferences(Level.cells[target.cell].id); Level.cells[target.cell] = Fresh(material, rotation);
                    Level.cells[target.cell].editTopLayer = HasActor(target.cell) && material != PlayerRules.Plain ? 1 : 0;
                }
            });
            return true;
        }
        public bool Remove(PlayerEditTarget target, string material)
        {
            target = Normalize(target);
            if (!ContainsMaterial(target, material) || material == PlayerRules.Plain) return false;
            Mutate(() =>
            {
                if (target.edge) { var edge = Level.FindEdge(target.cell, target.side); if (edge != null) Level.edges.Remove(edge); }
                else if (material == Player) { Level.player = -1; Level.cells[target.cell].editTopLayer = 0; }
                else if (material == Box) { Level.boxes.Remove(target.cell); Level.cells[target.cell].editTopLayer = 0; }
                else { ClearReferences(Level.cells[target.cell].id); Level.cells[target.cell] = Fresh(PlayerRules.Plain, 0); }
            });
            return true;
        }
        void ClearReferences(string id)
        {
            foreach (var c in Level.cells) foreach (var b in c.bindings) b.cellIds.RemoveAll(s => s == id);
            foreach (var e in Level.edges) foreach (var b in e.bindings) b.cellIds.RemoveAll(s => s == id);
        }
        public bool Rotate(PlayerEditTarget target, string material, int quarterTurns)
        {
            target = Normalize(target); if (target.edge || !ContainsMaterial(target, material) || IsActor(material)) return false;
            Mutate(() => Level.cells[target.cell].rotation = (Level.cells[target.cell].rotation + quarterTurns + 4) % 4); return true;
        }
        public bool Link(int sourceCell, PlayerEditTarget target, out string reason)
        {
            reason = ""; target = Normalize(target);
            if (!Level.Contains(sourceCell) || !target.valid) { reason = "请先选按钮，再选对应的门或箭头。"; return false; }
            var source = Level.cells[sourceCell]; List<BindingData> bindings;
            if (source.ruleId == PlayerRules.Plate && target.edge)
            {
                var edge = Level.FindEdge(target.cell, target.side);
                if (edge == null || edge.kind != EdgeKind.Gate) { reason = "普通按钮只能连接门。"; return false; }
                bindings = edge.bindings;
            }
            else if (source.ruleId == PlayerRules.RotationPlate && !target.edge && Level.cells[target.cell].ruleId == PlayerRules.Arrow) bindings = Level.cells[target.cell].bindings;
            else { reason = source.ruleId == PlayerRules.RotationPlate ? "旋转按钮只能连接箭头格。" : "普通按钮只能连接门。"; return false; }
            var binding = bindings.Find(b => b.key == PlayerRules.Switches);
            if (binding != null && binding.cellIds.Contains(source.id)) { reason = "这条连线已经存在。"; return false; }
            if (binding != null && binding.cellIds.Count >= 3) { reason = "一个目标最多关联三个按钮，请先删去一条连线。"; return false; }
            Mutate(() => { if (binding == null) { binding = new BindingData { key = PlayerRules.Switches }; bindings.Add(binding); } binding.cellIds.Add(source.id); });
            return true;
        }
        public List<PlayerEditLink> Links()
        {
            var result = new List<PlayerEditLink>();
            Action<PlayerEditTarget, string, List<BindingData>> add = (target, id, bindings) =>
            {
                var b = bindings.Find(x => x.key == PlayerRules.Switches); if (b == null) return;
                foreach (string source in b.cellIds) { int at = Level.FindCellIndex(source); if (at >= 0) result.Add(new PlayerEditLink { sourceId = source, sourceCell = at, target = target, targetId = id }); }
            };
            for (int i = 0; i < Level.cells.Count; i++) if (Level.cells[i].ruleId == PlayerRules.Arrow) add(PlayerEditTarget.Ground(i), Level.cells[i].id, Level.cells[i].bindings);
            foreach (var e in Level.edges) if (e.kind == EdgeKind.Gate) add(PlayerEditTarget.Boundary(e.cell, e.direction), "", e.bindings);
            return result;
        }
        public bool RemoveLink(PlayerEditLink link)
        {
            var target = Normalize(link.target); if (!target.valid) return false;
            var bindings = target.edge ? Level.FindEdge(target.cell, target.side)?.bindings : Level.cells[target.cell].bindings;
            var binding = bindings?.Find(b => b.key == PlayerRules.Switches); if (binding == null || !binding.cellIds.Contains(link.sourceId)) return false;
            Mutate(() => binding.cellIds.Remove(link.sourceId)); return true;
        }
        public void Resize(int width, int height)
        {
            width = Mathf.Clamp(width, 2, 32); height = Mathf.Clamp(height, 2, 32); if (width == Level.width && height == Level.height) return;
            Mutate(() =>
            {
                int oldWidth = Level.width, oldHeight = Level.height;
                Func<int, int> remap = old => old < 0 || old % oldWidth >= width || old / oldWidth >= height ? -1 : old / oldWidth * width + old % oldWidth;
                var cells = new List<CellData>();
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) cells.Add(x < oldWidth && y < oldHeight ? Level.cells[y * oldWidth + x] : Fresh(PlayerRules.Plain, 0));
                var removed = Level.cells.Where(c => !cells.Contains(c)).Select(c => c.id).ToList(); foreach (var id in removed) ClearReferences(id);
                Level.player = remap(Level.player); Level.boxes = Level.boxes.Select(remap).Where(i => i >= 0).ToList();
                var edges = new List<EdgeData>();
                foreach (var e in Level.edges)
                {
                    int at = remap(e.cell); if (at < 0) continue;
                    if (e.kind == EdgeKind.Gate && ((e.direction == Direction.East && at % width == width - 1) || (e.direction == Direction.North && at / width == height - 1))) continue;
                    e.cell = at; edges.Add(e);
                }
                Level.width = width; Level.height = height; Level.cells = cells; Level.edges = edges;
            });
        }
    }
}
