using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    public sealed partial class PlayerEditor
    {
        const string GuideDefaultName = "工坊入门 · 我的第一道机关";
        const int GuidePlayStep = 8, GuideDoneStep = 9;
        static readonly HashSet<string> GuideDraftIds = new HashSet<string>();
        bool guideRunning;
        string guideWonLayout, guideCurrentLayout;
        string guideObservedStatus, guideFeedback;
        int guideFeedbackStep = -1;
        float guideFeedbackUntil;
        GUIStyle guideHeading, guideBody;
        Rect guidePanelRect;

        public bool GuideActive => guideRunning;
        public bool GuideComplete => guideRunning && GuideStep == GuideDoneStep;
        /// <summary>0..5 build, 6 name, 7 description, 8 play, 9 completed.</summary>
        public int GuideStep
        {
            get
            {
                if (!guideRunning) return -1;
                int step = FirstIncompleteGuideStep();
                return step == GuidePlayStep && !string.IsNullOrEmpty(guideWonLayout) && guideWonLayout == CurrentGuideLayout ? GuideDoneStep : step;
            }
        }

        public static LevelData CreateGuideLevel()
        {
            var level = LevelData.Create(5, 5, GuideDefaultName);
            level.player = -1;
            level.description = "";
            for (int row = 0; row < level.height; row++)
                level.edges.Add(new EdgeData { cell = level.Index(2, row), direction = Direction.East, kind = EdgeKind.Wall });
            GuideDraftIds.Add(level.id);
            return level;
        }

        public void BeginGuide()
        {
            if (!GuideDraftIds.Contains(Level.id))
                throw new InvalidOperationException("工坊引导只能在新建的练习草稿中启动，不会修改已有地图。");
            CancelHeld(); help = descriptionOpen = false; mode = 0; category = 1;
            selected = default(PlayerEditTarget); selectedMaterial = null; selectedLink = null;
            validationTarget = default(PlayerEditTarget); pendingValidationFocus = false;
            selectionDeleteRect = default(Rect); viewZoom = 1; viewPan = Vector3.zero;
            guideRunning = true; guideWonLayout = guideCurrentLayout = null; ShowGuideHint();
        }

        public void EndGuide()
        {
            guideRunning = false;
            Status = "已退出引导。这张练习草稿仍可继续编辑、试玩或保存。";
        }

        public void NotifyGuidePlayResult(LevelData playedLevel, GameStatus status)
        {
            if (!guideRunning || status != GameStatus.Won || playedLevel == null || playedLevel.id != Level.id || FirstIncompleteGuideStep() != GuidePlayStep) return;
            string played = GuideLayoutKey(playedLevel);
            if (played != CurrentGuideLayout) return;
            guideWonLayout = played;
            Status = "引导完成！你已经亲手跑通摆放、连线和试玩，可以保存这张地图或继续改造。";
        }

        static string GuideLayoutKey(LevelData level)
        {
            // Wording is editable without invalidating a genuinely completed playthrough.
            var layout = level.Clone(); layout.name = ""; layout.description = "";
            return PlayerStorage.Fingerprint(layout);
        }
        string CurrentGuideLayout => guideCurrentLayout ?? (guideCurrentLayout = GuideLayoutKey(Level));

        bool GuideSizeMatches => Level.width == 5 && Level.height == 5;
        int GuidePlayerCell => Level.Index(1, 2);
        int GuideBoxCell => Level.Index(2, 2);
        int GuideGoalCell => Level.Index(3, 2);
        PlayerEditTarget GuideGate => PlayerEditTarget.Boundary(GuideBoxCell, Direction.East);

        int FirstIncompleteGuideStep()
        {
            if (!GuideSizeMatches || Level.player != GuidePlayerCell) return 0;
            if (!Level.boxes.Contains(GuideBoxCell)) return 1;
            if (Level.GetCell(GuideGoalCell)?.ruleId != PlayerRules.Goal) return 2;
            if (Level.GetCell(GuidePlayerCell)?.ruleId != PlayerRules.Plate) return 3;
            var gate = Level.FindEdge(GuideBoxCell, Direction.East);
            if (gate == null || gate.kind != EdgeKind.Gate || gate.ruleId != PlayerRules.Gate) return 4;
            string sourceId = Level.GetCell(GuidePlayerCell).id;
            bool connected = gate.bindings != null && gate.bindings.Exists(binding => binding != null
                && binding.key == PlayerRules.Switches && binding.cellIds != null && binding.cellIds.Contains(sourceId));
            if (!connected) return 5;
            if (string.IsNullOrWhiteSpace(Level.name) || Level.name.Trim() == GuideDefaultName) return 6;
            if (string.IsNullOrWhiteSpace(Level.description)) return 7;
            return GuidePlayStep;
        }

        int GuideCategory
        {
            get
            {
                int step = GuideStep;
                if (step == 0 || step == 1) return 1;
                if (step == 2 || step == 3) return 0;
                return step == 4 ? 2 : -1;
            }
        }

        string GuideMaterial
        {
            get
            {
                switch (GuideStep)
                {
                    case 0: return PlayerEditing.Player;
                    case 1: return PlayerEditing.Box;
                    case 2: return PlayerRules.Goal;
                    case 3: return PlayerRules.Plate;
                    case 4: return PlayerRules.Gate;
                    default: return null;
                }
            }
        }

        string GuideTitle(int step)
        {
            if (!GuideSizeMatches) return "恢复练习地图的尺寸";
            switch (step)
            {
                case 0: return "放入人物";
                case 1: return "放入箱子";
                case 2: return "设定目标";
                case 3: return "在人脚下放压力板";
                case 4: return "把墙替换成门";
                case 5: return "连接按钮和门";
                case 6: return "给地图起个名字";
                case 7: return "写一段关卡说明";
                case 8: return "试玩自己搭好的机关";
                default: return "保存你的第一张地图";
            }
        }

        string GuideInstruction(int step)
        {
            if (!GuideSizeMatches) return "在右侧「尺寸」中恢复 5 × 5，即可继续提示。你仍可自由编辑，或退出引导继续创作。";
            switch (step)
            {
                case 0: return "把人物放在金框（B3）。\n从「物件」拖入，或先点格再点素材。";
                case 1: return "把箱子放在金框（C3）。\n人物在它左侧，可以向右推动。";
                case 2: return "点「地面」，选目标。\n放到金框 D3。";
                case 3: return "在人物脚下放「压力按钮」。\n人物不必移开，地面和物件能共存。";
                case 4: return "点「墙 / 门」，选联动门。\n替换 C3 右边的墙。";
                case 5: return "点「连线」后：\n先点 B3 按钮，再点 C3 右侧的门。";
                case 6: return "改右侧「名称」，给地图起个名字。\n上方标题会同步更新。";
                case 7: return "点右侧「说明」，写句提示。\n点「应用说明」后，试玩就能看到。";
                case 8: return "试玩后按 D 或方向键 →。\n人物压住按钮，箱子就能过门到目标。";
                default: return "关卡已经完成！\n保存后可以分享，也可以继续改造。";
            }
        }

        void ShowGuideHint()
        {
            if (!guideRunning) return;
            int step = GuideStep;
            if (step <= 1) category = 1;
            else if (step <= 3) category = 0;
            else if (step == 4) category = 2;
            paletteScroll = Vector2.zero;
            Status = GuideInstruction(step);
            guideObservedStatus = Status; guideFeedback = null; guideFeedbackStep = step;
        }

        string GuidePanelMessage(float now)
        {
            int step = GuideStep;
            if (guideFeedbackStep != step)
            {
                // A new task always opens with its own instruction, never an old status line.
                guideFeedbackStep = step; guideObservedStatus = Status; guideFeedback = null;
            }
            else if (guideObservedStatus != Status)
            {
                guideObservedStatus = Status;
                guideFeedback = Status == GuideInstruction(step) ? null : Status;
                guideFeedbackUntil = now + 6;
            }
            if (!string.IsNullOrEmpty(hoverTip)) return hoverTip;
            if (!string.IsNullOrEmpty(guideFeedback) && now < guideFeedbackUntil) return guideFeedback;
            return GuideInstruction(step);
        }

        void DrawGuideTargets()
        {
            if (!guideRunning || !GuideSizeMatches || !view) return;
            int step = GuideStep;
            Color color = accent; color.a = .78f + .20f * Mathf.Sin(Time.unscaledTime * 3);
            if (step == 0 || step == 3 || step == 5) DrawTarget(PlayerEditTarget.Ground(GuidePlayerCell), color, 4);
            if (step == 1) DrawTarget(PlayerEditTarget.Ground(GuideBoxCell), color, 4);
            if (step == 2 || step == GuidePlayStep) DrawTarget(PlayerEditTarget.Ground(GuideGoalCell), color, 4);
            if (step == 4 || step == 5) DrawTarget(GuideGate, color, 4);
        }

        void UpdateGuidePanelBounds()
        {
            guidePanelRect = default(Rect);
            if (!GuideActive || !view || !view.boardCamera) return;
            var avoid = new List<Rect>();
            int step = GuideStep;
            if (GuideSizeMatches)
            {
                if (step == 0 || step == 3 || step == 5) avoid.Add(GuideTargetBounds(PlayerEditTarget.Ground(GuidePlayerCell)));
                if (step == 1) avoid.Add(GuideTargetBounds(PlayerEditTarget.Ground(GuideBoxCell)));
                if (step == 2 || step == GuidePlayStep) avoid.Add(GuideTargetBounds(PlayerEditTarget.Ground(GuideGoalCell)));
                if (step == 4 || step == 5) avoid.Add(GuideTargetBounds(GuideGate));
            }
            var first = GuideTargetBounds(PlayerEditTarget.Ground(0));
            var last = GuideTargetBounds(PlayerEditTarget.Ground(Level.cells.Count - 1));
            var map = Rect.MinMaxRect(Mathf.Min(first.xMin, last.xMin), Mathf.Min(first.yMin, last.yMin),
                Mathf.Max(first.xMax, last.xMax), Mathf.Max(first.yMax, last.yMax));
            guidePanelRect = ChooseGuidePanelRect(paintRect, map, avoid);
        }

        Rect GuideTargetBounds(PlayerEditTarget target)
        {
            Vector3 center = Center(target);
            float x = target.edge && (target.side == Direction.East || target.side == Direction.West) ? .06f : .48f;
            float z = target.edge && (target.side == Direction.North || target.side == Direction.South) ? .06f : .48f;
            Vector2 a = Project(center + new Vector3(-x, 0, -z)), b = Project(center + new Vector3(x, 0, z));
            // Include the selection outline and raised artwork above its ground footprint.
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 10, Mathf.Min(a.y, b.y) - 18,
                Mathf.Max(a.x, b.x) + 10, Mathf.Max(a.y, b.y) + 10);
        }

        internal static Rect ChooseGuidePanelRect(Rect viewport, Rect map, List<Rect> targets)
        {
            var area = Inset(viewport, 12);
            float height = Mathf.Min(168, area.height);
            Rect best = new Rect(area.x, area.y, Mathf.Min(360, area.width), height);
            float bestScore = float.PositiveInfinity;
            foreach (float requestedWidth in new[] { 360f, 320f, 280f, 260f, 240f })
            {
                float width = Mathf.Min(requestedWidth, area.width);
                for (int corner = 0; corner < 6; corner++)
                {
                    float x = corner % 2 == 0 ? area.x : area.xMax - width;
                    float y = corner < 2 ? area.y : corner < 4 ? area.yMax - height : area.center.y - height * .5f;
                    var candidate = new Rect(x, y, width, height);
                    float obscured = 0;
                    foreach (var target in targets) obscured += OverlapArea(candidate, target);
                    // Avoid the current task first, then the board. A wide viewport naturally
                    // uses its empty left margin; a narrow one can choose the opposite corner.
                    float score = obscured * 100000 + OverlapArea(candidate, map) * 2 + (360 - width) * 20 + corner;
                    if (score < bestScore) { bestScore = score; best = candidate; }
                }
            }
            return best;
        }
        static float OverlapArea(Rect a, Rect b) => Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin))
            * Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));

        void DrawGuidePanel()
        {
            if (guidePanelRect.width <= 0) return;
            int step = GuideStep; var r = guidePanelRect;
            Fill(new Rect(r.x + 4, r.y + 5, r.width, r.height), new Color(0, 0, 0, .25f));
            Theme.DrawPanel(r); Outline(Inset(r, 2), accent, 2);
            if (guideHeading == null)
            {
                guideHeading = Theme.CreateLabel(16); guideHeading.fontStyle = FontStyle.Bold; guideHeading.alignment = TextAnchor.MiddleLeft;
                guideBody = Theme.CreateLabel(15);
            }
            GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, 29),
                (step == GuideDoneStep ? "完成 · " : (step + 1) + " / 9 · ") + GuideTitle(step), guideHeading);
            Fill(new Rect(r.x + 12, r.y + 41, r.width - 24, 2), new Color(accent.r, accent.g, accent.b, .28f));
            Fill(new Rect(r.x + 12, r.y + 41, (r.width - 24) * Mathf.Min(1, (step + 1) / 9f), 2), accent);
            string message = GuidePanelMessage(Time.unscaledTime);
            GUI.Label(new Rect(r.x + 12, r.y + 51, r.width - 24, r.height - 97), message,
                message == GuideInstruction(step) ? guideBody : small);
            bool action = step >= 6;
            float gap = 6, width = (r.width - 24 - (action ? 2 : 1) * gap) / (action ? 3 : 2);
            float y = r.yMax - 39;
            if (Theme.Button(new Rect(r.x + 12, y, width, 28), "再提示", true, WorkshopButtonKind.Secondary, 13)) ShowGuideHint();
            if (Theme.Button(new Rect(r.x + 12 + width + gap, y, width, 28), width < 74 ? "退出" : "退出引导", true, WorkshopButtonKind.Secondary, 13)) EndGuide();
            if (action)
            {
                string label = step == 6 ? "改名称" : step == 7 ? "写说明" : step == GuidePlayStep ? "试玩" : "保存";
                if (Theme.Button(new Rect(r.xMax - 12 - width, y, width, 28), label, true, WorkshopButtonKind.Primary, 13))
                {
                    FinishGesture();
                    if (step == 6) GUI.FocusControl("BoxLab.MapName");
                    else if (step == 7) OpenDescription();
                    else if (step == GuidePlayStep) PlayRequested?.Invoke();
                    else SaveRequested?.Invoke();
                }
            }
        }
    }
}
