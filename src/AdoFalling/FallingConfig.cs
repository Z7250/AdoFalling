using UnityEngine;

namespace AdoFalling
{
    /// <summary>All tunables exposed through the UMM settings file.</summary>
    internal sealed class FallingConfig
    {
        public readonly Setting<KeyCode> ToggleKey;
        public readonly Setting<float> PollInterval;
        public readonly Setting<KeyCode> LoadKey;
        public readonly Setting<bool> DebugHud;
        public readonly Setting<bool> DemoMode;
        public readonly Setting<double> DemoBpm;
        public readonly Setting<bool> ForceDemo;
        public readonly Setting<bool> DemoHits;
        public readonly Setting<bool> AutoPlay;

        public readonly Setting<float> JudgeLineY;
        public readonly Setting<float> NoteSpeedPxPerSec;
        public readonly Setting<float> NoteHeight;
        public readonly Setting<float> LaneWidth;
        public readonly Setting<float> LaneGap;
        public readonly Setting<bool> MirrorDirection;
        public readonly Setting<LaneMapping> LaneMapping;
        public readonly Setting<double> FingeringWindowMs;
        public readonly Setting<string> FilePath;
        public readonly Setting<double> FileOffsetSeconds;
        public readonly Setting<bool> NoteRotation;
        public readonly Setting<bool> SkipAutoTiles;
        public readonly Setting<bool> MergeSameBeat;
        public readonly Setting<double> SameBeatEpsilon;
        public readonly Setting<bool> CountdownLead;
        public readonly Setting<double> LeadInSeconds;
        public readonly Setting<double> LeadFraction;
        public readonly Setting<double> LeadOffsetManual;
        public readonly Setting<double> HoldOffsetSeconds;
        public readonly Setting<double> HoldScale;
        public readonly Setting<bool> HoldShrinkWhileHeld;
        public readonly Setting<bool> SkipHoldReleaseTiles;
        public readonly Setting<KeyCode> HoldLaterKey;
        public readonly Setting<KeyCode> HoldEarlierKey;
        public readonly Setting<KeyCode> TuneKey;
        public readonly Setting<KeyCode> TuneEarlierKey;
        public readonly Setting<KeyCode> TuneLaterKey;
        public readonly Setting<KeyCode> ControlPanelKey;
        public readonly Setting<bool> ShowJudgeLine;
        public readonly Setting<bool> ShowNotes;
        public readonly Setting<bool> ShowHitEffects;
        public readonly Setting<bool> ShowJudgeText;
        public readonly Setting<bool> ShowLanes;

        public readonly Setting<float> LaneOpacity;
        public readonly Setting<float> LaneAlpha;
        public readonly Setting<float> JudgeOpacity;
        public readonly Setting<float> NoteOpacity;
        public readonly Setting<float> HoldBodyOpacity;
        public readonly Setting<Color> LaneColor;
        public readonly Setting<Color> LaneEdgeColor;
        public readonly Setting<Color> JudgeColor;
        public readonly Setting<string> NoteColor;
        public readonly Setting<Color> NoteAltColor;
        private Color _noteColorCache = new Color(1f, 0f, 0f, 1f);
        private string _noteColorCacheText;

        /// <summary>
        /// Note colour parsed from the config text. Accepts RGB like "255,0,0", hex like
        /// "FF0000" / "#FF0000", optionally with a 4th alpha value ("255,0,0,128" or the
        /// 8-digit hex "FF000080"). Invalid text falls back to red.
        /// </summary>
        public Color NoteColorValue
        {
            get
            {
                string text = NoteColor.Value;
                if (text == _noteColorCacheText)
                    return _noteColorCache;
                _noteColorCacheText = text;
                Color parsed;
                if (TryParseNoteColor(text, out parsed))
                    _noteColorCache = parsed;
                else
                {
                    _noteColorCache = new Color(1f, 0f, 0f, 1f);
                    FallingPlugin.Log.LogWarning("NoteColor: cannot read \"" + text +
                        "\"; expected RGB like 255,0,0 or hex like FF0000. Using red.");
                }
                return _noteColorCache;
            }
        }
        public readonly Setting<float> LaneFlashStrength;
        public readonly Setting<float> JudgeFlashStrength;

