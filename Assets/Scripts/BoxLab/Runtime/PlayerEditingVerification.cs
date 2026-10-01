using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Runs against the same operations used by drag/drop and palette input, without assets/UI.</summary>
    public static class PlayerEditingVerification
    {
        static PlayerEditing Fresh()
        {
            var level = LevelData.Create(5, 5, "编辑操作校验"); level.player = -1;
            return new PlayerEditing(level, PlayerRules.CreateBook());
        }
        static void Check(bool value, string message) { if (!value) throw new Exception("玩家编辑器校验失败：" + message); }
        static void Put(PlayerEditing e, string material, PlayerEditTarget target, PlayerEditTarget? source = null)
        { string reason; Check(e.Place(material, target, 0, out reason, source), material + " 放置失败：" + reason); }
        static PlayerEditTarget C(int at) => PlayerEditTarget.Ground(at);
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(PlayerEditor editor, string field, object value) { typeof(PlayerEditor).GetField(field, PrivateInstance).SetValue(editor, value); }
        static T Get<T>(PlayerEditor editor, string field) => (T)typeof(PlayerEditor).GetField(field, PrivateInstance).GetValue(editor);
        static void Call(PlayerEditor editor, string method, params object[] args) { typeof(PlayerEditor).GetMethod(method, PrivateInstance).Invoke(editor, args); }
        static void CheckPlacementEnded(PlayerEditor editor, string context)
        {
            Check(Get<string>(editor, "held") == null && !Get<PlayerEditTarget?>(editor, "heldSource").HasValue
                && !Get<PlayerEditTarget>(editor, "selected").valid && Get<string>(editor, "selectedMaterial") == null
                && Get<PlayerEditLink>(editor, "selectedLink") == null && Get<Rect>(editor, "selectionDeleteRect").width == 0,
                context + "后仍残留持物、选中目标或删除按钮。");
        }
        public static List<string> RunAll()
        {
            var results = new List<string>(); string reason;
            var e = Fresh(); var boundary = PlayerEditTarget.Boundary(6, Direction.East);
            Put(e, PlayerEditing.Wall, boundary); Put(e, PlayerRules.Gate, boundary);
            Check(e.Level.edges.Count == 1 && e.Level.FindEdge(7, Direction.West).kind == EdgeKind.Gate, "门未覆写唯一公共边。");
            e.Undo(); Check(e.Level.FindEdge(6, Direction.East).kind == EdgeKind.Wall, "覆写门撤销未恢复墙。"); e.Redo();
            Put(e, PlayerRules.Plate, C(0)); Check(e.Link(0, boundary, out reason), reason);
            Put(e, PlayerRules.Gate, boundary); Check(e.Links().Count == 1, "重复刷同一扇门丢失关联。");
            Put(e, PlayerEditing.Wall, PlayerEditTarget.Boundary(0, Direction.West));
            Check(!e.CanPlace(PlayerRules.Gate, PlayerEditTarget.Boundary(0, Direction.West), out reason), "外边界允许了无目的地门。");
            results.Add("玩家编辑：公共边墙门覆写、外墙、撤销与重复刷门保留关联。");

            string plateId = e.Level.cells[0].id; int style = e.Level.cells[0].signalStyle;
            Put(e, PlayerRules.Plate, C(10), C(0));
            Check(e.Level.FindCellIndex(plateId) == 10 && e.Links()[0].sourceCell == 10 && e.Level.cells[10].signalStyle == style, "移动按钮未保留颜色、身份及连线。");
            e.Remove(C(10), PlayerRules.Plate); Check(e.Links().Count == 0, "删除按钮没有清理引用。");
            e.Undo(); Check(e.Links().Count == 1 && e.Level.FindCellIndex(plateId) == 10, "撤销删除没有恢复按钮和关联。");
            results.Add("玩家编辑：按钮移动保留样式和连线，删除清理引用，撤销整体恢复。");

            e = Fresh(); Put(e, PlayerEditing.Box, C(2)); Put(e, PlayerRules.Hole, C(3));
            string before = JsonUtility.ToJson(e.Level);
            Check(!e.Place(PlayerEditing.Box, C(3), 0, out reason, C(2)), "非法拖放应被拒绝。");
            Check(JsonUtility.ToJson(e.Level) == before && e.Level.boxes.Contains(2), "非法拖放提前移除了原件。");
            Check(!e.Place(PlayerEditing.Box, C(4), 0, out reason, C(18)), "不存在的原件不应被复制。");
            results.Add("玩家编辑：非法拖放与失效原件均无副作用。");

            e = Fresh(); Put(e, PlayerRules.Selective, C(6)); Put(e, PlayerRules.BoxOnly, C(7)); e.MarkSaved();
            Check(e.CanPlace(PlayerEditing.Player, C(6), out reason) && !e.CanPlace(PlayerEditing.Box, C(6), out reason)
                && e.CanPlace(PlayerEditing.Box, C(7), out reason) && !e.CanPlace(PlayerEditing.Player, C(7), out reason),
                "人物通道与箱子通道没有分别限制初始物件。");
            string passageSnapshot = JsonUtility.ToJson(e.Level);
            Check(!e.Place(PlayerEditing.Box, C(6), 0, out reason) && !e.Place(PlayerEditing.Player, C(7), 0, out reason)
                && JsonUtility.ToJson(e.Level) == passageSnapshot && !e.Dirty, "不兼容物件摆放改变地图或保存点。");
            Put(e, PlayerEditing.Player, C(6)); Put(e, PlayerEditing.Box, C(7));
            passageSnapshot = JsonUtility.ToJson(e.Level);
            Check(!e.Place(PlayerRules.BoxOnly, C(6), 0, out reason) && !e.Place(PlayerRules.Selective, C(7), 0, out reason)
                && JsonUtility.ToJson(e.Level) == passageSnapshot, "通道覆盖没有保护已放置的人物/箱子。");
            Put(e, PlayerRules.BoxOnly, C(8)); Put(e, PlayerRules.Selective, C(9)); e.MarkSaved();
            passageSnapshot = JsonUtility.ToJson(e.Level);
            Check(!e.Place(PlayerEditing.Player, C(8), 0, out reason, C(6)) && !e.Place(PlayerEditing.Box, C(9), 0, out reason, C(7))
                && JsonUtility.ToJson(e.Level) == passageSnapshot && !e.Dirty, "拖到不兼容通道时原件或保存状态被改变。");
            Put(e, PlayerRules.Plain, C(7));
            Check(e.Undo() && e.Level.cells[7].ruleId == PlayerRules.BoxOnly && e.Level.boxes.Contains(7) && !e.Dirty,
                "撤销通道替换没有恢复箱子、预设或保存点。");
            Check(e.Redo() && e.Level.cells[7].ruleId == PlayerRules.Plain && e.Level.boxes.Contains(7), "重做通道替换误删箱子。");
            e.Undo();
            foreach (var restored in new[] { JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(e.Level)), e.Level.Clone() })
            {
                var restoredEdit = new PlayerEditing(restored, PlayerRules.CreateBook());
                Check(restored.cells[6].ruleId == PlayerRules.Selective && restored.cells[7].ruleId == PlayerRules.BoxOnly
                    && restored.player == 6 && restored.boxes.Contains(7) && !restoredEdit.Dirty
                    && restoredEdit.CanPlace(PlayerEditing.Box, C(8), out reason) && !restoredEdit.CanPlace(PlayerEditing.Player, C(8), out reason)
                    && restoredEdit.CanPlace(PlayerEditing.Player, C(9), out reason) && !restoredEdit.CanPlace(PlayerEditing.Box, C(9), out reason),
                    "JSON/Clone读回丢失两种通道、占位物件或对应准入权限。");
            }
            results.Add("玩家编辑：人物/箱子通道独立准入，非法覆盖与拖移保留原件，替换可撤销，JSON/Clone读回保持权限与保存状态。");

            e = Fresh(); e.BeginStroke(); Put(e, PlayerEditing.Box, C(1)); Put(e, PlayerEditing.Box, C(2)); Put(e, PlayerEditing.Box, C(3)); e.EndStroke();
            Check(e.Level.boxes.Count == 3, "连续笔刷漏放。"); e.Undo(); Check(e.Level.boxes.Count == 0, "一笔涂抹未整体撤销。");
            e.Redo(); e.MarkSaved(); Check(!e.Dirty, "保存后仍脏。"); e.Undo(); Check(e.Dirty, "回退到不同于保存状态时未标记脏。"); e.Redo(); Check(!e.Dirty, "重做回保存状态仍标记脏。");
            results.Add("玩家编辑：连续笔刷一次撤销，重做与保存状态准确。");

            e = Fresh(); Put(e, PlayerRules.PortalEntrance, C(1)); string pair = e.Level.cells[1].portalPair;
            Put(e, PlayerRules.PortalExit, C(12)); Check(!string.IsNullOrEmpty(pair) && e.Level.cells[12].portalPair == pair, "传送两端未自动配对。");
            string entranceId = e.Level.cells[1].id; Put(e, PlayerRules.PortalEntrance, C(8), C(1));
            Check(e.Level.FindCellIndex(entranceId) == 8 && e.Level.cells[8].portalPair == pair, "移动传送入口丢失配对。");
            Put(e, PlayerRules.Arrow, C(3)); e.Rotate(C(3), PlayerRules.Arrow, 1); Check(e.Level.cells[3].rotation == 1, "E 未转向东。");
            e.Rotate(C(3), PlayerRules.Arrow, -1); Check(e.Level.cells[3].rotation == 0, "Q 未转回北。");
            e.Resize(7, 6); Check(e.Level.FindCellIndex(entranceId) == 10 && e.Level.cells[10].portalPair == pair, "缩放网格破坏二维位置或传送身份。");
            results.Add("玩家编辑：传送自动配对、稳定搬移、四向旋转和尺寸调整。");

            e = Fresh(); boundary = PlayerEditTarget.Boundary(12, Direction.East); Put(e, PlayerRules.Gate, boundary);
            for (int i = 0; i < 4; i++) Put(e, PlayerRules.Plate, C(i));
            for (int i = 0; i < 3; i++) Check(e.Link(i, boundary, out reason), reason);
            Check(!e.Link(3, boundary, out reason), "门允许超过三条关联。");
            Put(e, PlayerRules.Arrow, C(16));
            for (int i = 5; i < 9; i++) Put(e, PlayerRules.RotationPlate, C(i));
            for (int i = 5; i < 8; i++) Check(e.Link(i, C(16), out reason), reason);
            Check(!e.Link(8, C(16), out reason) && !e.Link(0, C(16), out reason), "箭头接受了第四个或错误类别按钮。");
            int links = e.Links().Count; e.RemoveLink(e.Links()[0]); Check(e.Links().Count == links - 1, "删除一条线影响了其他连接。");
            results.Add("玩家编辑：按钮类别限制、门与箭头三路上限、单线删除。");

            e = Fresh(); Put(e, PlayerEditing.Box, C(6)); Put(e, PlayerRules.PortalEntrance, C(6));
            Check(e.MaterialAt(C(6)) == PlayerRules.PortalEntrance, "箱子后放地面，应优先选中后放地面。");
            e.Remove(C(6), e.MaterialAt(C(6)));
            Check(e.Level.boxes.Contains(6) && e.Level.cells[6].ruleId == PlayerRules.Plain && e.MaterialAt(C(6)) == PlayerEditing.Box, "删除上层地面误删箱子或未恢复普通地面。");
            e.Undo(); Check(e.MaterialAt(C(6)) == PlayerRules.PortalEntrance && e.Level.boxes.Contains(6), "撤销未还原地面优先及下层箱子。");
            e.Redo(); Check(e.MaterialAt(C(6)) == PlayerEditing.Box, "重做未恢复剩余箱子。");
            e = Fresh(); Put(e, PlayerRules.PortalEntrance, C(6)); Put(e, PlayerEditing.Box, C(6));
            Check(e.MaterialAt(C(6)) == PlayerEditing.Box, "地面后放箱子，应优先选中箱子。");
            e.MarkSaved(); Put(e, PlayerRules.PortalEntrance, C(6));
            Check(!e.Dirty && e.MaterialAt(C(6)) == PlayerEditing.Box, "重刷相同地面无变化时，错误改变层级或保存状态。");
            e.Remove(C(6), e.MaterialAt(C(6))); Check(e.Level.cells[6].ruleId == PlayerRules.PortalEntrance && !e.Level.boxes.Contains(6), "删除上层箱子误删下层地面。");
            results.Add("玩家编辑：按放置先后选择和删除，保留另一层，撤销重做恢复顺序，同材质空操作不变脏。");

            e = Fresh(); Put(e, PlayerEditing.Box, C(1)); Put(e, PlayerRules.PortalEntrance, C(1)); Put(e, PlayerEditing.Box, C(4));
            string movingId = e.Level.cells[1].id; Put(e, PlayerRules.PortalEntrance, C(4), C(1));
            Check(e.Level.cells[4].id == movingId && e.MaterialAt(C(4)) == PlayerRules.PortalEntrance && e.MaterialAt(C(1)) == PlayerEditing.Box, "移动地面未更新目的地顺序或误带走原箱子。");
            Put(e, PlayerEditing.Box, C(9), C(4));
            Check(e.MaterialAt(C(9)) == PlayerEditing.Box && e.Level.cells[4].ruleId == PlayerRules.PortalEntrance, "移动显式下层箱子被层级遮挡拒绝，或带走了地面。");
            Put(e, PlayerEditing.Box, C(4), C(9)); Check(e.MaterialAt(C(4)) == PlayerEditing.Box, "箱子移入后应成为最新一层。");
            e.Undo(); Check(e.MaterialAt(C(4)) == PlayerRules.PortalEntrance && e.MaterialAt(C(9)) == PlayerEditing.Box, "移动撤销未恢复层级。");
            Put(e, PlayerEditing.Player, C(12)); Put(e, PlayerRules.Ice, C(12));
            Check(e.MaterialAt(C(12)) == PlayerRules.Ice, "人物后放地面未更新顺序。");
            Put(e, PlayerEditing.Player, C(13)); Check(e.MaterialAt(C(12)) == PlayerRules.Ice && e.MaterialAt(C(13)) == PlayerEditing.Player, "人物移动破坏原地面。");
            results.Add("玩家编辑：移动地面及物件时更新目的地顺序，来源另一层保持原位，撤销恢复。");

            e = Fresh(); Put(e, PlayerEditing.Box, C(6)); Put(e, PlayerRules.PortalEntrance, C(6));
            string json = JsonUtility.ToJson(e.Level);
            var reloaded = new PlayerEditing(JsonUtility.FromJson<LevelData>(json), e.Book);
            Check(reloaded.MaterialAt(C(6)) == PlayerRules.PortalEntrance && !reloaded.Dirty, "JSON读回丢失删除顺序或误标脏。");
            var cloned = new PlayerEditing(e.Level.Clone(), e.Book); Check(cloned.MaterialAt(C(6)) == PlayerRules.PortalEntrance, "Clone丢失编辑层级。");
            var legacy = new PlayerEditing(JsonUtility.FromJson<LevelData>(json.Replace("\"editTopLayer\":", "\"unusedLegacyLayer\":")), e.Book);
            Check(legacy.MaterialAt(C(6)) == PlayerEditing.Box, "旧图缺少层字段时未按物件在上回退。");
            reloaded.Resize(7, 7); Check(reloaded.MaterialAt(C(8)) == PlayerRules.PortalEntrance && reloaded.Level.boxes.Contains(8), "尺寸变更丢失坐标或编辑层级。");
            string savedBefore = JsonUtility.ToJson(e.Level);
            Check(!e.Place(PlayerRules.Hole, C(6), 0, out reason) && JsonUtility.ToJson(e.Level) == savedBefore, "非法空洞覆盖改变了物件或层级。");
            Put(e, PlayerRules.Hole, C(8)); e.Remove(C(8), e.MaterialAt(C(8))); Put(e, PlayerEditing.Box, C(8));
            Check(e.Level.cells[8].ruleId == PlayerRules.Plain && e.MaterialAt(C(8)) == PlayerEditing.Box, "删除空洞后没有恢复可放物件的普通地面。");
            results.Add("玩家编辑：存读JSON、Clone、旧图回退及改尺寸保持顺序，非法覆盖无副作用，删除空洞恢复地面。");

            var draft = LevelData.Create(5, 5); draft.player = -1;
            using (var editor = new PlayerEditor(null, draft, PlayerRules.CreateBook()))
            {
                editor.DismissHelp(); var a = PlayerEditTarget.Boundary(6, Direction.East); var b = PlayerEditTarget.Boundary(12, Direction.North);
                Call(editor, "Select", a); Set(editor, "selectionDeleteRect", new Rect(10, 10, 28, 28));
                Set(editor, "held", PlayerEditing.Wall); Call(editor, "TryPlace", a, true);
                CheckPlacementEnded(editor, "单次素材拖放成功");
                Call(editor, "Select", b); Check(Get<PlayerEditTarget>(editor, "selected").valid && Get<PlayerEditTarget>(editor, "selected").Key == b.Key, "放置完成后再次单击选择另一条边未更新目标。");
                Set(editor, "held", PlayerEditing.Wall); string unchanged = JsonUtility.ToJson(editor.Level);
                Call(editor, "TryPlace", C(10), true);
                Check(Get<string>(editor, "held") == PlayerEditing.Wall && JsonUtility.ToJson(editor.Level) == unchanged, "非法落点未保留拿起素材，或破坏原地图。");
                Call(editor, "TryPlace", b, true); CheckPlacementEnded(editor, "失败后重试成功");
                Call(editor, "Select", b); Set(editor, "selectionDeleteRect", new Rect(10, 10, 28, 28));
                Set(editor, "held", PlayerEditing.Box); Set(editor, "painting", true); editor.Operations.BeginStroke();
                Call(editor, "PaintOne", C(2)); Call(editor, "PaintOne", C(3)); Call(editor, "CompletePainting");
                CheckPlacementEnded(editor, "连续涂抹完成"); Check(!Get<bool>(editor, "painting"), "涂抹结束仍保留绘制状态。");
                editor.Operations.Undo(); Check(editor.Level.boxes.Count == 0 && editor.Level.edges.Count == 2, "涂抹未整体撤销或影响之前墙体。");
                editor.Operations.Redo(); Check(editor.Level.boxes.Count == 2, "涂抹重做不完整。");

                Call(editor, "Select", C(3));
                Check(Get<PlayerEditTarget>(editor, "selected").valid && Get<string>(editor, "selectedMaterial") == PlayerEditing.Box, "涂抹完成后不能单独选中箱子。");
                Set(editor, "held", PlayerEditing.Box); Set(editor, "heldSource", (PlayerEditTarget?)C(3)); Set(editor, "selectionDeleteRect", new Rect(10, 10, 28, 28));
                string beforeMove = JsonUtility.ToJson(editor.Level); Call(editor, "TryPlace", C(2), true);
                Check(JsonUtility.ToJson(editor.Level) == beforeMove && Get<string>(editor, "held") == PlayerEditing.Box
                    && Get<PlayerEditTarget?>(editor, "heldSource").HasValue && Get<PlayerEditTarget?>(editor, "heldSource").Value.Key == C(3).Key,
                    "原件移动失败后丢失原件或重试来源。");
                Call(editor, "TryPlace", C(4), true); CheckPlacementEnded(editor, "原件移动成功");
                Check(!editor.Level.boxes.Contains(3) && editor.Level.boxes.Contains(4) && editor.Level.boxes.Count == 2, "原件移动位置错误或复制了额外箱子。");
                Call(editor, "Select", C(4)); Call(editor, "DeleteSelection");
                Check(!editor.Level.boxes.Contains(4), "移动完成后重新单击选中，无法删除新位置的箱子。");
                editor.Operations.Undo(); editor.Operations.Undo();
                Check(editor.Level.boxes.Contains(2) && editor.Level.boxes.Contains(3), "删除与移动的独立撤销未恢复原位置。");
                Put(editor.Operations, PlayerRules.Arrow, C(20)); Call(editor, "Select", C(20));
                Check(Get<PlayerEditTarget>(editor, "selected").valid && Get<string>(editor, "selectedMaterial") == PlayerRules.Arrow
                    && editor.Operations.Rotate(Get<PlayerEditTarget>(editor, "selected"), Get<string>(editor, "selectedMaterial"), 1)
                    && editor.Level.cells[20].rotation == 1, "主动单击选择箭头后无法旋转。");

                Put(editor.Operations, PlayerRules.PortalEntrance, C(2));
                Check(editor.Operations.MaterialAt(C(2), PlayerEditLayer.Actor) == PlayerEditing.Box && editor.Operations.MaterialAt(C(2), true) == PlayerRules.PortalEntrance, "明确选择物件/旧groundOnly调用未能访问对应层。");
                Set(editor, "selectionLayer", PlayerEditLayer.Actor); Call(editor, "Select", C(2));
                Check(Get<string>(editor, "selectedMaterial") == PlayerEditing.Box, "物件模式无法选中后放地面下面的箱子。");
                Call(editor, "DeleteSelection");
                Check(!editor.Level.boxes.Contains(2) && editor.Level.cells[2].ruleId == PlayerRules.PortalEntrance, "物件模式删除影响了地面。");
                string noActor = JsonUtility.ToJson(editor.Level); Call(editor, "DeleteSelection");
                Check(JsonUtility.ToJson(editor.Level) == noActor && Get<string>(editor, "selectedMaterial") == null && editor.Status.Contains("没有人物或箱子"), "物件模式无物件时错误删除地面或没有提示。");
                Put(editor.Operations, PlayerRules.Arrow, C(10)); Call(editor, "Select", C(10)); editor.MarkSaved();
                string beforeRotate = JsonUtility.ToJson(editor.Level);
                Check(!editor.Operations.Rotate(C(10), Get<string>(editor, "selectedMaterial"), 1) && JsonUtility.ToJson(editor.Level) == beforeRotate && !editor.Dirty, "空物件层旋转穿透到下层地面或错误标脏。");
                Put(editor.Operations, PlayerEditing.Box, C(2)); Set(editor, "selectionLayer", PlayerEditLayer.Ground); Call(editor, "Select", C(2)); Call(editor, "DeleteSelection");
                Check(editor.Level.boxes.Contains(2) && editor.Level.cells[2].ruleId == PlayerRules.Plain, "地面模式未访问下层或误删了箱子。");
            }
            results.Add("玩家编辑：放置、移动及整笔涂抹完成后清空持物和选择；失败可重试，主动再选仍可旋转/删除，整笔可撤销。");
            results.Add("玩家编辑：最近/地面/物件三种选择访问上下层，物件为空时不误删地面并明确提示。");
            using (var editor = new PlayerEditor(null, LevelData.Create(5, 5), PlayerRules.CreateBook()))
            {
                editor.DismissHelp(); var gate = PlayerEditTarget.Boundary(6, Direction.East);
                Put(editor.Operations, PlayerRules.Plate, C(1)); Put(editor.Operations, PlayerRules.Gate, gate);
                Set(editor, "mode", 1); Call(editor, "LinkClick", C(1));
                string source = Get<string>(editor, "linkSource");
                Check(!string.IsNullOrEmpty(source), "点击按钮未选择连线起点。");
                string beforeBadLink = JsonUtility.ToJson(editor.Level); Call(editor, "LinkClick", C(10));
                Check(Get<int>(editor, "mode") == 1 && Get<string>(editor, "linkSource") == source && JsonUtility.ToJson(editor.Level) == beforeBadLink,
                    "非法连线提前退出或改动了地图。");
                Call(editor, "LinkClick", gate);
                CheckPlacementEnded(editor, "点对点连线成功");
                Check(Get<int>(editor, "mode") == 0 && Get<string>(editor, "linkSource") == null && editor.Operations.Links().Count == 1,
                    "创建关联后没有退出连线模式或仍保留起点。");
                Call(editor, "Select", C(13)); Check(Get<PlayerEditTarget>(editor, "selected").cell == 13, "连线结束后不能直接选择下一格。");
                editor.Operations.Undo(); Check(editor.Operations.Links().Count == 0, "连线未独立撤销。");
                editor.Operations.Redo(); Check(editor.Operations.Links().Count == 1, "连线重做未恢复。");
                Put(editor.Operations, PlayerRules.RotationPlate, C(14)); Put(editor.Operations, PlayerRules.Arrow, C(18));
                Set(editor, "mode", 1); Call(editor, "LinkClick", C(14)); Call(editor, "LinkClick", C(18));
                CheckPlacementEnded(editor, "旋转按钮到箭头连线成功");
                Check(Get<int>(editor, "mode") == 0 && Get<string>(editor, "linkSource") == null && editor.Operations.Links().Count == 2,
                    "箭头关联没有同样退出连线或丢失之前门关联。");
            }
            results.Add("玩家编辑：按钮到门/箭头连线成功即清空选择并退出模式，失败保留起点，下一格可直接选，关联可独立撤销重做。");
            e = Fresh();
            Check(!e.SetDescription("") && !e.Dirty && !e.CanUndo, "空说明重复应用产生了撤销或脏状态。");
            string explanation = "先压住按钮，再把箱子送到目标。\n多余箱子可以用来开门。";
            Check(e.SetDescription(explanation) && e.Dirty && e.CanUndo, "应用关卡说明未作为一次编辑记录。");
            e.MarkSaved();
            Check(JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(e.Level)).description == explanation && e.Level.Clone().description == explanation,
                "说明未在JSON或地图副本中保留。");
            Check(!e.SetDescription(explanation) && !e.Dirty, "重复应用相同说明错误标记未保存。");
            Check(e.Undo() && e.Level.description == "" && e.Dirty, "撤销说明未恢复原文。");
            Check(e.Redo() && e.Level.description == explanation && !e.Dirty, "重做说明未恢复保存点。");
            bool rejected = false;
            try { e.SetDescription(new string('字', PlayerEditing.MaxDescriptionLength + 1)); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && e.Level.description == explanation && !e.Dirty, "超长说明被静默裁剪或改变了地图。");
            results.Add("玩家编辑：关卡说明应用、空操作、JSON/Clone、撤销重做及保存点一致；超长输入保留原文。");

            e = Fresh();
            Put(e, PlayerRules.Gate, PlayerEditTarget.Boundary(6, Direction.East));
            Put(e, PlayerRules.Gate, PlayerEditTarget.Boundary(6, Direction.North));
            Put(e, PlayerRules.Plate, C(0)); Check(e.Link(0, PlayerEditTarget.Boundary(6, Direction.East), out reason), reason);
            var issues = PlayerRules.Validate(e.Level);
            var gateIssue = issues.Find(issue => issue.cellIndex == 6 && issue.ruleId == PlayerRules.Gate && issue.edgeDirection == (int)Direction.North);
            Check(gateIssue != null && !issues.Exists(issue => issue.cellIndex == 6 && issue.edgeDirection == (int)Direction.East),
                "同格两门的诊断未区分有线东边与无线北边。");
            using (var editor = new PlayerEditor(null, e.Level, e.Book))
            {
                editor.DismissHelp(); editor.MarkSaved(); string snapshot = JsonUtility.ToJson(editor.Level);
                Set(editor, "held", PlayerEditing.Box); Set(editor, "selectionLayer", PlayerEditLayer.Actor);
                editor.FocusValidationIssue(gateIssue);
                var target = Get<PlayerEditTarget>(editor, "selected");
                Check(target.valid && target.edge && target.cell == 6 && target.side == Direction.North
                    && Get<string>(editor, "selectedMaterial") == PlayerRules.Gate && Get<string>(editor, "held") == null
                    && editor.Status.Contains(gateIssue.message) && !editor.Dirty && JsonUtility.ToJson(editor.Level) == snapshot,
                    "定位门错误选错了公共边，丢失原因，或修改了地图。");
                editor.FocusValidationIssue(new ValidationIssue { message = "请放置人物。" });
                Check(!Get<PlayerEditTarget>(editor, "selected").valid && editor.Status.Contains("请放置人物") && !editor.Dirty,
                    "无位置问题残留旧选中或改变保存状态。");
                Put(editor.Operations, PlayerRules.PortalExit, C(20)); Put(editor.Operations, PlayerEditing.Box, C(20)); editor.MarkSaved();
                editor.FocusValidationIssue(new ValidationIssue { severity = ValidationSeverity.Warning, cellIndex = 20, ruleId = PlayerRules.PortalExit, message = "出口朝向地图外。" });
                Check(Get<string>(editor, "selectedMaterial") == PlayerRules.PortalExit && !Get<PlayerEditTarget>(editor, "selected").edge
                    && editor.Status.Contains("提示") && !editor.Dirty, "定位地面警告被上层箱子或旧选择模式遮挡。");
                string original = editor.Level.description; Call(editor, "OpenDescription"); Set(editor, "descriptionDraft", "暂不应用的说明");
                Check(editor.Level.description == original && !editor.Dirty, "打开说明或编辑弹窗草稿提前修改了地图。");
            }
            var unnormalized = LevelData.Create(5, 5);
            unnormalized.edges.Add(new EdgeData { cell = 7, direction = Direction.West, kind = EdgeKind.Gate, ruleId = PlayerRules.Gate });
            Check(LevelValidator.Validate(unnormalized, PlayerRules.CreateBook()).Exists(issue => issue.ruleId == PlayerRules.Gate && issue.cellIndex == 6 && issue.edgeDirection == (int)Direction.East),
                "共用验证器的西边诊断未规范化为相邻格东边。");
            var wideLevel = LevelData.Create(32, 2);
            using (var editor = new PlayerEditor(null, wideLevel, PlayerRules.CreateBook()))
            {
                editor.FocusValidationIssue(new ValidationIssue { cellIndex = 31, message = "定位末列" });
                Check(editor.Status.Contains("AF1") && !editor.Dirty, "32列地图错误位置未使用可读的AF列坐标。");
            }
            results.Add("玩家编辑：检查结果定位正确格/公共边，规范化方向、警告与全局问题可读；定位和说明草稿不改地图或保存点。");
            var guide = PlayerEditor.CreateGuideLevel(); string originalGuide = JsonUtility.ToJson(guide);
            using (var editor = new PlayerEditor(null, guide, PlayerRules.CreateBook()))
            {
                editor.BeginGuide(); var operations = editor.Operations;
                Check(editor.GuideActive && editor.GuideStep == 0 && !editor.GuideComplete && !editor.Dirty
                    && JsonUtility.ToJson(editor.Level) == originalGuide && !Get<PlayerEditTarget>(editor, "selected").valid,
                    "开始引导代放内容、选择目标，或错误完成/改变保存状态。");
                int person = guide.Index(1, 2), box = guide.Index(2, 2), goal = guide.Index(3, 2);
                var door = PlayerEditTarget.Boundary(box, Direction.East);
                Put(operations, PlayerEditing.Player, C(person)); Check(editor.GuideStep == 1, "真实人物放置未推进到箱子步骤。");
                Put(operations, PlayerEditing.Box, C(box)); Check(editor.GuideStep == 2, "箱子放置未推进到目标步骤。");
                Put(operations, PlayerRules.Goal, C(goal)); Check(editor.GuideStep == 3, "目标放置未推进到压力板步骤。");
                Put(operations, PlayerRules.Plate, C(person)); Check(editor.GuideStep == 4 && editor.Level.player == person, "脚下放板误移人物或未推进到门。");
                Put(operations, PlayerRules.Gate, door); Check(editor.GuideStep == 5, "覆墙为门未推进到连线。");
                Check(operations.Link(person, door, out reason), reason);
                Check(editor.GuideStep == 6 && !editor.GuideComplete, "搭完关卡未进入自定义名称步骤，或提前冒充完成。");
                operations.Undo(); Check(editor.GuideStep == 5, "撤销连线没有回到最早未完成提示。");
                operations.Redo(); Check(editor.GuideStep == 6, "重做连线没有恢复名称步骤。");
                operations.Remove(C(goal), PlayerRules.Goal); Check(editor.GuideStep == 2, "破坏之前目标后没有回退到目标提示。");
                operations.Undo(); Check(editor.GuideStep == 6, "撤销破坏没有恢复名称步骤。");
                operations.Rename("   "); Check(editor.GuideStep == 6, "空白名称错误完成命名步骤。");
                operations.Rename("工坊入门 · 我的第一道机关 "); Check(editor.GuideStep == 6, "只加空格冒充自定义名称。");
                operations.Rename("我的第一扇门"); Check(editor.GuideStep == 7, "自定义名称未推进说明步骤。");
                Call(editor, "OpenDescription"); Set(editor, "descriptionDraft", "按 D 或方向键 →，把箱子推过门送到目标。");
                Check(editor.GuideStep == 7 && string.IsNullOrEmpty(editor.Level.description), "尚未应用的说明草稿提前推进了教程。");
                Call(editor, "ApplyDescription"); Check(editor.GuideStep == 8 && !Get<bool>(editor, "descriptionOpen"), "应用说明未推进试玩或未关闭编辑窗口。");
                operations.Undo(); Check(editor.GuideStep == 7, "撤销说明后没有回到说明步骤。");
                operations.Undo(); Check(editor.GuideStep == 6, "撤销名称后没有回到名称步骤。");
                operations.Redo(); operations.Redo(); Check(editor.GuideStep == 8, "重做名称与说明没有恢复试玩步骤。");
                Check(!LevelValidator.HasErrors(PlayerRules.Validate(editor.Level)), "完整练习图未通过真实关卡校验。");
                var playLevel = editor.Level.Clone(); var playBook = PlayerRules.CreateBook();
                var played = RuleEngine.Step(playLevel, playBook, RuleEngine.CreateState(playLevel, playBook), Direction.East);
                Check(played.changed && played.state.status == GameStatus.Won && played.state.boxes[0] == goal,
                    "实际规则下人物压板并向右推动一次不能过门通关。");
                editor.NotifyGuidePlayResult(playLevel, GameStatus.Playing); Check(!editor.GuideComplete, "未获胜就完成引导。");
                var unrelated = playLevel.Clone(); unrelated.id = Guid.NewGuid().ToString("N");
                editor.NotifyGuidePlayResult(unrelated, GameStatus.Won); Check(!editor.GuideComplete, "其他地图获胜错误完成当前引导。");
                editor.NotifyGuidePlayResult(playLevel, played.state.status);
                Check(editor.GuideComplete && editor.GuideStep == 9, "真实匹配地图通关未完成引导。");
                operations.SetDescription("这是我亲手搭好的压力板与门。"); Check(editor.GuideComplete, "修改文字不应作废已完成的试玩。");
                operations.Remove(C(box), PlayerEditing.Box); Check(editor.GuideStep == 1 && !editor.GuideComplete, "删除前置箱子后仍宣称引导完成。");
                operations.Undo(); Check(editor.GuideComplete, "撤销到已完成布局后没有恢复完成状态。");
                string beforeExit = JsonUtility.ToJson(editor.Level); editor.EndGuide();
                Check(!editor.GuideActive && editor.GuideStep == -1 && JsonUtility.ToJson(editor.Level) == beforeExit,
                    "退出引导修改或重置了练习地图。");
            }
            using (var editor = new PlayerEditor(null, LevelData.Create(), PlayerRules.CreateBook()))
            {
                string beforeGuide = JsonUtility.ToJson(editor.Level); bool blocked = false;
                try { editor.BeginGuide(); } catch (InvalidOperationException) { blocked = true; }
                Check(blocked && !editor.GuideActive && JsonUtility.ToJson(editor.Level) == beforeGuide && !editor.Dirty,
                    "引导可以在已有普通地图上启动或改写它。");
            }
            results.Add("玩家编辑：九步引导识别摆放、连线、自定义名称及已应用说明，撤销回退；专用图真实一推通关才完成，不改原图。");
            foreach (Vector2 size in new[] { new Vector2(624, 416), new Vector2(921, 616), new Vector2(2068, 666) })
            {
                var viewport = new Rect(10, 166, size.x, size.y);
                float cell = size.y * .78f / 5;
                var map = new Rect(viewport.center.x - 2.5f * cell, viewport.center.y - 2.5f * cell, 5 * cell, 5 * cell);
                var person = new Rect(viewport.center.x - 1.48f * cell - 10, viewport.center.y - .48f * cell - 18, .96f * cell + 20, .96f * cell + 28);
                var box = new Rect(person.x + cell, person.y, person.width, person.height);
                var goal = new Rect(box.x + cell, box.y, box.width, box.height);
                var gate = new Rect(viewport.center.x + .44f * cell - 10, person.y, .12f * cell + 20, person.height);
                foreach (var targets in new[] { new List<Rect> { person }, new List<Rect> { box }, new List<Rect> { goal }, new List<Rect> { gate }, new List<Rect> { person, gate } })
                {
                    Rect panel = PlayerEditor.ChooseGuidePanelRect(viewport, map, targets);
                    Check(panel.xMin >= viewport.xMin && panel.xMax <= viewport.xMax && panel.yMin >= viewport.yMin && panel.yMax <= viewport.yMax
                        && panel.width <= 360 && panel.height >= 160 && !targets.Exists(target => target.Overlaps(panel)),
                        "960/1280/2504布局的引导浮窗超出棋盘、退成长条或遮住操作目标。");
                }
                Rect widePanel = PlayerEditor.ChooseGuidePanelRect(viewport, map, new List<Rect>());
                if (size.x > 1500) Check(!widePanel.Overlaps(map) && widePanel.x < map.x, "宽屏没有利用棋盘左侧空白放置引导窗。");
            }
            using (var editor = new PlayerEditor(null, PlayerEditor.CreateGuideLevel(), PlayerRules.CreateBook()))
            {
                editor.BeginGuide(); Set(editor, "paintRect", new Rect(10, 166, 624, 416)); Set(editor, "guidePanelRect", new Rect(380, 178, 240, 168));
                var hit = typeof(PlayerEditor).GetMethod("IsBoardPointer", PrivateInstance);
                Check(!(bool)hit.Invoke(editor, new object[] { new Vector2(400, 200) }) && (bool)hit.Invoke(editor, new object[] { new Vector2(200, 200) }),
                    "浮窗范围没有阻止点击/涂抹/滚轮穿透，或把窗外棋盘也挡住了。");
                editor.EndGuide(); Check((bool)hit.Invoke(editor, new object[] { new Vector2(400, 200) }), "退出引导后残留了不可操作区域。");
            }
            results.Add("玩家编辑：引导小浮窗在960/1280/2504布局内避开当前格及连线两端，宽屏用左侧空白，点击不穿透，退出不残留遮挡。");
            using (var editor = new PlayerEditor(null, PlayerEditor.CreateGuideLevel(), PlayerRules.CreateBook()))
            {
                editor.BeginGuide(); var read = typeof(PlayerEditor).GetMethod("GuidePanelMessage", PrivateInstance);
                string original = (string)read.Invoke(editor, new object[] { 0f });
                editor.Status = "未放置：这里已有箱子。";
                Check(((string)read.Invoke(editor, new object[] { 1f })).Contains("未放置"), "引导浮窗丢失当前操作错误。");
                Set(editor, "hoverTip", "冰面让箱子额外滑一格。");
                Check(((string)read.Invoke(editor, new object[] { 2f })).Contains("冰面"), "引导没有展示素材悬停说明。");
                Set(editor, "hoverTip", null);
                Check((string)read.Invoke(editor, new object[] { 7.1f }) == original, "临时反馈结束后没有恢复当前步骤提示。");
                editor.Status = "未放置：落点无效。"; read.Invoke(editor, new object[] { 8f });
                Put(editor.Operations, PlayerEditing.Player, C(editor.Level.Index(1, 2)));
                string next = (string)read.Invoke(editor, new object[] { 8.1f });
                Check(next.Contains("箱子") && !next.Contains("未放置") && !next.Contains("人物放在"), "切换教程步骤时残留之前操作或教程文字。");
            }
            results.Add("玩家编辑：引导操作/悬停反馈只在浮窗临时显示，到时恢复指引，步骤推进清掉旧错误和旧教程内容。");
            return results;
        }
    }
}
