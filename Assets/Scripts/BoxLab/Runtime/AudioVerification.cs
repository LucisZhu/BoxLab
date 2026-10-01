using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Opt-in runtime acceptance. Never boots itself or changes stored preferences.</summary>
    public static class AudioVerification
    {
        /// <summary>When a controller is supplied, call while the workshop is on Home; temporary fixtures are loaded.</summary>
        public static IEnumerator RunAll(List<string> results, GameController controller = null)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            float savedVolume = WorkshopAudio.Volume; bool savedMuted = WorkshopAudio.Muted;
            try
            {
                WorkshopAudio.StopAll(); WorkshopAudio.Configure(.4f, false);
                int count = WorkshopAudio.PlayedCount;
                Require(WorkshopAudio.Play(WorkshopSound.UI), "有效UI音效未被接受。");
                Require(WorkshopAudio.PlayedCount == count + 1 && WorkshopAudio.LastSound == WorkshopSound.UI, "播放计数/最近音效不匹配。");
                Require(!WorkshopAudio.Play(WorkshopSound.UI) && WorkshopAudio.PlayedCount == count + 1, "同帧连续点击未节流。");
                WorkshopAudio.Play(WorkshopSound.Win); // A longer cue keeps the device check stable during a slow first frame.
                yield return null;
                Require(WorkshopAudio.ActiveVoiceCount > 0 && WorkshopAudio.ActiveVoiceCount <= WorkshopAudio.MaxVoices, "真实音源未开始播放或超过上限。");
                WorkshopAudio.StopAll();
                Require(WorkshopAudio.Play(WorkshopSound.Win) && WorkshopAudio.Play(WorkshopSound.Teleport) &&
                    WorkshopAudio.Play(WorkshopSound.Fall) && WorkshopAudio.Play(WorkshopSound.Undo), "并发验收音效未完整进入池。");
                Require(WorkshopAudio.ActiveVoiceCount == WorkshopAudio.MaxVoices, "音源池没有保持四个并发声部。");
                count = WorkshopAudio.PlayedCount;
                Require(!WorkshopAudio.Play(WorkshopSound.Step) && WorkshopAudio.PlayedCount == count, "低优先级脚步切断了高优先级音效。");
                WorkshopAudio.Configure(.4f, true);
                Require(WorkshopAudio.ActiveVoiceCount == 0 && !WorkshopAudio.Play(WorkshopSound.Push), "静音没有立即停止全部音源。");
                WorkshopAudio.Configure(0, false);
                Require(!WorkshopAudio.Play(WorkshopSound.UI) && WorkshopAudio.PlayedCount == count, "零音量仍创建播放事件。");
                WorkshopAudio.Configure(2, false); Require(WorkshopAudio.Volume == 1, "音量没有限制到1。");
                WorkshopAudio.Configure(-1, false); Require(WorkshopAudio.Volume == 0, "音量没有限制到0。");
                WorkshopAudio.Configure(float.NaN, false); Require(WorkshopAudio.Volume == 0, "非法音量没有安全归零。");
                results.Add("音效：真实播放、重复节流、四路上限/优先级、立即静音及0..1音量均通过。");

                int verified = 0;
                foreach (WorkshopSound sound in Enum.GetValues(typeof(WorkshopSound)))
                {
                    if (sound == WorkshopSound.None) continue;
                    var clip = WorkshopAudio.BuildClip(sound);
                    try
                    {
                        Require(clip && clip.channels == 1 && clip.frequency == 44100 && clip.length > .04f && clip.length <= 1, sound + " 格式或长度错误。");
                        var data = new float[clip.samples]; Require(clip.GetData(data, 0), sound + " 样本不可读。");
                        double energy = 0; float peak = 0;
                        foreach (float value in data)
                        {
                            Require(!float.IsNaN(value) && !float.IsInfinity(value), sound + " 包含无效样本。");
                            peak = Mathf.Max(peak, Mathf.Abs(value)); energy += value * value;
                        }
                        Require(peak > .001f && peak <= .2201f && energy > .001, sound + " 静音或峰值超限。");
                        if (sound == WorkshopSound.UI) Require(Math.Sqrt(energy / data.Length) > .05 && peak > .20f,
                            "按钮点击声音量没有达到本轮可辨识要求。");
                        Require(Mathf.Abs(data[0]) < .001f && Mathf.Abs(data[data.Length - 1]) < .001f, sound + " 首尾未淡化，可能产生爆音。");
                        verified++;
                    }
                    finally { if (clip) UnityEngine.Object.Destroy(clip); }
                }
                results.Add("音效：" + verified + "个原创提示片段均为44.1kHz单声道，首尾淡化、样本有效，四声叠加峰值仍小于1。");
                VerifyFrameClassification(results);
                if (controller)
                {
                    WorkshopAudio.Configure(.4f, false); WorkshopAudio.StopAll();
                    var book = PlayerRules.CreateBook();
                    var level = LevelData.Create(5, 5, "验收 · 操作音效");
                    level.player = level.Index(1, 1); DemoLevels.Box(level, 2, 2); DemoLevels.Tile(level, 4, 4, PlayerRules.Goal);
                    count = WorkshopAudio.PlayedCount; controller.Load(level, book); controller.Restart(); controller.Undo();
                    Require(WorkshopAudio.PlayedCount == count, "载入或无效撤销/重开产生音效。");
                    controller.TryMove(Direction.East);
                    double deadline = Time.realtimeSinceStartupAsDouble + 4;
                    while (controller.IsBusy) { Require(Time.realtimeSinceStartupAsDouble < deadline, "音效控制器验收等待超时。"); yield return null; }
                    Require(WorkshopAudio.PlayedCount == count + 1 && WorkshopAudio.LastSound == WorkshopSound.Step, "有效普通移动未发出单次脚步。");
                    WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount; controller.Undo();
                    Require(WorkshopAudio.PlayedCount == count + 1 && WorkshopAudio.LastSound == WorkshopSound.Undo, "有效撤销未播放提示。");
                    WorkshopAudio.StopAll(); controller.TryMove(Direction.East);
                    while (controller.IsBusy) { Require(Time.realtimeSinceStartupAsDouble < deadline, "音效重开验收等待超时。"); yield return null; }
                    WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount; controller.Restart();
                    Require(WorkshopAudio.PlayedCount == count + 1 && WorkshopAudio.LastSound == WorkshopSound.Undo, "有效重开未播放提示。");
                    controller.Load(level, book); WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount;
                    controller.TryMove(Direction.South);
                    while (controller.IsBusy) { Require(Time.realtimeSinceStartupAsDouble < deadline, "受阻验收等待超时。"); yield return null; }
                    WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount; controller.TryMove(Direction.South);
                    Require(WorkshopAudio.PlayedCount == count + 1 && WorkshopAudio.LastSound == WorkshopSound.Blocked, "受阻移动没有提示音。");
                    controller.Load(level, book); WorkshopAudio.StopAll(); controller.TryMove(Direction.North);
                    yield return WaitForController(controller);
                    WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount; controller.TryMove(Direction.East);
                    yield return WaitForController(controller);
                    Require(WorkshopAudio.PlayedCount == count + 1 && WorkshopAudio.LastSound == WorkshopSound.Push, "普通推箱未发出单次木箱提示。");
                    foreach (bool fall in new[] { false, true })
                    {
                        level = LevelData.Create(5, 5, "验收 · 终局音效");
                        level.player = level.Index(1, 2); DemoLevels.Box(level, 2, 2);
                        DemoLevels.Tile(level, fall ? 4 : 3, fall ? 4 : 2, PlayerRules.Goal);
                        if (fall) DemoLevels.Tile(level, 3, 2, PlayerRules.Hole);
                        controller.Load(level, book); WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount;
                        controller.TryMove(Direction.East); yield return WaitForController(controller);
                        Require(WorkshopAudio.PlayedCount == count + 2 && WorkshopAudio.LastSound == (fall ? WorkshopSound.Fall : WorkshopSound.Win),
                            "推箱后的通关/掉落声音遗漏或重复。");
                    }
                    foreach (bool blocked in new[] { false, true })
                    {
                        level = LevelData.Create(5, 5, "验收 · 冰面滑动音效");
                        level.player = level.Index(1, 1); DemoLevels.Box(level, 2, 1);
                        DemoLevels.Tile(level, 3, 1, PlayerRules.Ice); DemoLevels.Tile(level, 4, 4, PlayerRules.Goal);
                        if (blocked) level.edges.Add(new EdgeData { cell = level.Index(3, 1), direction = Direction.East, kind = EdgeKind.Wall });
                        controller.Load(level, book); WorkshopAudio.StopAll(); count = WorkshopAudio.PlayedCount;
                        controller.TryMove(Direction.East); yield return WaitForController(controller);
                        Require(controller.State.boxes[0] == level.Index(blocked ? 3 : 4, 1), "冰面验收未形成预期额外移动。");
                        Require(WorkshopAudio.PlayedCount == count + (blocked ? 1 : 2) &&
                            WorkshopAudio.LastSound == (blocked ? WorkshopSound.Push : WorkshopSound.IceSlide),
                            "冰面出溜音效未严格跟随真实滑动；受阻时不应播放。");
                    }
                    results.Add("音效：真实控制器载入/无效操作安静；移动、推箱、受阻、撤销、重开以及通关/掉落单次反馈均通过。");
                    results.Add("音效：冰面确实额外滑动时才播放出溜声；进入冰面但撞墙停止时只有推箱声。");
                }
            }
            finally { WorkshopAudio.StopAll(); WorkshopAudio.Configure(savedVolume, savedMuted); }
        }

        static IEnumerator WaitForController(GameController controller)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 4;
            while (controller.IsBusy)
            { Require(Time.realtimeSinceStartupAsDouble < deadline, "音效操作验收等待超时。"); yield return null; }
        }

        static void VerifyFrameClassification(List<string> results)
        {
            var book = PlayerRules.CreateBook();
            var level = LevelData.Create(5, 5); level.player = 0; level.boxes.Add(6); level.cells[24].ruleId = PlayerRules.Goal;
            level.cells[6].ruleId = PlayerRules.PortalEntrance; level.cells[7].ruleId = PlayerRules.PortalExit;
            level.cells[6].portalPair = level.cells[7].portalPair = "audio-test";
            level.cells[12].ruleId = PlayerRules.Arrow; level.cells[13].ruleId = PlayerRules.Plate;
            var before = RuleEngine.CreateState(level, book); var after = before.Clone();
            Require(GameControllerAudio.DescribeFrame(level, book, before, after, false) == FrameAudioCue.None, "相同状态产生了机关声音。");
            after.boxes[0] = 7;
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, false) & FrameAudioCue.Teleport) != 0, "相邻两端的传送未识别。");
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, true) & FrameAudioCue.Teleport) == 0, "常规第一步推动被误报传送。");
            after = before.Clone(); after.boxes[0] = -1;
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, false) & FrameAudioCue.Fall) != 0, "箱子掉落未识别。");
            after = before.Clone(); after.rotations[12] = 1;
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, false) & FrameAudioCue.Rotation) != 0, "箭头旋转未识别。");
            after = before.Clone(); after.player = 13;
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, false) & FrameAudioCue.Mechanism) != 0, "压力板变化未识别。");
            level.cells[6].ruleId = PlayerRules.Ice; before = RuleEngine.CreateState(level, book); after = before.Clone();
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, false) & FrameAudioCue.IceSlide) == 0, "静止冰面产生了滑动声。");
            after.boxes[0] = 7;
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, false) & FrameAudioCue.IceSlide) != 0, "真实冰面额外滑动未识别。");
            Require((GameControllerAudio.DescribeFrame(level, book, before, after, true) & FrameAudioCue.IceSlide) == 0, "普通推动离开冰面误报了自动滑行。");
            results.Add("音效：帧差识别相邻传送、掉落、旋转与压力板；相同状态/普通初始推箱不会误报。");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
