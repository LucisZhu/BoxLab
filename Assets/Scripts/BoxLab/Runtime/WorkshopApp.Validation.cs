using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    public sealed partial class WorkshopApp
    {
        private List<ValidationIssue> validationIssues;
        private Vector2 validationScroll;

        private void DrawValidationIssues()
        {
            bool errors = LevelValidator.HasErrors(validationIssues);
            Rect r = DialogRect(760, 540);
            WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 24, r.y + 20, r.width - 48, 40), errors ? "地图还需要调整" : "试玩前留意一下", heading);
            GUI.Label(new Rect(r.x + 24, r.y + 67, r.width - 48, 50),
                errors ? "点击有位置的问题即可定位。半成品仍然可以保存为草稿。" : "以下提示不会阻止试玩；你可以先修改，也可以继续验证。", body);
            float textWidth = r.width - 210, total = 0;
            foreach (var issue in validationIssues) total += Mathf.Max(56, body.CalcHeight(new GUIContent(issue.message), textWidth) + 24);
            validationScroll = GUI.BeginScrollView(new Rect(r.x + 24, r.y + 130, r.width - 48, r.height - 214), validationScroll, new Rect(0, 0, r.width - 72, total));
            float y = 0;
            foreach (var issue in validationIssues)
            {
                float h = Mathf.Max(56, body.CalcHeight(new GUIContent(issue.message), textWidth) + 24);
                WorkshopTheme.Shared.DrawPanel(new Rect(0, y, r.width - 76, h - 6), WorkshopPanelKind.Card);
                GUI.Label(new Rect(10, y + 7, textWidth - 8, h - 12), (issue.severity == ValidationSeverity.Error ? "需调整 · " : "提示 · ") + issue.message, body);
                bool located = pendingTest && editor != null && editor.Level.Contains(issue.cellIndex);
                if (located)
                {
                    var selectedIssue = issue;
                    int column = issue.cellIndex % pendingLevel.width;
                    string place = (column < 26 ? ((char)('A' + column)).ToString() : "A" + (char)('A' + column - 26)) + (issue.cellIndex / pendingLevel.width + 1);
                    Button(new Rect(r.width - 196, y + 8, 108, 36), "定位 " + place, () => { validationIssues = null; editor.FocusValidationIssue(selectedIssue); }, true, true);
                }
                y += h;
            }
            GUI.EndScrollView();
            Button(new Rect(r.x + 24, r.yMax - 62, 180, 40), "返回修改", () => validationIssues = null);
            if (!errors) Button(new Rect(r.xMax - 236, r.yMax - 62, 212, 40), "继续检查并试玩", BeginSolutionCheck);
        }
    }
}
