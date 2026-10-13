using System;
using UnityEngine;

namespace AdoFalling8K
{
    /// <summary>
    /// Draws a falling-note ("下落式") rhythm overlay on top of the stock ADOFAI chart.
    ///
    /// Only the judgement line, the lanes and the falling notes are drawn; everything else
    /// stays transparent, so the original chart (planets, tiles, path) remains visible and
    /// playable underneath. The overlay is a pure visualisation: ADOFAI keeps handling
    /// input and judgement.
    /// </summary>
    public class FallingPlugin : MonoBehaviour
    {
        internal static ModLog Log;
        internal static FallingPlugin Instance;
        internal static FallingConfig Cfg;

        private FallingView _view;
        private ChartReader _chart;
        private FallingEngine _engine;
        private OverlayRenderer _renderer;
        private GameClock _clock;
        private readonly AutoPlayDriver _autoPlay = new AutoPlayDriver();
        private readonly ControlPanel _panel = new ControlPanel();
        private float _nextPoll;
        private float _nextWarn;
        private double _demoTime;
        private double _prevNow;
        private double _rawBeforeFrame;
        private string _lastAutoStatus = "";
        private bool _stopped;

        internal void Initialize()
        {
            Instance = this;
            Log.LogInfo(PluginInfo.Name + " " + PluginInfo.Version + " loading");
            Log.LogInfo(string.Format(
                "config: DebugHud={0} DemoMode={1} AutoPlay={2} CountdownLead={3} NoteSpeed={4} JudgeLineY={5} Fingering={6}",
                Cfg.DebugHud.Value, Cfg.DemoMode.Value, Cfg.AutoPlay.Value, Cfg.CountdownLead.Value,
                Cfg.NoteSpeedPxPerSec.Value, Cfg.JudgeLineY.Value, FingeringPlanner.ModeName(Cfg.FingeringMode.Value)));

            _view = new FallingView();
            _chart = new ChartReader();
            _clock = new GameClock();
            _engine = new FallingEngine(_view, _chart, Cfg);
            _renderer = new OverlayRenderer();
            _view.Build();
            _panel.Configure(Cfg, _engine, _clock, LoadChartFile, StopChartFile, LogAlignment);
        }

        private void Update()
        {
            if (_stopped) return;
            try
            {
                // Hotkey is polled every frame so it stays responsive.
                if (Input.GetKeyDown(KeyCode.F8))
                    _view.SetVisible(!_view.Visible);

                if (Input.GetKeyDown(KeyCode.F5)) TogglePanel();

                // Polling the game costs a few reflection reads, so it runs at a modest rate.
                if (Time.unscaledTime >= _nextPoll)
                {
                    _nextPoll = Time.unscaledTime + Cfg.PollInterval.Value;
                    bool playing = _clock.Poll();
                    _clock.DemoDriven = false;

                    // Leaving the level ends a file-chart run; start the file from the panel again next time.
                    if (!_clock.InLevelScene && MacroPlan.Active)
                    {
                        MacroPlan.Active = false;
                        Log.LogInfo("file chart: left the level - start from the panel again");
                    }
                    if (Cfg.DemoMode.Value && (!playing || Cfg.ForceDemo.Value))
                    {
                        _clock.DemoDriven = true;
                        StartDemoTick();
                    }
                    else
                    {
                        _demoTime = 0.0;
                    }

                    // Optional: let the game play itself (RDC.auto), which is also how the
                    // countdown timing can be observed without a human playing.
                    _autoPlay.Apply(Cfg.AutoPlay.Value && !_clock.DemoDriven);
                    if (Cfg.AutoPlay.Value && _autoPlay.Status != _lastAutoStatus)
                    {
                        _lastAutoStatus = _autoPlay.Status;
                        Log.LogInfo("auto-play: " + _autoPlay.Status
                            + (_autoPlay.Available ? "" : " (RDC.auto not found)"));
                    }
                }

                // ...but the overlay itself is advanced and drawn every single frame, which
                // is what keeps the fall smooth instead of stepping at the polling rate.
                _rawBeforeFrame = _clock.SongPosition;
                double now = _clock.AdvanceFrame();
                _clock.MeasuredDt = Time.unscaledDeltaTime;
                _clock.MeasuredStep = now - _prevNow;
                _prevNow = now;
                _chart.Refresh(_clock);
                _engine.Tick(_clock, Time.unscaledDeltaTime, _rawBeforeFrame);
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime > _nextWarn)
                {
                    _nextWarn = Time.unscaledTime + 5f;
                    Log.LogWarning("update: " + ex);
                }
            }
        }

