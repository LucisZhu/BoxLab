using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    public sealed partial class WorkshopApp
    {
        private int helpPage;
        private GUIStyle helpCopy, helpName, helpSection;
        // Resource textures belong to Unity's asset cache, not the disposable map previews.
        private readonly Dictionary<string, Texture2D> helpThumbnails = new Dictionary<string, Texture2D>();
        public int HelpPage => helpPage;
        public int HelpPageCount => Page == WorkshopPage.Edit ? 3 : 2;
        public bool HelpVisible => help;

        public void OpenHelp(int page = 0)
        {
            helpPage = Mathf.Clamp(page, 0, HelpPageCount - 1);
            helpScroll = Vector2.zero;
            if (editor != null) editor.DismissHelp();
            help = true;
        }
        public void CloseHelp() { help = false; }

        private void EnsureHelpStyles()
        {
            if (helpCopy != null) return;
            helpCopy = WorkshopTheme.Shared.CreateLabel(16);
            helpCopy.alignment = TextAnchor.MiddleLeft;
            helpName = new GUIStyle(helpCopy) { fontStyle = FontStyle.Bold };
            helpSection = WorkshopTheme.Shared.CreateLabel(18);
            helpSection.fontStyle = FontStyle.Bold;
            helpSection.alignment = TextAnchor.MiddleLeft;
        }
        private void DrawHelp()
        {
            EnsureHelpStyles();
            helpPage = Mathf.Clamp(helpPage, 0, HelpPageCount - 1);
            Rect r = DialogRect(820, 570); WorkshopTheme.Shared.DrawPanel(r);
            GUI.Label(new Rect(r.x + 24, r.y + 16, r.width - 170, 38),
                helpPage == 2 ? "三分钟认识地图工坊" : "推箱工坊 · 操作指南", heading);
            GUI.Label(new Rect(r.xMax - 112, r.y + 22, 88, 28), (helpPage + 1) + " / " + HelpPageCount + " 页", muted);

            string[] tabs = { "基本操作与地块", "特殊地块与编辑", "工坊操作" };
            float tabWidth = (r.width - 48 - (HelpPageCount - 1) * 8) / HelpPageCount;
            for (int i = 0; i < HelpPageCount; i++)
            {
                int page = i;
                Rect tab = new Rect(r.x + 24 + i * (tabWidth + 8), r.y + 62, tabWidth, 31);
                if (WorkshopTheme.Shared.Button(tab, tabs[i], true,
                    i == helpPage ? WorkshopButtonKind.Primary : WorkshopButtonKind.Secondary, 14)) Queue(() => OpenHelp(page));
            }

            Rect content = new Rect(r.x + 24, r.y + 106, r.width - 48, r.height - 174);
            if (helpPage == 0) DrawHelpBasics(content);
            else if (helpPage == 1) DrawHelpSpecials(content);
            else DrawEditorHelp(content);

            WorkshopTheme.Shared.DrawDivider(new Rect(r.x + 24, r.yMax - 62, r.width - 48, 1));
            float buttonY = r.yMax - 49;
            Button(new Rect(r.x + 24, buttonY, 94, 34), "← 上一页", () => OpenHelp(helpPage - 1), helpPage > 0, true);
            Button(new Rect(r.x + 126, buttonY, 94, 34), "下一页 →", () => OpenHelp(helpPage + 1), helpPage < HelpPageCount - 1, true);
            if (Page != WorkshopPage.Edit && Page != WorkshopPage.Play)
                Button(new Rect(r.x + 236, buttonY, 190, 34), "跟着做一张地图", StartEditorGuide, true, true);
            Button(new Rect(r.xMax - 144, buttonY, 120, 34), "关闭指南", CloseHelp, true, true);
        }

        private void DrawHelpBasics(Rect r)
        {
            float y = r.y;
            GUI.Label(new Rect(r.x, y, r.width, 26), "基本操作", helpSection); y += 28;
            GUI.Label(new Rect(r.x, y, r.width, 25), "移动  WASD / 方向键     撤销  Z     重开  R（可撤销）", helpCopy); y += 25;
            GUI.Label(new Rect(r.x, y, r.width, 25), "让每个目标都有箱子。人物站在箱子后面，每次推动一格。", helpCopy); y += 31;
            GUI.Label(new Rect(r.x, y, r.width, 26), "格子效果", helpSection); y += 29;
            HelpRow(r, ref y, PlayerRules.Plain, "普通地面", "人物和箱子都能经过。");
            HelpRow(r, ref y, PlayerRules.Goal, "目标", "把箱子推到金色目标上；所有目标都有箱子即可通关。");
            HelpRow(r, ref y, PlayerRules.Ice, "冰面", "箱子进入后，沿进入方向额外滑一格。");
            HelpRow(r, ref y, PlayerRules.Arrow, "箭头格", "箱子进入后，沿箭头方向再移动一格。");
            HelpRow(r, ref y, PlayerRules.Plate, "压力按钮", "人物或箱子压住时激活，无物件压住时解除。");
            HelpRow(r, ref y, PlayerRules.RotationPlate, "旋转按钮", "每次新压下，让关联箭头顺时针转 90°。");
            HelpRow(r, ref y, PlayerRules.Selective, "人物通道", "只允许人物经过，箱子不能进入。");
            HelpRow(r, ref y, PlayerRules.BoxOnly, "箱子通道", "只允许箱子进入；推走箱子时，人物留在原位。");
        }

        private void DrawHelpSpecials(Rect r)
        {
            float y = r.y;
            GUI.Label(new Rect(r.x, y, r.width, 26), "格子效果 · 特殊机关", helpSection); y += 29;
            HelpRow(r, ref y, PlayerRules.PortalEntrance, "传送入口", "箱子须从门正面进入；人物经过门口不传送。");
            HelpRow(r, ref y, PlayerRules.PortalExit, "传送出口", "沿门正面自动走一格；出口或前方受阻，箱子留在入口。");
            HelpRow(r, ref y, PlayerRules.Hole, "空洞", "人物不能进；箱子掉入后消失，剩余少于目标数时失败。");
            HelpRow(r, ref y, PlayerRules.Fragile, "坍塌地面", "物件离开后变空洞；推走箱子时人物不跟进。");
            HelpRow(r, ref y, PlayerEditing.Wall, "墙", "阻挡相邻两格间移动，人物和箱子都不能穿过。");
            HelpRow(r, ref y, PlayerRules.Gate, "联动门", "关联按钮全部受压才开门，松开任意一个即关门。");
            y += 9;
            WorkshopTheme.Shared.DrawDivider(new Rect(r.x, y, r.width, 1)); y += 12;
            GUI.Label(new Rect(r.x, y, r.width, 27), "地图编辑器", helpSection); y += 30;
            GUI.Label(new Rect(r.x, y, r.width, 28), "在“我的关卡”里创作、保存、分享与导入。", helpCopy); y += 29;
            GUI.Label(new Rect(r.x, y, r.width, 42), "复制分享码发给朋友，对方粘贴即可还原地图，不需要传文件。", helpCopy);
        }

        private void HelpRow(Rect section, ref float y, string material, string name, string explanation)
        {
            Texture2D image;
            if (!helpThumbnails.TryGetValue(material, out image))
            {
                image = Resources.Load<Texture2D>(PlayerEditor.ThumbnailResourcePath(material));
                helpThumbnails.Add(material, image);
            }
            Rect icon = new Rect(section.x + 2, y + 3, 26, 26);
            if (image) GUI.DrawTexture(icon, image, ScaleMode.ScaleToFit, true);
            else GUI.Label(icon, "◇", helpCopy);
            GUI.Label(new Rect(section.x + 38, y, 100, 32), name, helpName);
            GUI.Label(new Rect(section.x + 142, y, section.width - 142, 32), explanation, helpCopy);
            y += 32;
        }

        private void DrawEditorHelp(Rect r)
        {
            string[] sections = {
                "1  放置：先选格子或边，再点右侧素材；也可拖动素材，或点击素材拿起后再放。",
                "2  调整：Q / E 旋转 90°。绿色可放，红色不可放。选中后点红叉，或拖到垃圾桶删除。默认删除最近放的一层，可切换地面/物件。",
                "3  连线：点「连线」，先点按钮，再点门或箭头。压力按钮只能连门，旋转按钮只能连箭头。连好一对后自动回到选择模式。",
                "4  试玩：放一个人物和至少一个目标；箱子可以多于目标，但不能少于目标；检查传送配对、机关连线，再点击试玩。",
                "右侧「名称」可改关卡名，「说明」可填写给玩家看的提示。",
                "随时保存草稿；Ctrl+Z 撤销，Ctrl+Y 重做。试玩结束返回原来的地图。"
            };
            float width = r.width - 20, height = 0;
            foreach (string section in sections) height += body.CalcHeight(new GUIContent(section), width) + 17;
            helpScroll = GUI.BeginScrollView(r, helpScroll, new Rect(0, 0, width, Mathf.Max(r.height, height)));
            float y = 0;
            foreach (string section in sections)
            {
                float textHeight = body.CalcHeight(new GUIContent(section), width);
                GUI.Label(new Rect(0, y, width, textHeight), section, body); y += textHeight + 17;
            }
            GUI.EndScrollView();
        }
    }
}
