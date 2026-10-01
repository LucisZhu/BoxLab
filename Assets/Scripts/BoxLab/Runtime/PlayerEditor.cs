using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Player-facing sandbox authoring UI. Pixel-space IMGUI over the 3D board.</summary>
    public sealed partial class PlayerEditor : IDisposable
    {
        public Action SaveRequested, PlayRequested, ExitRequested;
        public string Status = "选中格子再点右侧素材，或直接把素材拖进地图。";
        public LevelData Level => editing.Level;
        public PlayerEditing Operations => editing;
        public bool Dirty => editing.Dirty;
        readonly PlayerEditing editing;
        readonly BoardView view;
        readonly RuleBookData book;
        readonly List<MaterialItem> materials = new List<MaterialItem>();
        readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        PlayerEditTarget selected, hover, lastPainted;
        PlayerEditTarget validationTarget;
        ValidationSeverity validationSeverity;
        bool pendingValidationFocus;
        string selectedMaterial, held, palettePressed;
        PlayerEditTarget? heldSource;
        PlayerEditLink selectedLink;
        string linkSource;
        int rotation, category, mode;
        PlayerEditLayer selectionLayer;
        Vector2 pressPosition, lastPaintPosition, paletteScroll;
        bool dragging, boardPressed, painting, needsRender = true, help, resizeOpen;
        bool descriptionOpen, focusDescription;
        string descriptionDraft = "";
        Vector2 descriptionScroll;
        Vector2 helpScroll;
        string widthText, heightText, hoverTip;
        HashSet<string> painted = new HashSet<string>();
        Rect boardRect, paintRect, toolboxRect, trashRect, paletteViewport, selectionDeleteRect, titleHoverRect;
        const float ToolbarHeight = 86;
        string fullTitleTooltip;
        Vector2 eventMouse;
        int pointerControl, capturedPointerControl;
        bool ownsPointer;
        float viewZoom = 1;
        Vector3 viewPan;
        bool panning;
        Vector2 panPointer;
        int renderedWidth, renderedHeight;
        GUIStyle text, title, small, cardText, icon, input, deleteBubble, toolbarDetail, unsavedBadge, titleTooltip, descriptionInput;
        WorkshopTheme Theme => WorkshopTheme.Shared;
        readonly Color accent = WorkshopTheme.Accent, muted = WorkshopTheme.Muted, error = WorkshopTheme.Error;
        sealed class MaterialItem { public string id, name, glyph, description; public int category; public Color color; }

        public PlayerEditor(BoardView view, LevelData level, RuleBookData book)
        {
            this.view = view; this.book = book; editing = new PlayerEditing(level, book);
            editing.Changed += OnChanged; widthText = Level.width.ToString(); heightText = Level.height.ToString();
            help = PlayerPrefs.GetInt("BoxLab.PlayerEditor.HelpSeen", 0) == 0;
            Add(PlayerRules.Plain, "普通地面", "□", "常规地面。人物和箱子都能经过。", 0);
            Add(PlayerRules.Goal, "目标", "◎", "每个目标需要一个箱子，不能与其他特殊地面叠加。", 0);
            Add(PlayerRules.Ice, "冰面", "❄", "箱子进入后沿来向多移动一格，人物正常行走。", 0);
            Add(PlayerRules.Arrow, "箭头格", "↑", "箱子进入后沿箭头移动一格。Q / E 旋转朝向。", 0);
            Add(PlayerRules.Plate, "压力按钮", "●", "持续压住才激活。连到门；门需要所有关联按钮同时受压。", 0);
            Add(PlayerRules.RotationPlate, "旋转按钮", "↻", "每次新压下，使关联箭头顺时针旋转 90°。", 0);
            Add(PlayerRules.Selective, "人物通道", "人", "只允许人物经过，箱子无法进入。", 0);
            Add(PlayerRules.BoxOnly, "箱子通道", "箱", "只允许箱子进入；推走通道上的箱子时，人物留在原位。", 0);
            Add(PlayerRules.PortalEntrance, "传送入口", "入", "箱子从门正面进入才传送。Q/E 转动门口；自动配对出口，人物正常步行。", 0);
            Add(PlayerRules.PortalExit, "传送出口", "出", "箱子传送后向门正面走出一格。出口或前方受阻则留在入口；Q/E 旋转。", 0);
            Add(PlayerRules.Hole, "空洞", "×", "人物不能进入；箱子进入后消失，剩余箱子少于目标时失败。", 0);
            Add(PlayerRules.Fragile, "坍塌地面", "▱", "物件离开后立即变为空洞。推走箱子时，人物留在原地。", 0);
            Add(PlayerEditing.Player, "人物", "人", "地图只有一个人物。再次放置会移动原来的人物。", 1);
            Add(PlayerEditing.Box, "箱子", "箱", "常规推动一格。箱子可多于目标，但不能少于目标。", 1);
            Add(PlayerEditing.Wall, "墙", "━", "直接放在公共边缝中；外圈边缝同样可放墙。", 2);
            Add(PlayerRules.Gate, "联动门", "╫", "覆写当前边墙。连接 1～3 个压力按钮，全部受压才打开。", 2);
        }
        void Add(string id, string name, string glyph, string description, int group)
        {
            Color color = new Color(.42f, .59f, .73f); var rule = book.Find(id);
            if (rule != null) ColorUtility.TryParseHtmlString(rule.colorHex, out color);
            if (id == PlayerEditing.Player) color = new Color(.14f, .8f, 1);
            if (id == PlayerEditing.Box) color = new Color(.96f, .69f, .3f);
            materials.Add(new MaterialItem { id = id, name = name, glyph = glyph, description = description, category = group, color = color });
        }
        public static string ThumbnailResourcePath(string materialId)
        { return "BoxLabArt/Thumbnails/" + (materialId ?? "").Replace("$", ""); }
        Texture2D Thumbnail(string materialId)
        {
            Texture2D result;
            if (!thumbnails.TryGetValue(materialId, out result))
            {
                result = Resources.Load<Texture2D>(ThumbnailResourcePath(materialId));
                thumbnails.Add(materialId, result); // Cache missing optional artwork as well.
            }
            return result;
        }
        public void MarkSaved() { editing.MarkSaved(); Status = "草稿已保存。"; }
        public void Dispose() { ReleasePointer(); if (view) view.HideEditGhost(); editing.EndStroke(); editing.Changed -= OnChanged; }
        public void ClearVisuals() { CancelHeld(); if (view) view.HideEditGhost(); }
        public void Resume() { needsRender = true; RefreshSelection(); }
        public void DismissHelp() { help = false; }
        void OnChanged() { needsRender = true; validationTarget = default(PlayerEditTarget); pendingValidationFocus = false; guideCurrentLayout = null; }
        public void FocusValidationIssue(ValidationIssue issue)
        {
            CancelHeld(); help = descriptionOpen = false; mode = 0; selectedLink = null;
            selectionDeleteRect = default(Rect); selected = validationTarget = default(PlayerEditTarget); selectedMaterial = null;
            pendingValidationFocus = false; viewZoom = 1; viewPan = Vector3.zero;
            string message = issue == null ? "请选择一项检查结果。" : issue.message ?? "请检查这张地图。";
            if (issue == null || !Level.Contains(issue.cellIndex) || Level.GetCell(issue.cellIndex) == null)
            { Status = "地图检查：" + message; return; }
            selectionLayer = PlayerEditLayer.Ground;
            var target = issue.edgeDirection >= 0 && issue.edgeDirection < 4
                ? PlayerEditTarget.Boundary(issue.cellIndex, (Direction)issue.edgeDirection) : PlayerEditTarget.Ground(issue.cellIndex);
            Select(target); category = target.edge ? 2 : 0;
            validationTarget = selected; validationSeverity = issue.severity; pendingValidationFocus = true;
            Status = (issue.severity == ValidationSeverity.Warning ? "提示 · " : "需要修改 · ") + Position(selected.cell)
                + (selected.edge ? " " + DirectionName((int)selected.side) + "侧边" : "") + "：" + message;
        }
        void Styles()
        {
            Theme.Ensure(GUI.skin.font); Theme.ApplyToSkin(GUI.skin);
            if (text != null) return;
            text = Theme.CreateLabel(16);
            title = new GUIStyle(text)
            {
                fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false,
                alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), fixedHeight = 0
            };
            small = Theme.CreateLabel(13, true);
            toolbarDetail = new GUIStyle(small) { wordWrap = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            unsavedBadge = new GUIStyle(toolbarDetail) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, .82f, .43f) } };
            titleTooltip = new GUIStyle(text) { fontSize = 16, alignment = TextAnchor.UpperLeft, wordWrap = true, padding = new RectOffset(12, 12, 10, 10) };
            cardText = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            icon = new GUIStyle(cardText) { fontSize = 27, fontStyle = FontStyle.Bold };
            deleteBubble = Theme.CreateButton(18, WorkshopButtonKind.Danger); deleteBubble.padding = new RectOffset(0, 0, 0, 0);
            deleteBubble.fontStyle = FontStyle.Bold;
            input = Theme.CreateInput(16); input.alignment = TextAnchor.MiddleCenter;
            descriptionInput = new GUIStyle(input) { alignment = TextAnchor.UpperLeft, wordWrap = true, padding = new RectOffset(8, 8, 8, 8) };
        }
        public void Draw(Rect boardRect, Rect toolboxRect)
        {
            Styles(); this.boardRect = boardRect; this.toolboxRect = toolboxRect; hoverTip = "";
            bool externalEnabled = GUI.enabled;
            GUI.enabled = externalEnabled && !descriptionOpen && !help;
            // Capture this queued GUI event before any scroll view changes its coordinate space.
            // Input.mousePosition may already describe a later physical mouse position.
            eventMouse = Event.current.mousePosition;
            pointerControl = GUIUtility.GetControlID(714013, FocusType.Passive);
            float paintTop = ToolbarHeight + 18;
            paintRect = new Rect(boardRect.x, boardRect.y + paintTop, boardRect.width, Mathf.Max(60, boardRect.height - paintTop - (GuideActive ? 0 : 56)));
            if (view) view.SetViewport(new Rect(paintRect.x / Screen.width, 1 - paintRect.yMax / Screen.height, paintRect.width / Screen.width, paintRect.height / Screen.height));
            if (needsRender && view)
            {
                if (renderedWidth != Level.width || renderedHeight != Level.height) { viewZoom = 1; viewPan = Vector3.zero; renderedWidth = Level.width; renderedHeight = Level.height; }
                view.Render(Level, book, RuleEngine.CreateState(Level)); view.FitCamera(); needsRender = false;
            }
            if (pendingValidationFocus && view && validationTarget.valid)
            {
                viewZoom = Mathf.Clamp(Mathf.Max(Level.width, Level.height) / 10f, 1, 3);
                viewPan = viewZoom > 1.01f ? view.transform.TransformVector(new Vector3(
                    validationTarget.cell % Level.width - (Level.width - 1) * .5f, 0,
                    validationTarget.cell / Level.width - (Level.height - 1) * .5f)) : Vector3.zero;
                pendingValidationFocus = false;
            }
            if (view && view.boardCamera)
            { view.boardCamera.orthographicSize /= viewZoom; view.boardCamera.transform.position += viewPan; }
            UpdateGuidePanelBounds();
            hover = Pick(Event.current.mousePosition);
            DrawToolbar(); DrawToolbox(); DrawConnections(); DrawSelection(); DrawGuideTargets(); DrawGhost(); DrawTitleTooltip();
            if (!help && !descriptionOpen && GUI.enabled) ProcessInput();
            if (!GuideActive)
            {
                var statusRect = new Rect(boardRect.x + 12, boardRect.yMax - 49, boardRect.width - 24, 40);
                Theme.DrawPanel(statusRect, WorkshopPanelKind.Inset); GUI.Label(Inset(statusRect, 5), string.IsNullOrEmpty(hoverTip) ? Status : hoverTip, small);
            }
            if (GuideActive) DrawGuidePanel();
            GUI.enabled = externalEnabled;
            if (help) DrawHelp();
            if (descriptionOpen) DrawDescription();
        }
        void DrawToolbar()
        {
            var r = new Rect(boardRect.x + 12, boardRect.y + 10, boardRect.width - 24, ToolbarHeight); Theme.DrawPanel(r);
            // A dedicated title row prevents the action buttons from squeezing a normal name
            // into a wrapped 27-pixel label. Reserve the dirty badge independently of the name.
            var nameRect = new Rect(r.x + 13, r.y + 5, Mathf.Max(60, r.width - 26 - (Dirty ? 82 : 0)), 36);
            string fullName = string.IsNullOrWhiteSpace(Level.name) ? "未命名关卡" : Level.name.Replace('\r', ' ').Replace('\n', ' ');
            string visibleName = EllipsizeTitle(fullName, nameRect.width);
            GUI.Label(nameRect, visibleName, title);
            titleHoverRect = nameRect;
            fullTitleTooltip = visibleName == fullName ? null : fullName + (Dirty ? "\n未保存的更改" : "");
            if (Dirty)
            {
                var badge = new Rect(r.xMax - 83, r.y + 10, 70, 26);
                Theme.DrawPanel(badge, WorkshopPanelKind.Inset); GUI.Label(badge, "未保存", unsavedBadge);
            }
            float x = r.xMax - 399;
            float actionY = r.y + 46;
            if (Btn(new Rect(x, actionY, 72, 32), "保存")) { FinishGesture(); SaveRequested?.Invoke(); }
            if (Btn(new Rect(x + 78, actionY, 90, 32), "▶ 试玩")) { FinishGesture(); PlayRequested?.Invoke(); }
            if (Btn(new Rect(x + 174, actionY, 64, 32), "撤销", editing.CanUndo)) { CancelHeld(); editing.Undo(); RefreshSelection(); Status = "已撤销整次操作。"; }
            if (Btn(new Rect(x + 244, actionY, 64, 32), "重做", editing.CanRedo)) { CancelHeld(); editing.Redo(); RefreshSelection(); Status = "已重做。"; }
            if (Btn(new Rect(x + 314, actionY, 76, 32), "返回")) { FinishGesture(); ExitRequested?.Invoke(); }
            var details = new Rect(r.x + 13, actionY, Mathf.Max(0, x - r.x - 25), 32);
            string dimensions = Level.width + " × " + Level.height;
            string controls = dimensions + "  ·  Q/E 旋转  ·  Delete 删除";
            if (toolbarDetail.CalcSize(new GUIContent(controls)).x > details.width) controls = dimensions + "  ·  Q/E 旋转";
            if (toolbarDetail.CalcSize(new GUIContent(controls)).x > details.width) controls = dimensions;
            GUI.Label(details, controls, toolbarDetail);
            if (details.Contains(eventMouse)) hoverTip = dimensions + " · Q/E 旋转 · Delete 删除 · Ctrl+Z 撤销 · Ctrl+Y 重做";
        }
        string EllipsizeTitle(string value, float width)
        {
            if (title.CalcSize(new GUIContent(value)).x <= width) return value;
            int end = value.Length;
            while (end > 0)
            {
                end--;
                if (end > 0 && char.IsHighSurrogate(value[end - 1])) end--;
                string candidate = value.Substring(0, end).TrimEnd() + "…";
                if (title.CalcSize(new GUIContent(candidate)).x <= width) return candidate;
            }
            return "…";
        }
        void DrawTitleTooltip()
        {
            if (string.IsNullOrEmpty(fullTitleTooltip) || !titleHoverRect.Contains(eventMouse) || !GUI.enabled || help) return;
            float width = Mathf.Min(620, Screen.width - 24);
            float height = titleTooltip.CalcHeight(new GUIContent(fullTitleTooltip), width);
            var r = new Rect(Mathf.Clamp(titleHoverRect.x, 12, Screen.width - width - 12),
                Mathf.Clamp(boardRect.y + ToolbarHeight + 22, 12, Mathf.Max(12, Screen.height - height - 12)), width, height);
            Theme.DrawPanel(r, WorkshopPanelKind.Inset); Outline(r, accent, 1);
            GUI.Label(r, fullTitleTooltip, titleTooltip); // Informational only; never captures a click.
        }
        void DrawToolbox()
        {
            Theme.DrawPanel(toolboxRect);
            float x = toolboxRect.x + 14, w = toolboxRect.width - 28, y = toolboxRect.y + 14;
            GUI.Label(new Rect(x, y, w - 76, 30), "关卡工坊", title);
            var descriptionRect = new Rect(x + w - 68, y, 68, 29);
            if (Btn(descriptionRect, "说明")) OpenDescription();
            if (GuideActive && GuideStep == 7) Outline(Inset(descriptionRect, 1), accent, 2);
            y += 39;
            GUI.Label(new Rect(x, y + 3, 38, 28), "名称", small);
            GUI.SetNextControlName("BoxLab.MapName");
            var nameRect = new Rect(x + 42, y, w - 42, 29);
            string newName = GUI.TextField(nameRect, Level.name ?? "", 80, input);
            if (newName != Level.name && GUI.enabled) editing.Rename(newName);
            if (GuideActive && GuideStep == 6) Outline(Inset(nameRect, 1), accent, 2);
            y += 38;
            float third = (w - 12) / 3;
            if (ModeButton(new Rect(x, y, third, 32), "选择", 0)) { CancelHeld(); mode = 0; }
            if (ModeButton(new Rect(x + third + 6, y, third, 32), "连线", 1)) { CancelHeld(); mode = 1; Status = "先点按钮，再点门或箭头。点击连线可单独删除。"; }
            if (ModeButton(new Rect(x + 2 * (third + 6), y, third, 32), "擦除", 2)) { CancelHeld(); mode = 2; }
            y += 40;
            var selectedRect = new Rect(x, y, w, 71); Theme.DrawPanel(selectedRect, WorkshopPanelKind.Inset);
            string label = selectedLink != null ? "已选择一条连线" : selected.valid ? (selected.edge ? "边界 " : "格子 ") + Position(selected.cell) + " · " + SelectionName() : !string.IsNullOrEmpty(held) ? "拿起：" + Name(held) : "先选格子，或拿起右侧素材";
            GUI.Label(new Rect(x + 8, y + 7, w - 55, 25), label, text);
            if (Btn(new Rect(x + w - 39, y + 6, 31, 27), "×", selectedLink != null || selected.valid && !string.IsNullOrEmpty(selectedMaterial) && selectedMaterial != PlayerRules.Plain)) DeleteSelection();
            if (selected.valid && !selected.edge && selectedLink == null)
            {
                string[] layerNames = { "最近放置", "地面", "物件" };
                float layerWidth = (w - 24) / 3;
                for (int i = 0; i < layerNames.Length; i++)
                {
                    var layerRect = new Rect(x + 8 + i * (layerWidth + 4), y + 39, layerWidth, 24);
                    if (Theme.Button(layerRect, layerNames[i], true, (int)selectionLayer == i ? WorkshopButtonKind.Primary : WorkshopButtonKind.Secondary, 13))
                    { selectionLayer = (PlayerEditLayer)i; Select(selected); }
                }
            }
            else GUI.Label(new Rect(x + 8, y + 38, w - 16, 24), "点击线删关联 · 拖到下方垃圾桶删除", small);
            y += 80;
            string[] tabs = { "地面", "物件", "墙 / 门" };
            for (int i = 0; i < 3; i++)
            {
                var tabRect = new Rect(x + i * (third + 6), y, third, 30);
                if (Theme.Button(tabRect, tabs[i], true, category == i ? WorkshopButtonKind.Primary : WorkshopButtonKind.Secondary, 15)) category = i;
                if (GuideActive && GuideCategory >= 0)
                {
                    if (GuideCategory == i && category != i) Outline(Inset(tabRect, 2), accent, 2);
                }
            }
            y += 39;
            // Reserve precisely the controls below; a short window scrolls the palette
            // instead of forcing a 110px viewport through the size form or trash target.
            float controlsY = toolboxRect.yMax - (resizeOpen ? 145 : 108);
            float bottom = controlsY - 9;
            var scrollRect = new Rect(x - 3, y, w + 6, Mathf.Max(36, bottom - y));
            paletteViewport = scrollRect;
            var items = materials.Where(m => m.category == category).ToList();
            int columns = toolboxRect.width >= 300 ? 3 : 2; float cw = (w - (columns - 1) * 6 - 10) / columns;
            const float cardHeight = 93, cardPitch = 100;
            float contentHeight = Mathf.Ceil(items.Count / (float)columns) * cardPitch;
            paletteScroll = GUI.BeginScrollView(scrollRect, paletteScroll, new Rect(0, 0, w - 12, contentHeight));
            for (int i = 0; i < items.Count; i++)
            {
                var rect = new Rect(i % columns * (cw + 6), i / columns * cardPitch, cw, cardHeight);
                DrawMaterial(rect, items[i]);
            }
            GUI.EndScrollView();
            if (Btn(new Rect(x, controlsY, (w - 6) * .5f, 28), resizeOpen ? "收起尺寸" : "尺寸 " + Level.width + " × " + Level.height)) resizeOpen = !resizeOpen;
            if (Btn(new Rect(x + (w + 6) * .5f, controlsY, (w - 6) * .5f, 28), "适应地图")) { viewZoom = 1; viewPan = Vector3.zero; Status = "已完整显示地图。滚轮缩放，中键拖动平移。"; }
            if (resizeOpen)
            {
                GUI.Label(new Rect(x, controlsY + 35, 24, 28), "宽", text); GUI.SetNextControlName("BoxLab.MapWidth"); widthText = GUI.TextField(new Rect(x + 27, controlsY + 33, 45, 29), widthText, 2, input);
                GUI.Label(new Rect(x + 82, controlsY + 35, 24, 28), "高", text); GUI.SetNextControlName("BoxLab.MapHeight"); heightText = GUI.TextField(new Rect(x + 109, controlsY + 33, 45, 29), heightText, 2, input);
                if (Btn(new Rect(x + 163, controlsY + 33, w - 163, 29), "应用"))
                {
                    int width, height;
                    if (int.TryParse(widthText, out width) && int.TryParse(heightText, out height) && width >= 2 && width <= 32 && height >= 2 && height <= 32)
                    { CancelHeld(); editing.Resize(width, height); selected = default(PlayerEditTarget); selectedLink = null; Status = "尺寸已调整。裁掉内容可撤销，传送门不全仍可保存草稿。"; }
                    else Status = "宽高请输入 2～32 的整数。";
                }
            }
            trashRect = new Rect(x, toolboxRect.yMax - 66, w, 50);
            bool overTrash = trashRect.Contains(Event.current.mousePosition) && (!string.IsNullOrEmpty(held) || selected.valid);
            Theme.DrawPanel(trashRect, WorkshopPanelKind.Inset);
            Outline(Inset(trashRect, 2), overTrash ? error : new Color(.42f, .40f, .33f), overTrash ? 2 : 1);
            GUI.Label(trashRect, "⌫  拖到这里删除", cardText);
        }
        void DrawMaterial(Rect rect, MaterialItem item)
        {
            string reason = "";
            bool enabled = !selected.valid || editing.CanPlace(item.id, selected, out reason);
            if (held == item.id) Theme.DrawButtonSurface(rect, WorkshopButtonKind.Primary, enabled);
            else Theme.DrawPanel(rect, enabled ? WorkshopPanelKind.Card : WorkshopPanelKind.Inset);
            Outline(Inset(rect, 2), new Color(.36f, .36f, .31f), 1);
            var old = GUI.color;
            var thumbnail = Thumbnail(item.id);
            var imageRect = new Rect(rect.x + 7, rect.y + 5, rect.width - 14, 58);
            if (thumbnail)
            {
                GUI.color = enabled ? Color.white : new Color(1, 1, 1, .45f);
                GUI.DrawTexture(imageRect, thumbnail, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.color = enabled ? item.color : new Color(.35f, .4f, .45f);
                GUI.Label(imageRect, item.glyph, icon);
            }
            GUI.color = old;
            GUI.Label(new Rect(rect.x + 2, rect.y + 65, rect.width - 4, 24), item.name, enabled ? cardText : new GUIStyle(cardText) { normal = { textColor = muted * .7f } });
            if (GuideActive && item.id == GuideMaterial) Outline(Inset(rect, 4), accent, 2);
            if (rect.Contains(Event.current.mousePosition) && paletteViewport.Contains(eventMouse))
            {
                hoverTip = enabled ? item.description : "不可放置：" + reason;
                if (Event.current.rawType == EventType.MouseDown && Event.current.button == 0 && enabled && GUI.enabled && !help)
                {
                    // Pointer is local inside GUI.BeginScrollView; retain screen GUI position for dragging.
                    palettePressed = item.id; GUI.FocusControl(null);
                    pressPosition = eventMouse;
                    dragging = false; boardPressed = false; mode = 0; CapturePointer(); Event.current.Use();
                }
            }
        }
        void ProcessInput()
        {
            var ev = Event.current; Vector2 mouse = eventMouse; EventType type = ev.rawType;
            // The floating delete button owns these events; never select/drag the board underneath it.
            if (selectionDeleteRect.width > 0 && selectionDeleteRect.Contains(mouse) && type == EventType.MouseDown) return;
            if (type == EventType.MouseUp && ev.button == 0) ReleasePointer();
            if (type == EventType.ScrollWheel && IsBoardPointer(mouse) && view && view.boardCamera)
            {
                Vector2 before, after; bool beforeHit = view.TryGetGridPoint(new Vector2(mouse.x, Screen.height - mouse.y), out before);
                float previous = viewZoom; viewZoom = Mathf.Clamp(viewZoom * Mathf.Exp(-ev.delta.y * .085f), 1, 3);
                view.boardCamera.orthographicSize *= previous / viewZoom;
                if (beforeHit && view.TryGetGridPoint(new Vector2(mouse.x, Screen.height - mouse.y), out after)) viewPan += view.transform.TransformVector(new Vector3(before.x - after.x, 0, before.y - after.y));
                if (viewZoom <= 1.001f) viewPan = Vector3.zero;
                Status = "视图 " + viewZoom.ToString("F1") + " 倍 · 中键拖动平移 · 适应地图恢复全图。";
                ev.Use(); return;
            }
            if (type == EventType.MouseDown && ev.button == 2 && IsBoardPointer(mouse)) { panning = true; panPointer = mouse; CapturePointer(); ev.Use(); return; }
            if (type == EventType.MouseDrag && ev.button == 2 && panning && view && view.boardCamera)
            {
                var camera = view.boardCamera; Vector2 delta = mouse - panPointer; panPointer = mouse;
                float units = camera.orthographicSize * 2 / Mathf.Max(1, camera.pixelHeight);
                Vector3 right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
                Vector3 up = Vector3.ProjectOnPlane(camera.transform.up, Vector3.up).normalized;
                float verticalScale = Mathf.Max(.3f, Mathf.Abs(Vector3.Dot(camera.transform.up, up)));
                viewPan += right * (-delta.x * units) + up * (delta.y * units / verticalScale);
                viewPan = Vector3.ClampMagnitude(viewPan, Mathf.Max(Level.width, Level.height));
                ev.Use(); return;
            }
            if (type == EventType.MouseUp && ev.button == 2 && panning) { panning = false; ReleasePointer(); ev.Use(); return; }
            if (type == EventType.KeyDown)
            {
                if (ev.type == EventType.Used) return;
                string focused = GUI.GetNameOfFocusedControl();
                if (focused == "BoxLab.MapName" || focused == "BoxLab.MapWidth" || focused == "BoxLab.MapHeight") return;
                if (ev.keyCode == KeyCode.Escape) { CancelHeld(); selectedLink = null; Status = "已取消拿取或连线。"; ev.Use(); return; }
                if ((ev.control || ev.command) && ev.keyCode == KeyCode.Z) { CancelHeld(); if (ev.shift) editing.Redo(); else editing.Undo(); RefreshSelection(); ev.Use(); return; }
                if ((ev.control || ev.command) && ev.keyCode == KeyCode.Y) { CancelHeld(); editing.Redo(); RefreshSelection(); ev.Use(); return; }
                if (ev.keyCode == KeyCode.Delete || ev.keyCode == KeyCode.Backspace) { DeleteSelection(); ev.Use(); return; }
                if (ev.keyCode == KeyCode.Q || ev.keyCode == KeyCode.E)
                {
                    int amount = ev.keyCode == KeyCode.E ? 1 : -1;
                    if ((!string.IsNullOrEmpty(held) && editing.IsEdgeMaterial(held)) || (string.IsNullOrEmpty(held) && selected.valid && selected.edge))
                        Status = "墙与门自动贴合所选边，换边即可转向。";
                    else if (!string.IsNullOrEmpty(held)) { rotation = (rotation + amount + 4) % 4; Status = "拿取方向：" + DirectionName(rotation); }
                    else if (selected.valid && !string.IsNullOrEmpty(selectedMaterial) && editing.Rotate(selected, selectedMaterial, amount)) Status = "朝向已旋转 90°。";
                    ev.Use(); return;
                }
            }
            if (type == EventType.MouseDown && ev.button == 1 && IsBoardPointer(mouse)) { CancelHeld(); Select(hover); ev.Use(); return; }
            if (type == EventType.MouseDrag && !string.IsNullOrEmpty(palettePressed) && Vector2.Distance(mouse, pressPosition) > 6)
            { held = palettePressed; heldSource = null; rotation = 0; dragging = true; selected = default(PlayerEditTarget); selectedLink = null; }
            if (type == EventType.MouseUp && ev.button == 0 && !string.IsNullOrEmpty(palettePressed))
            {
                string material = palettePressed; palettePressed = null;
                if (!dragging && Vector2.Distance(mouse, pressPosition) > 7)
                { held = material; heldSource = null; rotation = 0; dragging = true; selected = default(PlayerEditTarget); selectedLink = null; }
                if (dragging)
                {
                    if (trashRect.Contains(mouse)) CancelHeld();
                    else if (IsBoardPointer(mouse)) TryPlace(hover, true);
                    else { dragging = false; Status = "素材已拿起，点击地图放置，Esc 取消。"; }
                }
                else if (selected.valid)
                {
                    string why;
                    if (editing.Place(material, selected, rotation, out why))
                    { CompletePlacement(); Status = "已放置 " + Name(material) + "。本次操作完成，可直接选择其他位置。"; }
                    else Status = why;
                }
                else { held = material; heldSource = null; rotation = 0; Status = "已拿起 " + Name(material) + "，点击放置，拖动可连续涂抹。"; }
                ev.Use(); return;
            }
            if (type == EventType.MouseDown && ev.button == 0 && IsBoardPointer(mouse))
            {
                GUI.FocusControl(null);
                if (mode == 1) { LinkClick(hover); ev.Use(); return; }
                if (mode == 2)
                {
                    editing.BeginStroke(); painting = true; painted.Clear(); lastPainted = default(PlayerEditTarget); CapturePointer(); PaintErase(hover); lastPaintPosition = mouse; ev.Use(); return;
                }
                if (!string.IsNullOrEmpty(held))
                {
                    if (heldSource.HasValue) TryPlace(hover, true);
                    else { editing.BeginStroke(); painting = true; painted.Clear(); lastPainted = default(PlayerEditTarget); CapturePointer(); PaintOne(hover); lastPaintPosition = mouse; }
                    ev.Use(); return;
                }
                var hitLink = HitLink(mouse);
                if (hitLink != null) { selectedLink = hitLink; selected = default(PlayerEditTarget); Status = "已选中连线，点击右侧 × 或按 Delete 删除。"; }
                else { Select(hover); boardPressed = selected.valid && !string.IsNullOrEmpty(selectedMaterial); pressPosition = mouse; if (boardPressed) CapturePointer(); }
                ev.Use(); return;
            }
            if (type == EventType.MouseDrag && ev.button == 0)
            {
                if (painting) { PaintBetween(lastPaintPosition, mouse); lastPaintPosition = mouse; ev.Use(); return; }
                if (boardPressed && Vector2.Distance(mouse, pressPosition) > 7)
                {
                    held = selectedMaterial; heldSource = selected; rotation = selected.edge || editing.IsActor(held) ? 0 : Level.cells[selected.cell].rotation;
                    dragging = true; boardPressed = false; Status = "原件保留在原位；绿框可放，红框显示原因。"; ev.Use(); return;
                }
            }
            if (type == EventType.MouseUp && ev.button == 0)
            {
                if (boardPressed && !dragging && Vector2.Distance(mouse, pressPosition) > 7 && !string.IsNullOrEmpty(selectedMaterial))
                { held = selectedMaterial; heldSource = selected; rotation = selected.edge || editing.IsActor(held) ? 0 : Level.cells[selected.cell].rotation; dragging = true; }
                boardPressed = false;
                if (painting) { CompletePainting(); ev.Use(); return; }
                if (dragging)
                {
                    if (trashRect.Contains(mouse))
                    {
                        if (heldSource.HasValue) editing.Remove(heldSource.Value, held);
                        CancelHeld(); selected = default(PlayerEditTarget); Status = "已删除，可撤销。";
                    }
                    else if (IsBoardPointer(mouse)) TryPlace(hover, true);
                    else { dragging = false; Status = "尚未放下；原件仍在。点击合法位置放置，Esc 取消。"; }
                    ev.Use();
                }
            }
        }
        void PaintBetween(Vector2 from, Vector2 to)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 5));
            for (int i = 1; i <= samples; i++)
            { var p = Vector2.Lerp(from, to, i / (float)samples); if (!IsBoardPointer(p)) continue; var target = Pick(p); if (mode == 2) PaintErase(target); else PaintOne(target); }
        }
        void PaintOne(PlayerEditTarget target)
        {
            if (!target.valid || string.IsNullOrEmpty(held) || !painted.Add(target.Key)) return;
            string reason;
            if (editing.Place(held, target, rotation, out reason)) { lastPainted = editing.Normalize(target); Status = "已放置 " + Name(held) + "。整笔涂抹可一次撤销。"; }
            else Status = reason;
        }
        void CompletePainting()
        {
            editing.EndStroke(); painting = false;
            if (mode == 0 && lastPainted.valid)
            {
                CompletePlacement();
                Status = "已完成放置，整笔可撤销。可直接选择其他位置。";
            }
            lastPainted = default(PlayerEditTarget);
        }
        void PaintErase(PlayerEditTarget target)
        {
            if (!target.valid || !painted.Add(target.Key)) return;
            string material = editing.MaterialAt(target, selectionLayer);
            bool changed = editing.Remove(target, material);
            Status = changed ? "已擦除，整笔操作可撤销。" : selectionLayer == PlayerEditLayer.Actor && !target.edge ? "这里没有人物或箱子，地面保持不变。" : "这里没有可擦除的内容。";
        }
        void TryPlace(PlayerEditTarget target, bool finish)
        {
            if (string.IsNullOrEmpty(held)) return; string reason;
            if (editing.Place(held, target, rotation, out reason, heldSource))
            {
                string placed = held; bool moving = heldSource.HasValue; dragging = false; heldSource = null;
                if (moving || finish) CompletePlacement();
                Status = "已放置 " + Name(placed) + "。本次操作完成，可直接选择其他位置。";
            }
            else { dragging = false; Status = "未放置：" + reason + " Esc 可取消，原件保持不变。"; }
        }
        void CompletePlacement()
        {
            // A committed placement ends the entire gesture, including its old cell selection.
            // Explicitly clicking a cell still selects it for rotation or deletion.
            CancelHeld();
            selected = default(PlayerEditTarget);
            selectedMaterial = null;
            selectedLink = null;
            selectionDeleteRect = default(Rect);
        }
        void Select(PlayerEditTarget target)
        {
            validationTarget = default(PlayerEditTarget); pendingValidationFocus = false;
            selected = editing.Normalize(target); selectedLink = null;
            selectedMaterial = selected.valid ? editing.MaterialAt(selected, selectionLayer) : null;
            if (selected.valid) Status = !selected.edge && selectionLayer == PlayerEditLayer.Actor && selectedMaterial == null
                ? "这里没有人物或箱子。可切换到「最近放置」或「地面」，也可放入物件。"
                : "已选择 " + Position(selected.cell) + (selected.edge ? " 的公共边" : " · " + Name(selectedMaterial)) + "，右侧可直接放置素材。";
        }
        void RefreshSelection()
        {
            selectedLink = null; widthText = Level.width.ToString(); heightText = Level.height.ToString();
            if (!Level.Contains(selected.cell)) selected = default(PlayerEditTarget);
            if (selected.valid) selectedMaterial = editing.MaterialAt(selected, selectionLayer);
        }
        void CapturePointer() { capturedPointerControl = pointerControl; GUIUtility.hotControl = capturedPointerControl; ownsPointer = true; }
        void ReleasePointer() { if (ownsPointer && GUIUtility.hotControl == capturedPointerControl) GUIUtility.hotControl = 0; ownsPointer = false; capturedPointerControl = 0; }
        void FinishGesture() { ReleasePointer(); editing.EndStroke(); painting = false; panning = false; boardPressed = false; palettePressed = null; }
        void CancelHeld() { FinishGesture(); held = null; heldSource = null; dragging = false; linkSource = null; if (view) view.HideEditGhost(); }
        void DeleteSelection()
        {
            if (selectedLink != null) { editing.RemoveLink(selectedLink); selectedLink = null; Status = "已删除这条关联，其他连线保留。"; }
            else if (selected.valid)
            {
                string removed = selectedMaterial;
                bool changed = editing.Remove(selected, removed); selectedMaterial = editing.MaterialAt(selected, selectionLayer);
                Status = changed ? "已删除 " + Name(removed) + "，可撤销。默认先选中最近放置的一层。"
                    : selectionLayer == PlayerEditLayer.Actor && !selected.edge ? "这里没有人物或箱子，地面保持不变。" : "这里没有可删除的内容。";
            }
        }
        void LinkClick(PlayerEditTarget target)
        {
            if (!target.valid) return;
            if (!target.edge)
            {
                var cell = Level.cells[target.cell];
                if (cell.ruleId == PlayerRules.Plate || cell.ruleId == PlayerRules.RotationPlate)
                { linkSource = linkSource == cell.id ? null : cell.id; selected = target; selectedMaterial = cell.ruleId; selectedLink = null; Status = linkSource == null ? "已取消连线起点。" : "已选择按钮，请点目标门或箭头；一个目标最多关联 3 个按钮。"; return; }
            }
            if (string.IsNullOrEmpty(linkSource)) { var line = HitLink(Event.current.mousePosition); if (line != null) { selectedLink = line; selected = default(PlayerEditTarget); } else Status = "先选择普通按钮或旋转按钮。"; return; }
            int source = Level.FindCellIndex(linkSource); string reason;
            if (editing.Link(source, target, out reason))
            { CompletePlacement(); mode = 0; Status = "关联已创建，已退出连线。可直接选择下一处；再次点「连线」可添加关联。"; }
            else Status = reason;
        }
        bool IsBoardPointer(Vector2 gui) => paintRect.Contains(gui) && (!GuideActive || !guidePanelRect.Contains(gui));
        PlayerEditTarget Pick(Vector2 gui)
        {
            if (!view || !IsBoardPointer(gui)) return default(PlayerEditTarget);
            int cell; Direction side; bool edge;
            if (!view.PickGround(new Vector2(gui.x, Screen.height - gui.y), out cell, out side, out edge)) return default(PlayerEditTarget);
            return editing.Normalize(edge ? PlayerEditTarget.Boundary(cell, side) : PlayerEditTarget.Ground(cell));
        }
        Vector2 Project(Vector3 world)
        {
            var camera = view ? view.boardCamera : null; if (!camera) return Vector2.zero;
            Vector3 p = camera.WorldToScreenPoint(world); return new Vector2(p.x, Screen.height - p.y);
        }
        Vector3 Center(PlayerEditTarget target)
        {
            var center = view.GetCellWorld(target.cell) + Vector3.up * .035f;
            if (target.edge) center += Delta(target.side) * .5f;
            return center;
        }
        static Vector3 Delta(Direction d) => d == Direction.North ? Vector3.forward : d == Direction.East ? Vector3.right : d == Direction.South ? Vector3.back : Vector3.left;
        void DrawTarget(PlayerEditTarget target, Color color, float width = 3)
        {
            if (!target.valid || !view) return;
            Vector3 c = Center(target);
            if (target.edge)
            {
                Vector3 axis = target.side == Direction.North || target.side == Direction.South ? Vector3.right : Vector3.forward;
                Line(Project(c - axis * .44f), Project(c + axis * .44f), color, width + 2); return;
            }
            Vector3 a = c + new Vector3(-.43f, 0, -.43f), b = c + new Vector3(.43f, 0, -.43f), d = c + new Vector3(-.43f, 0, .43f), e = c + new Vector3(.43f, 0, .43f);
            Line(Project(a), Project(b), color, width); Line(Project(b), Project(e), color, width); Line(Project(e), Project(d), color, width); Line(Project(d), Project(a), color, width);
        }
        void DrawSelection()
        {
            selectionDeleteRect = default(Rect);
            if (hover.valid && string.IsNullOrEmpty(held)) TintSelection(hover, new Color(.32f, .82f, 1f, .10f));
            if (selected.valid)
            {
                bool issue = validationTarget.valid && selected.Key == validationTarget.Key;
                if (issue) DrawTarget(selected, validationSeverity == ValidationSeverity.Warning ? accent : error, 5);
                else TintSelection(selected, new Color(.25f, .77f, 1f, .24f));
            }
            if (!view || !selected.valid || selectedLink != null || !string.IsNullOrEmpty(held) || string.IsNullOrEmpty(selectedMaterial) || selectedMaterial == PlayerRules.Plain) return;
            Vector3 center = Center(selected);
            float maxX = float.NegativeInfinity, minY = float.PositiveInfinity;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 corner = Project(center + new Vector3(x * .43f, .12f, y * .43f));
                    maxX = Mathf.Max(maxX, corner.x); minY = Mathf.Min(minY, corner.y);
                }
            if (maxX < paintRect.x || minY > paintRect.yMax || maxX > paintRect.xMax + 50 || minY < paintRect.y - 50) return;
            selectionDeleteRect = new Rect(Mathf.Clamp(maxX - 5, paintRect.x + 2, paintRect.xMax - 30), Mathf.Clamp(minY - 17, paintRect.y + 2, paintRect.yMax - 30), 28, 28);
            if (GUI.Button(selectionDeleteRect, new GUIContent("×", "删除选中物件，可撤销"), deleteBubble)) { GUI.FocusControl(null); DeleteSelection(); }
            if (selectionDeleteRect.Contains(eventMouse)) hoverTip = "删除选中的 " + Name(selectedMaterial) + "，可撤销。";
        }
        void TintSelection(PlayerEditTarget target, Color color)
        {
            if (!target.valid || !view || Event.current.type != EventType.Repaint) return;
            Vector3 center = Center(target);
            if (target.edge)
            {
                Vector3 axis = target.side == Direction.North || target.side == Direction.South ? Vector3.right : Vector3.forward;
                color.a *= 2;
                Line(Project(center - axis * .44f), Project(center + axis * .44f), color, 8);
                return;
            }
            // The fixed board camera has no yaw/roll: cell interiors project to aligned rectangles.
            Vector2 a = Project(center + new Vector3(-.43f, 0, -.43f));
            Vector2 b = Project(center + new Vector3(.43f, 0, .43f));
            float left = Mathf.Max(paintRect.xMin, Mathf.Min(a.x, b.x));
            float top = Mathf.Max(paintRect.yMin, Mathf.Min(a.y, b.y));
            float right = Mathf.Min(paintRect.xMax, Mathf.Max(a.x, b.x));
            float bottom = Mathf.Min(paintRect.yMax, Mathf.Max(a.y, b.y));
            if (right > left && bottom > top) Fill(Rect.MinMaxRect(left, top, right, bottom), color);
        }
        void DrawGhost()
        {
            if (string.IsNullOrEmpty(held) || !IsBoardPointer(Event.current.mousePosition) || !GUI.enabled)
            { if (view) view.HideEditGhost(); return; }
            string reason; bool valid = editing.CanPlace(held, hover, out reason, heldSource);
            if (view && hover.valid) view.ShowEditGhost(held, hover.cell, hover.side, hover.edge, rotation, valid);
            else if (view) view.HideEditGhost();
            Color color = valid ? WorkshopTheme.Success : error; DrawTarget(hover, color, 4);
            Vector2 at = hover.valid ? Project(Center(hover)) : Event.current.mousePosition;
            var bubble = new Rect(at.x - 48, at.y - 53, 96, 28); Theme.DrawPanel(bubble, WorkshopPanelKind.Inset); Outline(bubble, color, 2);
            string arrow = held == PlayerRules.Arrow || held == PlayerRules.PortalEntrance || held == PlayerRules.PortalExit
                ? new[] { "↑", "→", "↓", "←" }[rotation] : "";
            GUI.Label(bubble, (valid ? "✓ " : "× ") + Name(held) + arrow, cardText);
            hoverTip = valid ? "可以放置 · Q/E 旋转 · Esc 取消" : reason;
        }
        void DrawConnections()
        {
            if (!view) return;
            DrawPortalPair();
            foreach (var link in editing.Links())
            {
                bool highlight = selectedLink != null && selectedLink.Key == link.Key || selected.valid && (selected.cell == link.sourceCell || selected.Key == link.target.Key) || hover.valid && (hover.cell == link.sourceCell || hover.Key == link.target.Key);
                Color c = BoardView.SignalColor(Level.cells[link.sourceCell].signalStyle); c.a = highlight ? .95f : .27f;
                Dashed(Project(view.GetCellWorld(link.sourceCell) + Vector3.up * .12f), Project(Center(link.target)), c, highlight ? 3 : 1.5f);
            }
            if (mode == 1 && !string.IsNullOrEmpty(linkSource))
            {
                int source = Level.FindCellIndex(linkSource);
                if (source >= 0) Dashed(Project(view.GetCellWorld(source) + Vector3.up * .12f), Event.current.mousePosition, BoardView.SignalColor(Level.cells[source].signalStyle), 2);
            }
        }

        void DrawPortalPair()
        {
            // Automatic pair IDs stay in map data. Selecting an endpoint reveals the relation
            // without putting opaque internal identifiers on the playable board.
            if (!selected.valid || selected.edge || !string.IsNullOrEmpty(held)
                || (selectedMaterial != PlayerRules.PortalEntrance && selectedMaterial != PlayerRules.PortalExit)) return;
            var cell = Level.cells[selected.cell];
            Direction front = (Direction)cell.rotation;
            Vector3 center = view.GetCellWorld(selected.cell);
            Vector3 tip = center + Delta(front) * .80f;
            Color color = new Color(.77f, .65f, 1, .9f);
            Dashed(Project(center + Vector3.up * .10f), Project(tip + Vector3.up * .10f), color, 2);
            if (string.IsNullOrEmpty(cell.portalPair)) return;
            for (int i = 0; i < Level.cells.Count; i++)
            {
                if (i == selected.cell || Level.cells[i].portalPair != cell.portalPair) continue;
                var target = PlayerEditTarget.Ground(i);
                TintSelection(target, new Color(color.r, color.g, color.b, .22f));
                Dashed(Project(center + Vector3.up * .15f), Project(view.GetCellWorld(i) + Vector3.up * .15f), color, 2);
                break;
            }
        }
        PlayerEditLink HitLink(Vector2 p)
        {
            if (!view) return null;
            PlayerEditLink found = null; float nearest = 6;
            foreach (var link in editing.Links())
            {
                Vector2 a = Project(view.GetCellWorld(link.sourceCell) + Vector3.up * .12f), b = Project(Center(link.target));
                if (Vector2.Distance(p, a) < 17 || Vector2.Distance(p, b) < 17) continue;
                Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                float distance = Vector2.Distance(p, a + t * ab); if (distance < nearest) { nearest = distance; found = link; }
            }
            return found;
        }
        void OpenDescription()
        {
            CancelHeld(); help = false;
            descriptionDraft = Level.description ?? ""; descriptionScroll = Vector2.zero;
            descriptionOpen = true; focusDescription = true;
        }
        void DrawDescription()
        {
            if (GUI.enabled && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            { descriptionOpen = false; GUI.FocusControl(null); Event.current.Use(); return; }
            Theme.DrawScrim(new Rect(0, 0, Screen.width, Screen.height));
            float width = Mathf.Min(680, Screen.width - 40), height = Mathf.Min(430, Screen.height - 70);
            var r = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            Theme.DrawPanel(r); Outline(Inset(r, 3), accent, 1);
            GUI.Label(new Rect(r.x + 22, r.y + 16, r.width - 44, 38), "关卡说明", title);
            GUI.Label(new Rect(r.x + 22, r.y + 61, r.width - 44, 43),
                "用简短文字告诉玩家目标、机关或提示。游玩时会显示这段说明。\n应用后加入地图；取消会保留原说明。", small);
            var viewport = new Rect(r.x + 22, r.y + 111, r.width - 44, r.height - 208);
            float textWidth = viewport.width - 20;
            float textHeight = Mathf.Max(viewport.height - 4, descriptionInput.CalcHeight(new GUIContent(descriptionDraft + "\n"), textWidth));
            descriptionScroll = GUI.BeginScrollView(viewport, descriptionScroll, new Rect(0, 0, textWidth, textHeight));
            GUI.SetNextControlName("BoxLab.MapDescription");
            // Imported legacy descriptions may be longer; keep them intact until the author
            // deliberately applies a shorter version. Never silently trim saved map content.
            descriptionDraft = GUI.TextArea(new Rect(0, 0, textWidth, textHeight), descriptionDraft, 2000, descriptionInput);
            GUI.EndScrollView();
            if (focusDescription && GUI.enabled && Event.current.type == EventType.Repaint)
            { GUI.FocusControl("BoxLab.MapDescription"); focusDescription = false; }
            bool valid = descriptionDraft.Length <= PlayerEditing.MaxDescriptionLength;
            GUI.Label(new Rect(r.x + 22, r.yMax - 83, r.width - 44, 30),
                descriptionDraft.Length + " / " + PlayerEditing.MaxDescriptionLength + " 字" + (valid ? " · 可留空" : " · 请缩短后再应用，原说明仍然保留"), small);
            if (Btn(new Rect(r.x + 22, r.yMax - 47, 160, 33), "应用说明", valid)) ApplyDescription();
            if (Btn(new Rect(r.x + 195, r.yMax - 47, 110, 33), "取消")) descriptionOpen = false;
        }
        void ApplyDescription()
        {
            if (descriptionDraft.Length > PlayerEditing.MaxDescriptionLength) return;
            bool changed = editing.SetDescription(descriptionDraft); descriptionOpen = false;
            Status = changed ? "关卡说明已更新，可撤销；保存地图后保留。" : "关卡说明没有变化。";
        }
        void DrawHelp()
        {
            if (GUI.enabled && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            { help = false; Event.current.Use(); return; }
            Theme.DrawScrim(new Rect(0, 0, Screen.width, Screen.height));
            float width = Mathf.Min(660, Screen.width - 40), height = Mathf.Min(450, Screen.height - 70);
            var r = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            Theme.DrawPanel(r); Outline(Inset(r, 3), accent, 1);
            GUI.Label(new Rect(r.x + 22, r.y + 18, r.width - 44, 34), "把想法摆成一张地图", title);
            string instructions = "① 选格子再点素材，或拖入素材。拿起后可点击 / 拖动涂抹，松手后可直接选下一处。\n\n② 墙门放在边缝；连线从按钮连到门或箭头。\n\n③ Q/E 旋转，Delete 或 × 删除最近放置层；切到「地面 / 物件」可选下层。整笔可撤销。\n\n④ 滚轮缩放，中键平移。草稿可不完整，试玩前会检查。\n\n⑤ 右侧「说明」可编辑关卡提示。应用后也可撤销，保存地图后保留。";
            var viewport = new Rect(r.x + 22, r.y + 66, r.width - 44, r.height - 135);
            float contentWidth = viewport.width - 20, contentHeight = text.CalcHeight(new GUIContent(instructions), contentWidth) + 8;
            helpScroll = GUI.BeginScrollView(viewport, helpScroll, new Rect(0, 0, contentWidth, Mathf.Max(viewport.height, contentHeight)));
            GUI.Label(new Rect(0, 0, contentWidth, contentHeight), instructions, text); GUI.EndScrollView();
            if (Btn(new Rect(r.x + 22, r.yMax - 57, r.width - 44, 36), "知道了，开始搭建")) { help = false; PlayerPrefs.SetInt("BoxLab.PlayerEditor.HelpSeen", 1); PlayerPrefs.Save(); }
        }
        string Name(string id) => string.IsNullOrEmpty(id) ? "开放边界" : materials.Find(m => m.id == id)?.name ?? book.Find(id)?.name ?? id;
        string SelectionName() => !selected.edge && selectionLayer == PlayerEditLayer.Actor && selectedMaterial == null ? "无物件" : Name(selectedMaterial);
        string Position(int index)
        {
            if (!Level.Contains(index)) return "地图外";
            int column = index % Level.width + 1; string letters = "";
            while (column > 0) { column--; letters = (char)('A' + column % 26) + letters; column /= 26; }
            return letters + (index / Level.width + 1);
        }
        static string DirectionName(int r) => new[] { "上", "右", "下", "左" }[(r + 4) % 4];
        bool ModeButton(Rect r, string label, int value)
        {
            bool chosen = mode == value && string.IsNullOrEmpty(held);
            bool hit = Theme.Button(r, label, true, chosen ? WorkshopButtonKind.Primary : WorkshopButtonKind.Secondary, 15);
            if (GuideActive && GuideStep == 5 && value == 1 && !chosen) Outline(Inset(r, 2), accent, 2);
            return hit;
        }
        bool Btn(Rect r, string label, bool enabled = true)
        { bool hit = Theme.Button(r, label, enabled, WorkshopButtonKind.Primary, 15); if (hit) GUI.FocusControl(null); return hit; }
        static Rect Inset(Rect r, float amount) => new Rect(r.x + amount, r.y + amount, r.width - amount * 2, r.height - amount * 2);
        static void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
        static void Outline(Rect r, Color c, float width) { Fill(new Rect(r.x, r.y, r.width, width), c); Fill(new Rect(r.x, r.yMax - width, r.width, width), c); Fill(new Rect(r.x, r.y, width, r.height), c); Fill(new Rect(r.xMax - width, r.y, width, r.height), c); }
        void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            // Clip only board strokes, not delete buttons or their tooltips. Insetting by
            // half the stroke width also keeps the rotated rectangle's thickness in view.
            float inset = width * .5f;
            var bounds = new Rect(paintRect.x + inset, paintRect.y + inset, paintRect.width - width, paintRect.height - width);
            if (!ClipLine(ref a, ref b, bounds)) return;
            var old = GUI.matrix; Vector2 d = b - a; GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - width * .5f, d.magnitude, width), c); GUI.matrix = old;
        }
        static bool ClipLine(ref Vector2 a, ref Vector2 b, Rect bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0) return false;
            Vector2 delta = b - a; float from = 0, to = 1;
            if (!ClipBoundary(-delta.x, a.x - bounds.xMin, ref from, ref to)
                || !ClipBoundary(delta.x, bounds.xMax - a.x, ref from, ref to)
                || !ClipBoundary(-delta.y, a.y - bounds.yMin, ref from, ref to)
                || !ClipBoundary(delta.y, bounds.yMax - a.y, ref from, ref to)) return false;
            b = a + delta * to; a += delta * from;
            return (b - a).sqrMagnitude > .0001f;
        }
        static bool ClipBoundary(float direction, float distance, ref float from, ref float to)
        {
            if (Mathf.Abs(direction) < .000001f) return distance >= 0;
            float t = distance / direction;
            if (direction < 0) { if (t > to) return false; from = Mathf.Max(from, t); }
            else { if (t < from) return false; to = Mathf.Min(to, t); }
            return true;
        }
        void Dashed(Vector2 a, Vector2 b, Color c, float width)
        {
            float length = Vector2.Distance(a, b); if (length < 1) return;
            for (float at = 0; at < length; at += 12) Line(Vector2.Lerp(a, b, at / length), Vector2.Lerp(a, b, Mathf.Min(length, at + 7) / length), c, width);
        }
    }
}