        /// <summary>
        /// Load / refresh the .adofai chart file in Chart/FilePath and start file mode: the
        /// overlay's timing comes from the file and lanes use the shared hand-group planner.
        /// The first press of the file plan is
        /// due at the moment the panel load action runs.
        /// </summary>
        internal void LoadChartFile()
        {
            string path = Cfg.FilePath.Value;
            if (string.IsNullOrEmpty(path))
            {
                if (MacroPlan.Active)
                {
                    MacroPlan.Active = false;
                    _chart.Rebuild();
                    Log.LogInfo("file chart mode off (Chart/FilePath is empty)");
                }
                else
                {
                    Log.LogWarning("Chart/FilePath is empty");
                }
                return;
            }

            string message;
            MacroPlan plan = MacroPlan.TryLoad(path, out message);
            Log.LogInfo("chart file: " + message);
            if (plan == null)
                return;

            MacroPlan.Active = true;
            MacroPlan.AnchorSongTime = _clock.SongPosition;
            _view.SetVisible(true);   // starting the reader also shows the overlay
            _chart.Rebuild();         // drop the old notes so the file chart is built fresh
            Log.LogInfo(string.Format(
                "file chart started at {0:F3}s ({1} notes) - restart from the panel",
                MacroPlan.AnchorSongTime, plan.PressCount));
        }

        internal void StopChartFile()
        {
            MacroPlan.Active = false;
            _chart.Rebuild();
        }

        private void LogAlignment()
        {
            Log.LogInfo(string.Format("alignment: raw={0:F3}s displayed={1:F3}s lead={2:F3}s trim={3:F3}s hold={4:F3}s",
                _clock.SongPosition, _clock.SongTime, _clock.LeadOffset,
                Cfg.LeadOffsetManual.Value, Cfg.HoldOffsetSeconds.Value));
        }

        /// <summary>True while the control panel is open.</summary>
        internal bool Tuning { get { return _panel != null && _panel.Visible; } }

        internal void TogglePanel()
        {
            if (_stopped) return;
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible) _panel.SyncFromConfig(Cfg);
            else Main.SaveSettings();
        }

        internal void Shutdown()
        {
            if (_stopped) return;
            _stopped = true;
            if (_clock != null) _clock.DetachHitHook();
            _autoPlay.Apply(false);
            MacroPlan.Active = false;
            _panel.Visible = false;
            if (_renderer != null) _renderer.Dispose();
            _panel.Dispose();
            Main.SaveSettings();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        private void OnDisable() { Shutdown(); }
        private void OnDestroy() { Shutdown(); }
        private void OnApplicationQuit() { Shutdown(); }

        /// <summary>
        /// IMGUI draws over the finished frame, which is the only path that reliably
        /// composites a plugin's overlay in this game's render setup.
        /// </summary>
        private void OnGUI()
        {
            if (_stopped) return;
            try
            {
                // The control panel must stay reachable even while the overlay itself is hidden,
                // otherwise there would be no way to switch the overlay back on.
                if (_view != null && _view.Visible)
                    _renderer.Draw(_engine.Visible, _engine, _view, Cfg, _clock.SongTime);

                if (_panel.Visible)
                {
                    _panel.Draw(_engine, Cfg, _clock);
                }
                else if (_chart.Count == 0)
                {
                    // No chart parsed means we are on a menu or in a cutscene: the game reports
                    // PlayerControl on the title screen too, so the chart is the reliable signal
                    // for "actually playing a level".
                    DrawHint();
                }
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime > _nextWarn)
                {
                    _nextWarn = Time.unscaledTime + 5f;
                    Log.LogWarning("OnGUI: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// A short hint in the top-left corner of the game's menus, so the control panel is
        /// discoverable. It disappears as soon as a level is entered, where it would only be in
        /// the way.
        /// </summary>
        private void DrawHint()
        {
            GUIStyle style = _panel.TextStyle();
            float refScale = Screen.height / 1080f;
            style.fontSize = Mathf.RoundToInt(16f * refScale);
            style.alignment = TextAnchor.UpperLeft;

            string hint = "按 F5 调出控制面板，F8 显示／隐藏叠加层";
            Vector2 size = style.CalcSize(new GUIContent(hint));

            float pad = 8f * refScale;
            var box = new Rect(12f * refScale, 12f * refScale, size.x + pad * 2f, size.y + pad);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            Color prevText = style.normal.textColor;
            style.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
            GUI.Label(new Rect(box.x + pad, box.y + pad * 0.5f, size.x, size.y), hint, style);
            style.normal.textColor = prevText;
        }

        /// <summary>
        /// DemoMode: pretend a 4/4 chart is playing so the overlay can be previewed or
        /// tuned without loading a level.
        /// </summary>
        private void StartDemoTick()
        {
            _demoTime += Cfg.PollInterval.Value;
            _clock.Playing = true;
            _clock.StateName = "DemoMode";
            _clock.Bpm = Cfg.DemoBpm.Value;
            _clock.Crotchet = _clock.Bpm > 0.0 ? 60.0 / _clock.Bpm : 0.4;
            _clock.SeqId = (int)(_demoTime / _clock.Crotchet);
            _clock.ForceTime(_demoTime + 1.5);
        }
    }

    internal static class PluginInfo
    {
        public const string Name = "AdoFalling8K";
        public const string Version = "0.8";
    }
}