        public FallingConfig(SettingsFile f)
        {
            ToggleKey = f.Bind("General", "ToggleKey", KeyCode.F8);
            LoadKey = f.Bind("General", "LoadKey", KeyCode.F4);
            // F used to activate file mode. It is now the left index lane: accepting
            // it here would restart the timeline on every normal index tap.
            switch (LoadKey.Value)
            {
                case KeyCode.A: case KeyCode.S: case KeyCode.D: case KeyCode.F:
                case KeyCode.J: case KeyCode.K: case KeyCode.L: case KeyCode.Semicolon:
                    LoadKey.Value = KeyCode.F4;
                    FallingPlugin.Log.LogInfo("LoadKey overlapped ASDFJKL; and was migrated to F4");
                    break;
            }
            PollInterval = f.Bind("General", "PollInterval", 0.05f);
            DebugHud = f.Bind("General", "DebugHud", false);
            DemoMode = f.Bind("General", "DemoMode", false);
            DemoBpm = f.Bind("General", "DemoBpm", 150.0);
            ForceDemo = f.Bind("General", "ForceDemo", false);
            DemoHits = f.Bind("General", "DemoHits", false);
            AutoPlay = f.Bind("General", "AutoPlay", false);

            JudgeLineY = f.Bind("Layout", "JudgeLineY", 0.18f);
            NoteSpeedPxPerSec = f.Bind("Layout", "NoteSpeed", 620f);
            NoteHeight = f.Bind("Layout", "NoteHeight", 26f);
            LaneWidth = f.Bind("Layout", "LaneWidth", 70f);
            LaneGap = f.Bind("Layout", "LaneGap", 8f);
            MirrorDirection = f.Bind("Layout", "RightToLeft", false);
            LaneMapping = f.Bind("Layout", "LaneMapping", AdoFalling.LaneMapping.Fingering);
            FingeringWindowMs = f.Bind("Chart", "FingeringWindowMs", 180.0);
            FilePath = f.Bind("Chart", "FilePath", "");
            FileOffsetSeconds = f.Bind("Chart", "FileOffsetSeconds", 0.0);
            NoteRotation = f.Bind("Layout", "NoteRotation", false);
            SkipAutoTiles = f.Bind("Chart", "SkipAutoTiles", true);
            MergeSameBeat = f.Bind("Chart", "MergeSameBeat", true);
            SameBeatEpsilon = f.Bind("Chart", "SameBeatEpsilon", 0.003);
            CountdownLead = f.Bind("Chart", "CountdownLead", true);
            LeadInSeconds = f.Bind("Chart", "LeadInSeconds", 0.35);
            LeadFraction = f.Bind("Chart", "LeadFraction", 0.0);
            LeadOffsetManual = f.Bind("Chart", "LeadOffsetSeconds", 0.07);
            TuneKey = f.Bind("Tuning", "TuneKey", KeyCode.F6);
            ControlPanelKey = f.Bind("Tuning", "ControlPanelKey", KeyCode.F5);
            ShowJudgeLine = f.Bind("View", "ShowJudgeLine", true);
            ShowNotes = f.Bind("View", "ShowNotes", true);
            ShowLanes = f.Bind("View", "ShowLanes", true);
            ShowHitEffects = f.Bind("View", "ShowHitEffects", true);
            ShowJudgeText = f.Bind("View", "ShowJudgeText", true);
            TuneEarlierKey = f.Bind("Tuning", "TuneEarlierKey", KeyCode.F7);
            TuneLaterKey = f.Bind("Tuning", "TuneLaterKey", KeyCode.F9);

            // Hold notes get their own trim so they can be lined up independently of the
            // regular notes.
            HoldOffsetSeconds = f.Bind("Tuning", "HoldOffsetSeconds", 0.0);
            HoldScale = f.Bind("Tuning", "HoldScale", 1.0);
            HoldShrinkWhileHeld = f.Bind("Tuning", "HoldShrinkWhileHeld", true);
            SkipHoldReleaseTiles = f.Bind("Chart", "SkipHoldReleaseTiles", true);
            HoldLaterKey = f.Bind("Tuning", "HoldLaterKey", KeyCode.Minus);
            HoldEarlierKey = f.Bind("Tuning", "HoldEarlierKey", KeyCode.Equals);

            LaneOpacity = f.Bind("Style", "LaneOpacity", 0.18f);
            LaneAlpha = f.Bind("Style", "LaneEdgeAlpha", 0.7f);
            JudgeOpacity = f.Bind("Style", "JudgeLineOpacity", 1f);
            NoteOpacity = f.Bind("Style", "NoteOpacity", 1f);
            HoldBodyOpacity = f.Bind("Style", "HoldBodyOpacity", 0.45f);
            LaneColor = f.Bind("Style", "LaneColor", new Color(0.05f, 0.06f, 0.09f, 1f));
            LaneEdgeColor = f.Bind("Style", "LaneEdgeColor", new Color(0.35f, 0.55f, 0.95f, 1f));
            JudgeColor = f.Bind("Style", "JudgeLineColor", new Color(1f, 1f, 1f, 1f));
            NoteColor = f.Bind("Style", "NoteColor", "255,0,0");
            NoteAltColor = f.Bind("Style", "NoteAltColor", new Color(0.4f, 0.7f, 1f, 1f));

            LaneFlashStrength = f.Bind("Feedback", "LaneFlashStrength", 0.7f);
            JudgeFlashStrength = f.Bind("Feedback", "JudgeFlashStrength", 0.85f);
        }

        /// <summary>Parses "R,G,B[,A]" (0-255) or "#RRGGBB[AA]" ("#" optional).</summary>
        private static bool TryParseNoteColor(string text, out Color color)
        {
            color = new Color(1f, 0f, 0f, 1f);
            if (string.IsNullOrEmpty(text))
                return false;
            text = text.Trim();
            if (text.Length == 0)
                return false;

            if (text.IndexOf(',') >= 0)
            {
                string[] parts = text.Split(',');
                if (parts.Length != 3 && parts.Length != 4)
                    return false;
                float[] v = new float[] { 0f, 0f, 0f, 1f };
                for (int i = 0; i < parts.Length; i++)
                {
                    int n;
                    if (!int.TryParse(parts[i].Trim(), out n) || n < 0 || n > 255)
                        return false;
                    v[i] = n / 255f;
                }
                color = new Color(v[0], v[1], v[2], v[3]);
                return true;
            }

            if (text[0] != '#')
                text = "#" + text;
            return ColorUtility.TryParseHtmlString(text, out color);
        }
    }
}
