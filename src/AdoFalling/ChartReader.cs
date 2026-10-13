using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AdoFalling8K
{
    /// <summary>
    /// Converts ADOFAI's own chart into a falling-note timeline.
    ///
    /// Source of truth (verified against the game assembly):
    ///   scrLevelMaker.instance.listFloors : List&lt;scrFloor&gt;
    ///   scrFloor.seqID      : int    - tile order
    ///   scrFloor.entryTime  : double - seconds (song clock) when the tile must be hit;
    ///                                 already includes BPM, speed, pause and hold events
    ///   scrFloor.entryangle : double - radians, direction the planet enters from
    ///   scrFloor.holdLength : int    - beats held (-1 = tap)
    ///   scrFloor.isFake     : bool   - decorative tile, not hittable
    /// </summary>
    internal sealed class ChartReader
    {
        private readonly List<NoteInstance> _notes = new List<NoteInstance>(8192);
        private int _lastCount = -1;
        private int _loggedFor = -1;
        private bool _statusFromPlay;
        private MacroPlan _lastPlan;
        private double _lastWindowMs;
        private FingeringMode _lastFingeringMode;
        private double _lastMinIntervalMs;
        private double _lastChordWindowMs;
        private double _lastFingeringBpm;
        private bool _lastFollowSpeed;
        private double _baseBeatSeconds;
        private string _fingering = "";
        public string FingeringReference { get; private set; }

        /// <summary>_lastCount marker that means "these notes came from a chart file".</summary>
        private const int FileChartSentinel = -3;

        public List<NoteInstance> Notes { get { return _notes; } }
        public int Count { get { return _notes.Count; } }

        /// <summary>Number of hold notes in the current chart, for diagnostics.</summary>
        public int HoldCount { get; private set; }

        /// <summary>
        /// Earliest note time in the chart. The overlay uses this to work out how far back the
        /// display clock has to be held so the first note starts at the top of the screen:
        /// some charts start at a negative song time (their first tile is reached before the
        /// song position reaches zero).
        /// </summary>
        public double FirstHitTime
        {
            get
            {
                double best = double.NaN;
                for (int i = 0; i < _notes.Count; i++)
                    if (double.IsNaN(best) || _notes[i].HitTime < best)
                        best = _notes[i].HitTime;
                return best;
            }
        }
        public string Status = "no chart";
        public string ChartSignature = "";
        public string Diagnostic { get { return _lastDiag; } }
        private string _lastDiag = "";
        private float _nextWarn;

        // --- reflection cache -------------------------------------------------
        private static bool _probed;
        private static MemberInfo _levelMakerInstance;
        private static MemberInfo _listFloors;
        private static MemberInfo _floorSeqId;
        private static MemberInfo _floorEntryTime;
        private static MemberInfo _floorEntryAngle;
        private static MemberInfo _floorHoldLength;
        private static MemberInfo _floorIsFake;
        private static MemberInfo _floorIsAuto;
        private static MemberInfo _floorIsMidSpin;
        private static MemberInfo _floorExitAngle;
        private static MemberInfo _floorSpeed;
        private static MemberInfo _floorIsCcw;
        private static MemberInfo _floorNumPlanets;
        private static MemberInfo _floorHoldCompletion;

        public void Reset()
        {
            _notes.Clear();
            _lastCount = -1;
            _loggedFor = -1;
            _baseBeatSeconds = 0.0;
            FingeringReference = "";
        }

        /// <summary>
        /// Drop the current timeline so the next Refresh rebuilds it from scratch. Used when the
        /// level restarts or resumes from a checkpoint: the rebuilt notes are all unconsumed, so
        /// the section ahead shows up again.
        /// </summary>
        public void Rebuild()
        {
            Reset();
            HoldCount = 0;
            _fingering = "";
        }

        /// <summary>Rebuild the timeline from the live chart. Cheap when nothing changed.</summary>
        public void Refresh(GameClock st)
        {
            FallingConfig c = FallingPlugin.Cfg;

            // Re-plan when the panel changes fingering protection. Preserve consumed state
            // and the timing anchor; tuning must not bring already-hit notes back.
            if (MacroPlan.Current != _lastPlan
                || c.FingeringMode.Value != _lastFingeringMode
                || c.FingeringWindowMs.Value != _lastWindowMs
                || c.FingeringMinIntervalMs.Value != _lastMinIntervalMs
                || c.FingeringBpm.Value != _lastFingeringBpm || c.FingeringFollowSpeed.Value != _lastFollowSpeed)
            {
                if (_notes.Count > 0)
                    ReassignLanes(c);
                _lastPlan = MacroPlan.Current;
                _lastFingeringMode = c.FingeringMode.Value;
                _lastWindowMs = c.FingeringWindowMs.Value;
                _lastMinIntervalMs = c.FingeringMinIntervalMs.Value;
                _lastFingeringBpm = c.FingeringBpm.Value;
                _lastFollowSpeed = c.FingeringFollowSpeed.Value;
            }

            // Changing visual recognition must never invoke the fingering allocator.
            if (c.ChordWindowMs.Value != _lastChordWindowMs)
            {
                UpdateChordVisuals(c);
                _lastChordWindowMs = c.ChordWindowMs.Value;
            }

            if (!st.Playing)
            {
                if (_notes.Count > 0)
                    Reset();
                return;
            }

            if (st.StateName == "DemoMode")
            {
                BuildDemoChart(st);
                return;
            }

            // File mode: the chart comes from the .adofai file loaded with the load key,
            // not from the live level. The notes and their times are the macro's own plan.
            if (MacroPlan.Active && MacroPlan.Current != null)
            {
                BuildFileNotes(st);
                return;
            }

            // A chart file is configured but the reader has not been started yet: wait for
            // the load key instead of showing the live chart, so F really "starts reading".
            if (!string.IsNullOrEmpty(c.FilePath.Value))
            {
                if (_notes.Count > 0)
                    Reset();
                Status = "file chart ready - start from the panel";
                return;
            }

            if (st.Playing)
                _statusFromPlay = true;

            IList floors = GetFloors();
            if (floors == null)
            {
                if (_statusFromPlay)
                    Status = "scrLevelMaker.listFloors unreachable";
                // Warn at most every few seconds: this is retried every frame and would
                // otherwise flood the log (tens of thousands of identical lines).
                if (UnityEngine.Time.unscaledTime > _nextWarn)
                {
                    _nextWarn = UnityEngine.Time.unscaledTime + 5f;
                    FallingPlugin.Log.LogWarning("chart not readable: " + _lastDiag);
                }
                return;
            }
            if (floors.Count == _lastCount && _notes.Count > 0)
                return;
            if (floors.Count == _lastCount && _lastCount > 0 && _loggedFor == floors.Count)
                return;   // already reported this (possibly empty) chart

            if (_notes.Count > 0)
                Reset();
            _lastCount = floors.Count;

            int skippedAuto = 0;
            int skippedMidSpin = 0;
            int merged = 0;
            int holds = 0;
            int releaseTiles = 0;
            double lastHitTime = double.NegativeInfinity;
            double baseBpm = st.Bpm > 0.0001 ? st.Bpm : 150.0;
            _baseBeatSeconds = 60.0 / baseBpm;

            // Hold durations straight from the game's own timeline.
            //
            // The game bakes a hold into the NEXT tile's entryTime:
            //     entryTime[i+1] = entryTime[i] + travelTime(i) + holdLength*2*halfTurn(i) + extraBeats
            // so the gap between consecutive entry times is the game's own answer for how long
            // the hold lasts. Deriving it from the angle formula instead can disagree wherever
            // angle/speed handling differs, so the gap wins and the formula only cross-checks.
            double[] entryTimes = new double[floors.Count];
            for (int k = 0; k < floors.Count; k++)
            {
                object fk = floors[k];
                entryTimes[k] = fk != null ? GameReflection.ToDouble(GameReflection.SafeRead(fk, _floorEntryTime)) : 0.0;
            }

            for (int i = 0; i < floors.Count; i++)
            {
                object f = floors[i];
                if (f == null)
                    continue;

                bool fake = GameReflection.ToBool(GameReflection.SafeRead(f, _floorIsFake));
                if (fake)
                    continue;

                // The game advances through auto tiles and midspins by itself
                // (scrPlayer.Simulated_PlayerControl_Update skips runs of midSpin tiles), so
                // they must not become notes of their own.
                if (c.SkipAutoTiles.Value)
                {
                    if (GameReflection.ToBool(GameReflection.SafeRead(f, _floorIsAuto)))
                    {
                        skippedAuto++;
                        continue;
                    }
                    if (GameReflection.ToBool(GameReflection.SafeRead(f, _floorIsMidSpin)))
                    {
                        skippedMidSpin++;
                        continue;
                    }
                }

                double t = GameReflection.ToDouble(GameReflection.SafeRead(f, _floorEntryTime));
                int seq = (int)GameReflection.ToDouble(GameReflection.SafeRead(f, _floorSeqId));

                // The tile right after a hold is consumed by the hold's RELEASE judgement:
                // scrPlayer.UpdateHoldBehavior calls Hit() on the hold tile itself when the key
                // comes up, and Hit() then walks onto this tile. Emitting a note for it would
                // show the same judgement twice (the hold's tail plus a stray single note), and
                // would ask for a press the chart never wants.
                if (ReleaseOfHold(floors, i, c))
                {
                    releaseTiles++;
                    continue;
                }

                // Keep the first tile when one press accounts for multiple same-beat tiles.
                if (c.MergeSameBeat.Value && _notes.Count > 0 && t - lastHitTime < c.SameBeatEpsilon.Value)
                {
                    merged++;
                    continue;
                }
                lastHitTime = t;

                NoteInstance n = default(NoteInstance);
                n.HitTime = t;
                double speed = GameReflection.ToDouble(GameReflection.SafeRead(f, _floorSpeed));
                n.BeatSeconds = 60.0 / baseBpm / (speed > 0.0 ? speed : 1.0);
                n.DistancePx = 0f;
                n.SeqId = seq;
                n.HoldSeconds = HoldSecondsFor(f, i, floors, entryTimes, baseBpm);
                n.Floor = f;
                if (n.HoldSeconds > 0.0)
                {
                    holds++;
                    if (holds <= 8)
                    {
                        double px = n.HoldSeconds * c.NoteSpeedPxPerSec.Value * (Screen.height / 1080f);
                        FallingPlugin.Log.LogInfo(string.Format(
                            "hold seq={0} entry={1:F3}s hold={2:F3}s ({3:F0}px) holdLength={4} speed={5}",
                            seq, t, n.HoldSeconds, px,
                            GameReflection.SafeRead(f, _floorHoldLength), GameReflection.SafeRead(f, _floorSpeed)));
                    }
                }
                n.Color = c.NoteColorValue;
                n.Active = true;
                n.Consumed = false;
                _notes.Add(n);
            }

            ReassignLanes(c);

            Status = string.Format(
                "{0} notes from {1} tiles (auto -{2}, midspin -{3}, same-beat -{4}, hold-release -{5}, holds {6})",
                _notes.Count, floors.Count, skippedAuto, skippedMidSpin, merged, releaseTiles, holds);
            if (_fingering.Length > 0)
                Status += ", " + _fingering;
            HoldCount = holds;
            _loggedFor = floors.Count;
            FallingPlugin.Log.LogInfo("chart read: " + Status);
            if (_notes.Count > 1)
                ChartSignature = string.Format("{0} notes, {1:F2}s..{2:F2}s",
                    _notes.Count, _notes[0].HitTime, _notes[_notes.Count - 1].HitTime);
        }

        /// <summary>Re-plan lanes without restoring consumed notes or changing the file anchor.</summary>
        private void ReassignLanes(FallingConfig c)
        {
            // ADOFAI authors also use SetSpeed to encode subdivisions of a steady song:
            // e.g. 180 BPM music with 360/720 BPM tiles. Those are not musical tempo changes.
            // Keep actual local tempo in NoteInstance for timing, but use the song's pulse
            // for hands unless the user explicitly chooses local tempo or a reference BPM.
            double referenceBeat = c.FingeringBpm.Value > 0.0 ? 60.0 / c.FingeringBpm.Value
                : c.FingeringFollowSpeed.Value ? 0.0 : _baseBeatSeconds;
            double minimum = FingeringPlanner.MinGroupIntervalSeconds(c.FingeringMinIntervalMs.Value);
            FingeringReference = referenceBeat > 0.0
                ? (60.0 / Math.Max(referenceBeat, minimum * 2.0)).ToString("0.##") + " BPM"
                : minimum > 0.0 ? "局部 BPM（最高 " + (30.0 / minimum).ToString("0.##") + "）" : "谱面局部 BPM";
            if (c.FingeringMode.Value == FingeringMode.FourKey) FingeringReference += " · 密集段按原 BPM";
            _fingering = "reference " + FingeringReference + ", "
                + FingeringPlanner.Assign(_notes, c.FingeringWindowMs.Value, referenceBeat,
                    c.FingeringMinIntervalMs.Value, c.FingeringMode.Value);
            UpdateChordVisuals(c);
        }

        private void UpdateChordVisuals(FallingConfig c)
        {
            ChordVisuals.Mark(_notes, c.ChordWindowMs.Value);
            for (int i = 0; i < _notes.Count; i++)
            {
                NoteInstance n = _notes[i];
                n.Color = c.NoteColorFor(n);
                _notes[i] = n;
            }
        }

        /// <summary>Synthetic 4/4 chart used by DemoMode.</summary>
        private void BuildDemoChart(GameClock st)
        {
            if (_notes.Count > 0 && _lastCount == -2)
                return;
            _notes.Clear();
            _lastCount = -2;
            FallingConfig c = FallingPlugin.Cfg;
            double beat = st.Crotchet > 0.0001 ? st.Crotchet : 0.4;
            _baseBeatSeconds = beat;
            for (int i = 0; i < 400; i++)
            {
                NoteInstance n = default(NoteInstance);
                n.HitTime = i * beat;
                n.BeatSeconds = beat;
                n.DistancePx = 0f;
                n.SeqId = -1;
                n.HoldSeconds = 0.0;
                // every 8th note is a hold, so the tail rendering is visible in DemoMode
                if (i % 8 == 4)
                    n.HoldSeconds = beat * 2.0;
                n.Color = c.NoteColorValue;
                n.Active = true;
                n.Consumed = false;
                _notes.Add(n);
            }
            ReassignLanes(c);
            Status = "demo chart: " + _notes.Count + " notes @" + st.Bpm.ToString("F0") + "bpm";
            ChartSignature = "demo, beat=" + beat.ToString("F3") + "s";
        }

        /// <summary>
        /// File mode: build the whole timeline from the loaded .adofai file. The notes and
        /// their timing uses the macro plan (press k is due at PressTimes[k]),
        /// anchored at the moment the load key was pressed. No live level data is used; a
        /// running level only provides the song clock (and, if readable, a one-time sync
        /// check that is written to the log).
        /// </summary>
        private void BuildFileNotes(GameClock st)
        {
            MacroPlan plan = MacroPlan.Current;
            if (plan == null || plan.PressCount == 0)
            {
                MacroPlan.Active = false;
                return;
            }

            FallingConfig c = FallingPlugin.Cfg;

            // A rewind means the level restarted: re-anchor to the current song time so the
            // aid still runs (start the file from the panel again for an exact start).
            double anchor = MacroPlan.AnchorSongTime;
            if (st.SongPosition < anchor - 1.0)
            {
                MacroPlan.AnchorSongTime = anchor = st.SongPosition;
                FallingPlugin.Log.LogInfo(string.Format(
                    "file chart: song rewound - re-anchored at {0:F3}s (restart from the panel for an exact start)",
                    anchor));
            }

            if (_notes.Count > 0 && _lastCount == FileChartSentinel)
                return;

            Reset();
            _lastCount = FileChartSentinel;
            _baseBeatSeconds = 60.0 / plan.LevelBpm;

            double offset = c.FileOffsetSeconds.Value;
            for (int k = 0; k < plan.PressCount; k++)
            {
                NoteInstance n = default(NoteInstance);
                n.HitTime = anchor + offset + plan.PressTimes[k];
                n.SeqId = plan.PressSeqIds[k];
                n.BeatSeconds = plan.PressBeatSeconds[k];
                n.HoldSeconds = 0.0;
                n.DistancePx = 0f;
                n.Color = c.NoteColorValue;
                n.Active = true;
                n.Consumed = false;
                _notes.Add(n);
            }

            HoldCount = 0;
            ReassignLanes(c);
            Status = string.Format("{0} notes from the chart file; {1}", _notes.Count, _fingering);
            FallingPlugin.Log.LogInfo(string.Format(
                "chart read (file): {0}; anchor {1:F3}s, offset {2:+0.000;-0.000;+0.000}s",
                Status, anchor, offset));

            // One-time cross-check: where does the live chart put its first press? If this
            // differs a lot from the anchor, the file was started at the wrong moment.
            IList liveFloors = GetFloors();
            if (liveFloors != null && liveFloors.Count > 1)
            {
                double liveFirst = GameReflection.ToDouble(GameReflection.SafeRead(liveFloors[1], _floorEntryTime));
                FallingPlugin.Log.LogInfo(string.Format(
                    "file chart sync check: live tile 1 at {0:F3}s, file anchor {1:F3}s (diff {2:+0.000;-0.000}s)",
                    liveFirst, anchor, liveFirst - anchor));
            }
        }

        /// <summary>
        /// True when this tile is the release half of the previous tile's hold, so it must not
        /// become a note of its own. scrPlayer judges the release on the hold tile itself.
        /// </summary>
        private static bool ReleaseOfHold(IList floors, int index, FallingConfig c)
        {
            if (!c.SkipHoldReleaseTiles.Value || index <= 0)
                return false;
            object prev = floors[index - 1];
            if (prev == null || _floorHoldLength == null)
                return false;
            try
            {
                object v = GameReflection.SafeRead(prev, _floorHoldLength);
                return v != null && Convert.ToInt32(v) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Live hold progress (0..1) for a note, or -1 when unavailable.</summary>
        internal static double ReadHoldCompletion(object floor)
        {
            if (!_probed)
                Probe();
            if (floor == null || _floorHoldCompletion == null)
                return -1.0;
            try
            {
                object v = GameReflection.SafeRead(floor, _floorHoldCompletion);
                return v != null ? Convert.ToDouble(v) : -1.0;
            }
            catch
            {
                return -1.0;
            }
        }

        /// <summary>Find the note belonging to a chart tile.</summary>
        internal bool TryGetBySeqId(int seqId, out NoteInstance note)
        {
            note = default(NoteInstance);
            if (seqId < 0)
                return false;
            for (int i = 0; i < _notes.Count; i++)
            {
                if (_notes[i].SeqId == seqId)
                {
                    note = _notes[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>Hide a note because the game registered a successful hit on it.</summary>
        internal void MarkConsumed(NoteInstance note)
        {
            note.Consumed = true;
            for (int i = 0; i < _notes.Count; i++)
            {
                NoteInstance n = _notes[i];
                if (n.SeqId >= 0 && note.SeqId >= 0)
                {
                    if (n.SeqId == note.SeqId)
                    {
                        n.Consumed = true;
                        _notes[i] = n;
                        return;
                    }
                }
                else if (Math.Abs(n.HitTime - note.HitTime) < 0.002)
                {
                    n.Consumed = true;
                    _notes[i] = n;
                    return;
                }
            }
        }

        /// <summary>
        /// Resolve a tile's hold into seconds.
        ///
        /// Preferred source is the game's own timeline: a hold of N beats is added to the next
        /// tile's entryTime, so <c>entryTime[i+1] - entryTime[i]</c> is exactly how long the tile
        /// occupies the song. The angle formula (holdLength * 2 * halfTurn) is used only when the
        /// gap is unavailable, and the two are compared so a disagreement is visible in the log.
        /// </summary>
        private double HoldSecondsFor(object floor, int index, IList floors, double[] entryTimes, double bpm)
        {
            if (_floorHoldLength == null)
                return 0.0;

            int hold = (int)GameReflection.ToDouble(GameReflection.SafeRead(floor, _floorHoldLength));
            if (hold < 0)
                return 0.0;          // -1 is "no hold"; 0 is a legal single-step hold

            double speed = GameReflection.ToDouble(GameReflection.SafeRead(floor, _floorSpeed));
            if (speed <= 0.0)
                speed = 1.0;

            // The game's own answer: the gap to the next tile already contains the hold.
            double fromGap = 0.0;
            if (index + 1 < entryTimes.Length && index < entryTimes.Length)
                fromGap = entryTimes[index + 1] - entryTimes[index];

            double fromFormula = FormulaHoldSeconds(floor, hold, speed, bpm);

            if (fromGap > 0.0)
            {
                if (Math.Abs(fromGap - fromFormula) > 0.02)
                {
                    FallingPlugin.Log.LogWarning(string.Format(
                        "hold duration mismatch: gap says {0:F3}s, angle formula says {1:F3}s, gap wins",
                        fromGap, fromFormula));
                }
                return fromGap;
            }
            return fromFormula;
        }

        /// <summary>holdLength * 2 * halfTurn, mirroring scrLevelMaker.</summary>
        private double FormulaHoldSeconds(object floor, int hold, double speed, double bpm)
        {
            if (hold <= 0)
                return 0.0;

            double entry = GameReflection.ToDouble(GameReflection.SafeRead(floor, _floorEntryAngle));
            double exit = GameReflection.ToDouble(GameReflection.SafeRead(floor, _floorExitAngle));
            double fwd = TimeBetweenAngles(0.0, 3.1415927410125732, speed, bpm, true);
            double back = TimeBetweenAngles(0.0, 3.1415927410125732, speed, bpm, false);
            double halfTurn = 0.5 * (fwd + back);
            if (halfTurn <= 0.0)
            {
                // fall back to the tile's own sweep when the half-turn degenerate case applies
                halfTurn = 0.5 * (TimeBetweenAngles(entry, exit, speed, bpm, true)
                                + TimeBetweenAngles(entry, exit, speed, bpm, false));
            }
            return hold * 2.0 * halfTurn;
        }

        /// <summary>scrMisc.GetTimeBetweenAngles, reimplemented from the game's source.</summary>
        private static double TimeBetweenAngles(double entry, double exit, double speed, double bpm, bool cw)
        {
            double dir = cw ? 1.0 : -1.0;
            double d = ScrMod((exit - entry) * dir, 6.2831854820251465);
            return d / 3.1415927410125732 * ((60.0 / bpm) / speed);
        }

        /// <summary>scrMisc.mod: a floored modulo that keeps the result non-negative.</summary>
        private static double ScrMod(double x, double m)
        {
            double r = x % m;
            if (r < 0.0)
                r += m;
            return r;
        }

        private IList GetFloors()
        {
            if (!_probed)
                Probe();
            object lm = GameReflection.Resolve(_levelMakerInstance);
            if (lm == null)
            {
                _lastDiag = "LevelMaker.instance null";
                return null;
            }
            object raw = GameReflection.SafeRead(lm, _listFloors);
            if (raw == null)
            {
                _lastDiag = "listFloors." + Name(_listFloors) + " returned null";
                return null;
            }
            var list = raw as IList;
            if (list == null)
            {
                _lastDiag = "listFloors is " + raw.GetType().FullName + " (not IList)";
                return null;
            }
            _lastDiag = "listFloors ok: " + list.Count;
            return list;
        }

        private static string Name(MemberInfo m)
        {
            return m == null ? "MISSING" : m.Name;
        }

        private static void Probe()
        {
            _probed = true;
            Assembly asm = GameReflection.Assembly;
            if (asm == null)
            {
                FallingPlugin.Log.LogError("Assembly-CSharp not loaded; cannot read the chart");
                return;
            }

            Type lmType = GameReflection.FindType(asm, "scrLevelMaker", "LevelMaker");
            Type floorType = GameReflection.FindType(asm, "scrFloor", "Floor");

            if (lmType != null)
            {
                _levelMakerInstance = GameReflection.FindMember(lmType, "instance");
                _listFloors = GameReflection.FindMember(lmType, "listFloors", "floors");
            }

            if (floorType != null)
            {
                _floorSeqId = GameReflection.FindMember(floorType, "seqID");
                _floorEntryTime = GameReflection.FindMember(floorType, "entryTime");
                _floorEntryAngle = GameReflection.FindMember(floorType, "entryangle", "entryAngle");
                _floorHoldLength = GameReflection.FindMember(floorType, "holdLength");
                _floorIsFake = GameReflection.FindMember(floorType, "isFake");
                _floorIsAuto = GameReflection.FindMember(floorType, "auto");
                _floorIsMidSpin = GameReflection.FindMember(floorType, "midSpin");
                _floorExitAngle = GameReflection.FindMember(floorType, "exitangle", "exitAngle");
                _floorSpeed = GameReflection.FindMember(floorType, "speed");
                _floorIsCcw = GameReflection.FindMember(floorType, "isCCW");
                _floorNumPlanets = GameReflection.FindMember(floorType, "numPlanets");
                _floorHoldCompletion = GameReflection.FindMember(floorType, "holdCompletion");
            }

            FallingPlugin.Log.LogInfo(string.Format(
                "ChartReader: levelMaker={0} listFloors={1} entryTime={2} entryangle={3} seqID={4}",
                lmType != null ? lmType.Name : "?",
                _listFloors != null ? _listFloors.Name : "MISSING",
                _floorEntryTime != null ? _floorEntryTime.Name : "MISSING",
                _floorEntryAngle != null ? _floorEntryAngle.Name : "MISSING",
                _floorSeqId != null ? _floorSeqId.Name : "MISSING"));
        }

    }

}
