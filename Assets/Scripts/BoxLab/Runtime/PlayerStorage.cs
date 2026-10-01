using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BoxLab
{
    [Serializable] public sealed class MapDocument
    {
        public string format = "BoxLab.Map";
        public int version = 2;
        public string id = Guid.NewGuid().ToString("N");
        public string modifiedUtc = "";
        public LevelData level;
        public MapDocument Clone() { return JsonUtility.FromJson<MapDocument>(JsonUtility.ToJson(this)); }
    }

    [Serializable] public sealed class PlaySave
    {
        public int version = 2;
        public string mapId, mapHash, savedUtc;
        public bool builtIn;
        public LevelData level;
        public BoardState state;
        public List<BoardState> history = new List<BoardState>();
    }

    [Serializable] public sealed class PlayerPreferences
    {
        public bool fullScreen, reducedMotion;
        public bool hideEffects;
        public bool editorHelpSeen;
        public float audioVolume = .65f;
        public bool audioMuted;
        public List<string> completed = new List<string>();
    }

    /// <summary>Drafts and play sessions are independent, versioned files. No UnityEditor APIs.</summary>
    public sealed class PlayerStorage
    {
        public const int MaxMapBytes = 2 * 1024 * 1024;
        public const int MaxShareCodeChars = 4 * 1024 * 1024;
        public const string ShareCodePrefix = "BL2:";
        public readonly string Root;
        public string MapsPath { get { return Path.Combine(Root, "Maps"); } }
        public string SharePath { get { return Path.Combine(Root, "SharedMaps"); } }
        public string LastWarning { get; private set; }
        public PlayerStorage(string root = null)
        {
            Root = root ?? Path.Combine(Application.persistentDataPath, "WorkshopV2");
            Directory.CreateDirectory(MapsPath); Directory.CreateDirectory(SharePath);
        }
        public static string Fingerprint(LevelData level)
        {
            // Deletion order is editor metadata, not a gameplay revision. Omitting this added
            // field also preserves hashes in progress files written before it existed.
            string json = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(level), ",\"editTopLayer\":-?\\d+(?=[,}])", "");
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }
        private static string SafeId(string id)
        {
            Guid value;
            if (!Guid.TryParseExact(id, "N", out value)) throw new InvalidDataException("地图编号无效。");
            return value.ToString("N");
        }
        public List<MapDocument> ListMaps()
        {
            var result = new List<MapDocument>(); LastWarning = "";
            foreach (var path in Directory.GetFiles(MapsPath, "*.boxmap"))
            {
                try { result.Add(ReadMapWithBackup(path)); }
                catch (Exception ex) { LastWarning += Path.GetFileName(path) + "：" + ex.Message + "\n"; }
            }
            result.Sort((a, b) => string.CompareOrdinal(b.modifiedUtc, a.modifiedUtc));
            return result;
        }
        public void SaveMap(MapDocument document)
        {
            CheckDocument(document); string id = SafeId(document.id);
            string oldTime = document.modifiedUtc; document.modifiedUtc = DateTime.UtcNow.ToString("o");
            try { AtomicWrite(Path.Combine(MapsPath, id + ".boxmap"), JsonUtility.ToJson(document, true)); }
            catch { document.modifiedUtc = oldTime; throw; }
        }
        public MapDocument LoadMap(string id) { return ReadMapWithBackup(Path.Combine(MapsPath, SafeId(id) + ".boxmap")); }
        public bool Exists(string id) { return File.Exists(Path.Combine(MapsPath, SafeId(id) + ".boxmap")); }
        public void DeleteMap(string id)
        {
            // Removal is recoverable: move both current file and backup into a local archive.
            string source = Path.Combine(MapsPath, SafeId(id) + ".boxmap");
            string archive = Path.Combine(Root, "Deleted", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Directory.CreateDirectory(archive);
            if (File.Exists(source)) File.Move(source, Path.Combine(archive, Path.GetFileName(source)));
            if (File.Exists(source + ".bak")) File.Move(source + ".bak", Path.Combine(archive, Path.GetFileName(source) + ".bak"));
        }
        public string Export(MapDocument document, string path = null)
        {
            CheckDocument(document);
            if (string.IsNullOrWhiteSpace(path))
            {
                string name = document.level.name ?? "地图";
                foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
                name = name.Trim().TrimEnd('.'); if (name.Length == 0) name = "地图";
                if (name.Length > 50) name = name.Substring(0, 50);
                path = Path.Combine(SharePath, name + "-" + DateTime.Now.ToString("MMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) + ".boxmap");
            }
            AtomicWrite(Path.GetFullPath(path), JsonUtility.ToJson(document, true)); return path;
        }
        public MapDocument Import(string path)
        {
            return SaveImportedCopy(ReadMap(path));
        }
        /// <summary>Portable text: version prefix, then Base64 of SHA256(gzip) followed by gzip JSON.</summary>
        public string ExportShareCode(MapDocument document)
        {
            CheckDocument(document);
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(document));
            if (json.Length > MaxMapBytes) throw new InvalidDataException("地图内容超过 2 MB 上限，无法生成分享码。");
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(json, 0, json.Length);
                compressed = output.ToArray();
            }
            byte[] payload = new byte[32 + compressed.Length];
            using (var sha = SHA256.Create()) Buffer.BlockCopy(sha.ComputeHash(compressed), 0, payload, 0, 32);
            Buffer.BlockCopy(compressed, 0, payload, 32, compressed.Length);
            string code = ShareCodePrefix + Convert.ToBase64String(payload);
            if (code.Length > MaxShareCodeChars) throw new InvalidDataException("地图分享码超过长度上限。");
            return code;
        }
        public MapDocument ImportShareCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidDataException("请先粘贴地图分享码。");
            if (code.Length > MaxShareCodeChars) throw new InvalidDataException("分享码过长，请复制游戏生成的完整分享码。");
            var compact = new StringBuilder(code.Length);
            foreach (char ch in code) if (!char.IsWhiteSpace(ch)) compact.Append(ch);
            string text = compact.ToString();
            if (!text.StartsWith(ShareCodePrefix, StringComparison.Ordinal)) throw new InvalidDataException("分享码格式或版本不支持，需要以 BL2: 开头。");
            byte[] payload;
            try { payload = Convert.FromBase64String(text.Substring(ShareCodePrefix.Length)); }
            catch (FormatException ex) { throw new InvalidDataException("分享码含有无效字符或不完整，请重新复制。", ex); }
            if (payload.Length < 50) throw new InvalidDataException("分享码不完整，请重新复制。");
            if (payload.Length > MaxMapBytes + 65536 + 32) throw new InvalidDataException("分享码内容超过大小上限。");
            // Verify the compressed bytes before unpacking. This also rejects a missing gzip trailer
            // on runtimes that otherwise tolerate truncated compressed streams.
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(payload, 32, payload.Length - 32);
            int different = 0;
            for (int i = 0; i < hash.Length; i++) different |= hash[i] ^ payload[i];
            if (different != 0) throw new InvalidDataException("分享码内容损坏或被截断，请重新复制。");
            string json;
            try
            {
                using (var input = new MemoryStream(payload, 32, payload.Length - 32, false))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + count > MaxMapBytes) throw new InvalidDataException("分享码解压后的地图超过 2 MB 上限。");
                        output.Write(buffer, 0, count);
                    }
                    json = new UTF8Encoding(false, true).GetString(output.ToArray());
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is DecoderFallbackException)
            { throw new InvalidDataException("分享码无法解压，内容损坏或超过 2 MB 上限，请重新复制。", ex); }
            // No disk writes occur until all encoded content and the draft schema are validated.
            return SaveImportedCopy(ParseMap(json));
        }
        private MapDocument SaveImportedCopy(MapDocument document)
        {
            CheckDocument(document); document.id = Guid.NewGuid().ToString("N");
            document.level.id = Guid.NewGuid().ToString("N"); document.level.name += " · 导入";
            if (document.level.name.Length > 80) document.level.name = document.level.name.Substring(0, 80);
            if (Encoding.UTF8.GetByteCount(JsonUtility.ToJson(document, true)) > MaxMapBytes - 64)
                throw new InvalidDataException("地图导入后的文件超过 2 MB 上限。");
            SaveMap(document); return document;
        }
        public static MapDocument ReadMap(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("找不到地图文件。", path);
            if (info.Length > MaxMapBytes) throw new InvalidDataException("地图文件超过 2 MB 上限。");
            return ParseMap(File.ReadAllText(path, Encoding.UTF8));
        }
        private static MapDocument ReadMapWithBackup(string path)
        {
            try { return ReadMap(path); }
            catch { if (File.Exists(path + ".bak")) return ReadMap(path + ".bak"); throw; }
        }
        public static MapDocument ParseMap(string json)
        {
            CheckJson(json, MaxMapBytes);
            MapDocument document;
            try { document = JsonUtility.FromJson<MapDocument>(json); }
            catch (Exception ex) { throw new InvalidDataException("不是有效的 BoxLab 地图文件。", ex); }
            CheckDocument(document); return document;
        }
        public static void CheckDocument(MapDocument document)
        {
            if (document == null || document.format != "BoxLab.Map" || document.version != 2) throw new InvalidDataException("地图格式或版本不支持（需要 BoxLab v2）。");
            SafeId(document.id); var l = document.level;
            if (l == null || l.width < 2 || l.height < 2 || l.width > 32 || l.height > 32 || l.cells == null || l.cells.Count != l.width * l.height)
                throw new InvalidDataException("地图尺寸或格子数据损坏。");
            if (l.name == null || l.name.Length > 80 || (l.description ?? "").Length > 2000) throw new InvalidDataException("地图名称或说明过长。");
            var book = PlayerRules.CreateBook(); var ids = new HashSet<string>();
            foreach (var cell in l.cells)
            {
                if (cell == null || string.IsNullOrEmpty(cell.id) || cell.id.Length > 64 || !ids.Add(cell.id) || book.Find(cell.ruleId) == null || cell.ruleId == "gate") throw new InvalidDataException("格子编号重复或素材无效。");
                if (cell.rotation < 0 || cell.rotation > 3 || (cell.portalPair ?? "").Length > 64 || cell.signalStyle < -1 || cell.signalStyle > 14) throw new InvalidDataException("格子方向或标记无效。");
                if (cell.editTopLayer < 0 || cell.editTopLayer > 1) throw new InvalidDataException("格子的编辑层级标记无效。");
                CheckBindings(cell.bindings);
            }
            if (l.player < -1 || l.player >= l.cells.Count || l.boxes == null || l.boxes.Count > l.cells.Count || l.edges == null || l.edges.Count > l.cells.Count * 4) throw new InvalidDataException("物体数据损坏。");
            var boxes = new HashSet<int>(); foreach (int box in l.boxes) if (!l.Contains(box) || !boxes.Add(box) || box == l.player) throw new InvalidDataException("箱子位置无效或重叠。");
            var edges = new HashSet<string>();
            foreach (var edge in l.edges)
            {
                if (edge == null || !l.Contains(edge.cell) || (int)edge.direction < 0 || (int)edge.direction > 3 || (int)edge.kind < 0 || (int)edge.kind > 2) throw new InvalidDataException("边数据无效。");
                int cell = edge.cell; var direction = edge.direction; l.NormalizeEdge(ref cell, ref direction);
                if (!edges.Add(cell + ":" + direction)) throw new InvalidDataException("同一条边重复定义。");
                if (edge.kind == EdgeKind.Gate && edge.ruleId != "gate") throw new InvalidDataException("未知门类型。");
                CheckBindings(edge.bindings);
            }
            // Missing actors/goals/pairs/connections are unfinished drafts, not corrupted files.
        }
        private static void CheckBindings(List<BindingData> bindings)
        {
            if (bindings == null || bindings.Count > 8) throw new InvalidDataException("连接数据无效。");
            foreach (var b in bindings)
            {
                if (b == null || (b.key ?? "").Length > 64 || b.cellIds == null || b.cellIds.Count > 1024) throw new InvalidDataException("连接数据超出范围。");
                foreach (string id in b.cellIds) if (id == null || id.Length > 64) throw new InvalidDataException("连接目标无效。");
            }
        }
        private static void CheckJson(string json, int max)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > max) throw new InvalidDataException("文件为空或过大。");
            int depth = 0; bool quoted = false, escaped = false;
            foreach (char c in json)
            {
                if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                if (c == '"') quoted = true;
                else if (c == '{' || c == '[') { if (++depth > 40) throw new InvalidDataException("文件嵌套过深。"); }
                else if (c == '}' || c == ']') { if (--depth < 0) throw new InvalidDataException("文件结构不完整。"); }
            }
            if (depth != 0 || quoted) throw new InvalidDataException("文件结构不完整。");
        }
        public void SaveProgress(PlaySave save)
        {
            if (save == null) return;
            save.savedUtc = DateTime.UtcNow.ToString("o");
            AtomicWrite(Path.Combine(Root, "continue.json"), JsonUtility.ToJson(save));
        }
        public PlaySave LoadProgress()
        {
            string path = Path.Combine(Root, "continue.json"); if (!File.Exists(path)) return null;
            var info = new FileInfo(path); if (info.Length > 32 * 1024 * 1024) throw new InvalidDataException("进度文件过大。");
            string json = File.ReadAllText(path); CheckJson(json, 32 * 1024 * 1024);
            var save = JsonUtility.FromJson<PlaySave>(json);
            if (save == null || save.version != 2 || save.level == null || Fingerprint(save.level) != save.mapHash) throw new InvalidDataException("进度文件版本或地图不匹配。");
            CheckDocument(new MapDocument { level = save.level });
            if (LevelValidator.HasErrors(PlayerRules.Validate(save.level))) throw new InvalidDataException("进度所用地图无效。");
            if (save.history == null || save.history.Count > 512) throw new InvalidDataException("进度撤销记录无效。");
            ValidateState(save.level, save.state); foreach (var state in save.history) ValidateState(save.level, state);
            if (!save.builtIn && Exists(save.mapId) && Fingerprint(LoadMap(save.mapId).level) != save.mapHash) throw new InvalidDataException("这张地图已经修改，请从“我的关卡”重新开始。");
            return save;
        }
        public static void ValidateState(LevelData level, BoardState state)
        {
            if (state == null || !level.Contains(state.player) || state.tiles == null || state.tiles.Length != level.cells.Count || state.rotations == null || state.rotations.Length != level.cells.Count || state.boxes == null || state.boxes.Count != level.boxes.Count || state.moves < 0 || state.pushes < 0 || state.pushes > state.moves || (int)state.status < 0 || (int)state.status > 2) throw new InvalidDataException("进度中的局面无效。");
            var occupied = new HashSet<int>(); occupied.Add(state.player); var book = PlayerRules.CreateBook();
            foreach (int box in state.boxes) if (box != -1 && (!level.Contains(box) || !occupied.Add(box))) throw new InvalidDataException("进度物体位置无效。");
            for (int i = 0; i < state.tiles.Length; i++)
            {
                string original = level.cells[i].ruleId;
                var rule = book.Find(state.tiles[i]);
                if (rule == null || state.rotations[i] < 0 || state.rotations[i] > 3 || (state.tiles[i] != original && !(original == PlayerRules.Fragile && state.tiles[i] == PlayerRules.Hole))) throw new InvalidDataException("进度的地块或方向无效。");
                if (original != PlayerRules.Arrow && state.rotations[i] != level.cells[i].rotation) throw new InvalidDataException("进度改变了不可旋转地块的朝向。");
                ActorMask occupant = state.Occupant(i);
                if (occupant != ActorMask.None && (rule.visual == TerrainVisual.Hole || (rule.allowedActors & occupant) == 0)) throw new InvalidDataException("进度物体位于不可站立地块。");
            }
            GameStatus expected = RuleEngine.GetStatus(level, book, state);
            if (state.status != expected) throw new InvalidDataException("进度胜负状态与局面不一致。");
        }
        public PlayerPreferences LoadPreferences()
        {
            string path = Path.Combine(Root, "settings.json"); if (!File.Exists(path)) return new PlayerPreferences();
            try
            {
                string json = File.ReadAllText(path);
                var result = JsonUtility.FromJson<PlayerPreferences>(json);
                if (result == null || result.completed == null) return new PlayerPreferences();
                if (json.IndexOf("\"audioVolume\"", StringComparison.Ordinal) < 0) result.audioVolume = .65f;
                if (float.IsNaN(result.audioVolume) || float.IsInfinity(result.audioVolume)) result.audioVolume = .65f;
                result.audioVolume = Mathf.Clamp01(result.audioVolume);
                return result;
            }
            catch { return new PlayerPreferences(); }
        }
        public void SavePreferences(PlayerPreferences preferences) { AtomicWrite(Path.Combine(Root, "settings.json"), JsonUtility.ToJson(preferences, true)); }
        public static void AtomicWrite(string path, string text)
        {
            path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { byte[] bytes = new UTF8Encoding(false).GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
