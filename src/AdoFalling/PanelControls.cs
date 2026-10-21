using System;
using System.Collections.Generic;
using System.Globalization;

namespace AdoFalling8K
{
    internal enum PanelControlKind { Flag, Number, Choice, Action }

    /// <summary>One definition shared by mouse drawing and keyboard navigation.</summary>
    internal sealed class PanelControl
    {
        public string Section, Label, Text;
        public PanelControlKind Kind;
        public double Min, Max, Step;
        public Func<double> Number;
        public Func<string> Display;
        public Action Click;
        public Action<int> Choose;
        internal Action<double> Write;
        internal Action ResetValue, Changed;

        public void Sync()
        { if (Number != null) Text = Number().ToString("0.###", CultureInfo.InvariantCulture); }
        public void SetNumber(double value)
        { Write(Math.Max(Min, Math.Min(Max, value))); if (Changed != null) Changed(); }
        public bool ApplyText(string text)
        {
            Text = text;
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.IsNaN(value) || double.IsInfinity(value)) return false;
            SetNumber(value); return true;
        }
        public void Activate()
        {
            if (Click != null) Click();
            else if (Choose != null) Choose(1);
            else Reset(true);
        }
        public void Nudge(int direction, int coarse)
        {
            if (Kind == PanelControlKind.Number) { SetNumber(Number() + direction * coarse * Step); Sync(); }
            else if (Choose != null) Choose(direction);
            else if (Kind == PanelControlKind.Flag) Click();
        }
        internal void Reset(bool notify)
        {
            if (ResetValue == null) return;
            ResetValue(); Sync();
            if (notify && Changed != null) Changed();
        }
    }

    internal sealed class PanelControls
    {
        public readonly List<PanelControl> Rows = new List<PanelControl>();
        public int Selection;
        private readonly FallingConfig _config;
        private readonly Action _retune, _resetStats;
        private static readonly FingeringMode[] Modes = (FingeringMode[])Enum.GetValues(typeof(FingeringMode));

        public PanelControls(FallingConfig c, Action retune, Action resetStats,
            Action loadFile, Action stopFile, Action logAlignment)
        {
            _config = c; _retune = retune; _resetStats = resetStats;
            string s = "显示（一键切换）";
            Flag(s, "显示判定线", c.ShowJudgeLine); Flag(s, "显示轨道", c.ShowLanes);
            Flag(s, "显示音符 / 长按", c.ShowNotes); Flag(s, "显示打击特效", c.ShowHitEffects);
            Flag(s, "显示判定文字", c.ShowJudgeText);
            s = "判定线与轨道";
            Number(s, "判定线高度", c.JudgeLineY, 0, 1, .005);
            Number(s, "轨道宽度", c.LaneWidth, 40, 400, 4);
            Number(s, "轨道间距", c.LaneGap, 0, 80, 1);
            Number(s, "F/J 额外间距", c.FJGap, 0, 600, 5);
            s = "音符";
            Number(s, "下落速度", c.NoteSpeedPxPerSec, 100, 3000, 20);
            Number(s, "音符高度", c.NoteHeight, 6, 120, 1);
            s = "对齐（判定与视觉）";
            Number(s, "音符提前量(秒)", c.LeadOffsetManual, -.5, .5, .005);
            Number(s, "提前量系数", c.LeadFraction, 0, 1.5, .02);
            Number(s, "开场余量(秒)", c.LeadInSeconds, 0, 1, .02);
            Number(s, "长按提前量(秒)", c.HoldOffsetSeconds, -.5, .5, .005);
            Number(s, "长按长度系数", c.HoldScale, .2, 3, .02);
            s = "透明度";
            Number(s, "判定线", c.JudgeOpacity, 0, 1, .02);
            Number(s, "音符", c.NoteOpacity, 0, 1, .02);
            Number(s, "轨道底色", c.LaneOpacity, 0, 1, .02);
            Number(s, "轨道边框", c.LaneAlpha, 0, 1, .02);
            Number(s, "长按尾迹", c.HoldBodyOpacity, 0, 1, .02);
            s = "行为开关";
            Flag(s, "长按：按住期间缩短", c.HoldShrinkWhileHeld);
            Flag(s, "长按：跳过松手方块", c.SkipHoldReleaseTiles);
            Flag(s, "开场从顶端下落", c.CountdownLead);
            Flag(s, "显示诊断信息", c.DebugHud, false); Flag(s, "自动播放（游戏自演）", c.AutoPlay, false);
            s = "八轨指法";
            Rows.Add(new PanelControl { Section = s, Label = "排键方案", Kind = PanelControlKind.Choice,
                Display = delegate { return FingeringPlanner.ModeName(c.FingeringMode.Value); },
                Choose = delegate(int d) { int i = Array.IndexOf(Modes, c.FingeringMode.Value);
                    c.FingeringMode.Value = Modes[(i + (d < 0 ? Modes.Length - 1 : 1)) % Modes.Length]; },
                ResetValue = c.FingeringMode.Reset });
            Number(s, "节奏保护基准(ms)", c.FingeringMinIntervalMs, 0, 500, 5, false);
            Number(s, "多押对齐窗口(ms)", c.ChordWindowMs, 0, 100, 1, false);
            s = "文件谱面";
            ActionRow(s, "载入 / 重启谱面", loadFile); ActionRow(s, "退出文件模式", stopFile);
            ActionRow(s, "输出对齐日志", logAlignment);
            s = "操作";
            ActionRow(s, "恢复默认", ResetAll);
            ActionRow(s, "长按归零", delegate { c.HoldOffsetSeconds.Reset(); c.HoldScale.Reset(); _retune(); Sync(); });
            ActionRow(s, "清除统计数据", resetStats);
            Sync();
        }

        public void MoveSelection(int delta) { Selection = (Selection + delta + Rows.Count) % Rows.Count; }
        public void Sync() { foreach (PanelControl row in Rows) row.Sync(); }
        public void ResetAll()
        {
            foreach (PanelControl row in Rows) row.Reset(false);
            _config.FingeringWindowMs.Reset(); _config.FingeringBpm.Reset(); _config.FingeringFollowSpeed.Reset();
            _retune(); _resetStats(); Sync();
        }
        private void ActionRow(string section, string label, Action action)
        { Rows.Add(new PanelControl { Section = section, Label = label, Kind = PanelControlKind.Action, Click = action }); }
        private void Flag(string section, string label, Setting<bool> setting, bool reset = true)
        {
            Rows.Add(new PanelControl { Section = section, Label = label, Kind = PanelControlKind.Flag,
                Display = delegate { return setting.Value ? "[开]" : "[关]"; },
                Click = delegate { setting.Value = !setting.Value; }, ResetValue = reset ? (Action)setting.Reset : null });
        }
        private void Number(string section, string label, Setting<float> setting, double min, double max, double step)
        { Number(section, label, delegate { return setting.Value; }, delegate(double v) { setting.Value = (float)v; }, setting.Reset, min, max, step, true); }
        private void Number(string section, string label, Setting<double> setting, double min, double max, double step, bool retune = true)
        { Number(section, label, delegate { return setting.Value; }, delegate(double v) { setting.Value = v; }, setting.Reset, min, max, step, retune); }
        private void Number(string section, string label, Func<double> read, Action<double> write,
            Action reset, double min, double max, double step, bool retune)
        {
            Rows.Add(new PanelControl { Section = section, Label = label, Kind = PanelControlKind.Number,
                Number = read, Write = write, ResetValue = reset, Min = min, Max = max, Step = step,
                Changed = retune ? _retune : null });
        }
    }
}
