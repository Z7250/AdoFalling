using System.Globalization;
using UnityEngine;

namespace AdoFalling8K
{
    /// <summary>All tunables exposed through the UMM settings file.</summary>
    internal sealed class FallingConfig
    {
        public readonly Setting<float> PollInterval;
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
        public readonly Setting<float> FJGap;
        public readonly Setting<double> FingeringWindowMs;
        public readonly Setting<FingeringMode> FingeringMode;
        public readonly Setting<double> FingeringMinIntervalMs;
        public readonly Setting<double> ChordWindowMs;
        public readonly Setting<double> FingeringBpm;
        public readonly Setting<bool> FingeringFollowSpeed;
        public readonly Setting<string> FilePath;
        public readonly Setting<double> FileOffsetSeconds;
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
        public readonly Setting<string> ChordColor;
        public readonly Setting<string>[] NoteNormalColors = new Setting<string>[8];
        public readonly Setting<string>[] NoteHighlightColors = new Setting<string>[8];
        public static readonly string[] NoteColorKeys = { "A", "S", "D", "F", "J", "K", "L", "Semicolon" };
        public const string DefaultHighlightColor = "#FFD966";
        public const string DefaultChordColor = "#ADD8E6";
        private static readonly Color HighlightFallback = ParseDefault(DefaultHighlightColor);
        private static readonly Color ChordFallback = ParseDefault(DefaultChordColor);
        private static Color ParseDefault(string text)
        { Color color; TryParseNoteColor(text, out color); return color; }
        private sealed class ColorCache
        {
            private bool _ready;
            private string _text;
            private Color _value;
            public Color Get(string text, Color fallback, string label)
            {
                if (_ready && _text == text) return _value;
                _ready = true; _text = text;
                if (!TryParseNoteColor(text, out _value))
                {
                    _value = fallback;
                    FallingPlugin.Log.LogWarning("Invalid " + label + "; using default colour");
                }
                return _value;
            }
        }
        private readonly ColorCache _globalColor = new ColorCache(), _chordColor = new ColorCache();
        private readonly ColorCache[] _laneColors = new ColorCache[16];
        public readonly Setting<float> LaneFlashStrength;
        public readonly Setting<float> JudgeFlashStrength;
        public Color NoteColorValue { get { return _globalColor.Get(NoteColor.Value, new Color(1, 0, 0, 1), "Style/NoteColor"); } }
        public Color ChordColorValue
        {
            get { return _chordColor.Get(ChordColor.Value, ChordFallback, "Style/ChordColor"); }
        }

        public Color NoteColorFor(NoteInstance note)
        {
            return note.IsChord ? ChordColorValue : NoteColorFor(note.Lane, note.IsGroupStart);
        }

        /// <summary>Resolve the logical key's normal or group-head colour.</summary>
        public Color NoteColorFor(int lane, bool groupStart)
        {
            if (lane < 0 || lane >= NoteColorKeys.Length) return NoteColorValue;
            int index = lane * 2 + (groupStart ? 1 : 0);
            Color fallback = groupStart ? HighlightFallback : NoteColorValue;
            if (_laneColors[index] == null) _laneColors[index] = new ColorCache();
            return _laneColors[index].Get((groupStart ? NoteHighlightColors[lane] : NoteNormalColors[lane]).Value,
                fallback, (groupStart ? "NoteColorsHighlight/" : "NoteColorsNormal/") + NoteColorKeys[lane]);
        }

        internal static string FormatNoteColor(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}",
                ColorByte(color.r), ColorByte(color.g), ColorByte(color.b), ColorByte(color.a));
        }

        private static int ColorByte(float value)
        {
            return (int)System.Math.Round(System.Math.Max(0.0, System.Math.Min(1.0, value)) * 255.0);
        }

        public FallingConfig(SettingsFile f)
        {
            f.Remove("Layout", "NoteRotation");
            f.Remove("Layout", "RightToLeft");
            f.Remove("Layout", "LaneMapping");
            f.Remove("Chart", "FingeringSixteenthProtection");
            foreach (string key in new[] { "ToggleKey", "LoadKey" }) f.Remove("General", key);
            foreach (string key in new[] { "TuneKey", "ControlPanelKey", "TuneEarlierKey", "TuneLaterKey", "HoldLaterKey", "HoldEarlierKey" })
                f.Remove("Tuning", key);
            f.Remove("Style", "NoteAltColor");
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
            FJGap = f.Bind("Layout", "FJGap", 0f);
            FingeringWindowMs = f.Bind("Chart", "FingeringWindowMs", 180.0);
            FingeringMode = f.Bind("Chart", "FingeringMode", AdoFalling8K.FingeringMode.Stable);
            FingeringMinIntervalMs = f.Bind("Chart", "FingeringMinIntervalMs", FingeringPlanner.DefaultMinGroupIntervalMs);
            ChordWindowMs = f.Bind("Chart", "ChordWindowMs", ChordVisuals.DefaultWindowMs);
            FingeringBpm = f.Bind("Chart", "FingeringBpm", 0.0);
            FingeringFollowSpeed = f.Bind("Chart", "FingeringFollowSpeed", false);
            FilePath = f.Bind("Chart", "FilePath", "");
            FileOffsetSeconds = f.Bind("Chart", "FileOffsetSeconds", 0.0);
            SkipAutoTiles = f.Bind("Chart", "SkipAutoTiles", true);
            MergeSameBeat = f.Bind("Chart", "MergeSameBeat", true);
            SameBeatEpsilon = f.Bind("Chart", "SameBeatEpsilon", 0.003);
            CountdownLead = f.Bind("Chart", "CountdownLead", true);
            LeadInSeconds = f.Bind("Chart", "LeadInSeconds", 0.35);
            LeadFraction = f.Bind("Chart", "LeadFraction", 0.0);
            LeadOffsetManual = f.Bind("Chart", "LeadOffsetSeconds", 0.07);
            ShowJudgeLine = f.Bind("View", "ShowJudgeLine", true);
            ShowNotes = f.Bind("View", "ShowNotes", true);
            ShowLanes = f.Bind("View", "ShowLanes", true);
            ShowHitEffects = f.Bind("View", "ShowHitEffects", true);
            ShowJudgeText = f.Bind("View", "ShowJudgeText", true);

            // Hold notes get their own trim so they can be lined up independently of the
            // regular notes.
            HoldOffsetSeconds = f.Bind("Tuning", "HoldOffsetSeconds", 0.0);
            HoldScale = f.Bind("Tuning", "HoldScale", 1.0);
            HoldShrinkWhileHeld = f.Bind("Tuning", "HoldShrinkWhileHeld", true);
            SkipHoldReleaseTiles = f.Bind("Chart", "SkipHoldReleaseTiles", true);

            LaneOpacity = f.Bind("Style", "LaneOpacity", 0.18f);
            LaneAlpha = f.Bind("Style", "LaneEdgeAlpha", 0.7f);
            JudgeOpacity = f.Bind("Style", "JudgeLineOpacity", 1f);
            NoteOpacity = f.Bind("Style", "NoteOpacity", 1f);
            HoldBodyOpacity = f.Bind("Style", "HoldBodyOpacity", 0.45f);
            LaneColor = f.Bind("Style", "LaneColor", new Color(0.05f, 0.06f, 0.09f, 1f));
            LaneEdgeColor = f.Bind("Style", "LaneEdgeColor", new Color(0.35f, 0.55f, 0.95f, 1f));
            JudgeColor = f.Bind("Style", "JudgeLineColor", new Color(1f, 1f, 1f, 1f));
            NoteColor = f.Bind("Style", "NoteColor", "255,0,0");
            ChordColor = f.Bind("Style", "ChordColor", DefaultChordColor);
            // Seed new normal palettes from the previous global colour on upgrade.
            string normal = FormatNoteColor(NoteColorValue);
            for (int lane = 0; lane < NoteColorKeys.Length; lane++)
            {
                NoteNormalColors[lane] = f.Bind("NoteColorsNormal", NoteColorKeys[lane], normal);
                NoteHighlightColors[lane] = f.Bind("NoteColorsHighlight", NoteColorKeys[lane], DefaultHighlightColor);
            }

            LaneFlashStrength = f.Bind("Feedback", "LaneFlashStrength", 0.7f);
            JudgeFlashStrength = f.Bind("Feedback", "JudgeFlashStrength", 0.85f);
        }

        /// <summary>Parses "R,G,B[,A]" (0-255) or "#RRGGBB[AA]" ("#" optional).</summary>
        internal static bool TryParseNoteColor(string text, out Color color)
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
