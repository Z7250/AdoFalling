using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AdoFalling
{
    /// <summary>
    /// File chart timing adapted from ReADOFAIMacro: rotation angles -> Twirl / Pause /
    /// SetSpeed -> cumulative press times. Per-press local tempo is retained separately
    /// so live and file charts can use the same eight-key hand-group allocator.
    /// </summary>
    internal sealed class MacroPlan
    {
        public static MacroPlan Current;
        private static string _loadedPath = "";
        private static DateTime _loadedStamp;

        public string Status = "not loaded";
        public string FilePath = "";
        public int FloorCount;
        public int PlannedPresses;
        public double LevelBpm;

        /// <summary>Press times in seconds, measured from the first press (time 0).</summary>
        public double[] PressTimes;

        /// <summary>Local quarter-note duration for each press, before BPM normalisation.</summary>
        public double[] PressBeatSeconds;

        /// <summary>Chart tile (seqID) each press belongs to. The first press is tile 1:
        /// tile 0 is the starting tile the macro does not press (it counts the activation
        /// press, made by the player, as the first track of the fingering).</summary>
        public int[] PressSeqIds;

        /// <summary>File mode: the notes come from the chart file instead of the live level.
        /// Turned on by the load key; <see cref="AnchorSongTime"/> is the song time of the
        /// first press when no live level is readable to anchor against.</summary>
        public static bool Active;
        public static double AnchorSongTime;

        public int PressCount
        {
            get { return PressTimes != null ? PressTimes.Length : 0; }
        }

        private MacroPlan()
        {
        }

        /// <summary>
        /// Load (or reuse) the plan for a path. Re-reads the file when it changed.
        /// Returns the current plan or null; <paramref name="message"/> always describes what
        /// happened, for the log.
        /// </summary>
        public static MacroPlan TryLoad(string path, out string message)
        {
            if (string.IsNullOrEmpty(path))
            {
                message = "no chart file path set (Chart/FilePath is empty)";
                return Current;
            }

            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    message = "chart file not found: " + path;
                    return null;
                }

                if (Current != null && _loadedPath == path && _loadedStamp == info.LastWriteTimeUtc)
                {
                    message = "plan already loaded: " + Current.Status;
                    return Current;
                }

                MacroPlan plan = Parse(path);
                plan.FilePath = path;
                Current = plan;
                _loadedPath = path;
                _loadedStamp = info.LastWriteTimeUtc;
                message = plan.Status;
                return plan;
            }
            catch (Exception ex)
            {
                message = "chart file load failed: " + ex.Message;
                FallingPlugin.Log.LogWarning("macro plan: " + ex);
                return null;
            }
        }

        // ------------------------------------------------------------------ parsing

        private static MacroPlan Parse(string path)
        {
            EnsureJsonAssembly();

            string text = File.ReadAllText(path);
            text = Regex.Replace(text, @",\s*([}\]])", "$1");
            JObject root = JObject.Parse(text);

            MacroPlan plan = new MacroPlan();
            JToken settings = root["settings"];
            plan.LevelBpm = settings != null ? ToDouble(settings["bpm"], 150.0) : 150.0;
            if (plan.LevelBpm <= 0.0 || double.IsNaN(plan.LevelBpm) || double.IsInfinity(plan.LevelBpm))
                throw new FormatException("Chart BPM must be a positive finite number.");

            double[] angleData = ReadAngles(root);
            plan.FloorCount = angleData.Length + 1;

            List<double> rot = ComputeRotationAngles(angleData);

            List<string> actionTypes = new List<string>();
            List<int> actionFloors = new List<int>();
            List<double> actionA = new List<double>();
            List<double> actionB = new List<double>();
            ReadActions(root, actionTypes, actionFloors, actionA, actionB);

            ProcessTwirl(rot, angleData, actionTypes, actionFloors);
            ProcessPause(rot, angleData, actionTypes, actionFloors, actionA);
            double baseBpm = NormalizeBpm(plan.LevelBpm);
            ProcessSetSpeed(rot, angleData, actionTypes, actionFloors, actionA, actionB,
                baseBpm, plan.LevelBpm);

            RemoveMidSpins(rot, angleData);
            if (rot.Count > 0)
                rot.RemoveAt(0);            // the start tile's interval is not part of a press

            // The macro's own timeline: cumulative travel of the remaining intervals. Press 0
            // is at 0 and belongs to chart tile 1 (tile 0 is the starting tile), so tile k+1
            // gets press k. Computed before the sentinel is appended, exactly like the macro.
            double[] pressTimes = new double[rot.Count + 1];
            for (int i = 0; i < rot.Count; i++)
                pressTimes[i + 1] = pressTimes[i] + rot[i] / 180.0 * 60.0 / baseBpm;

            List<int> playable = new List<int>();
            for (int f = 0; f < plan.FloorCount; f++)
                if (!IsMidSpin(angleData, f))
                    playable.Add(f);

            double[] beatByFloor = ReadBeatSeconds(plan.FloorCount, plan.LevelBpm,
                actionTypes, actionFloors, actionA, actionB);
            int presses = Math.Min(Math.Max(0, playable.Count - 1), pressTimes.Length);
            plan.PlannedPresses = presses;
            plan.PressTimes = new double[presses];
            plan.PressSeqIds = new int[presses];
            plan.PressBeatSeconds = new double[presses];
            for (int k = 0; k < presses; k++)
            {
                plan.PressTimes[k] = pressTimes[k];
                int tile = k + 1;
                plan.PressSeqIds[k] = tile < playable.Count ? playable[tile] : -1;
                plan.PressBeatSeconds[k] = beatByFloor[plan.PressSeqIds[k]];
            }
            plan.Status = string.Format(
                CultureInfo.InvariantCulture,
                "{0} tiles, {1} presses, {2:F0} bpm (normalised {3:F0}), {4:F1}s",
                plan.FloorCount, plan.PlannedPresses, plan.LevelBpm, baseBpm,
                pressTimes.Length > 0 ? pressTimes[pressTimes.Length - 1] : 0.0);
            return plan;
        }

        private static double[] ReadBeatSeconds(int count, double bpm, List<string> types,
            List<int> floors, List<double> a, List<double> b)
        {
            var events = new List<int>[count];
            for (int i = 0; i < types.Count; i++)
            {
                int floor = floors[i];
                if (types[i] != "SetSpeed" || floor < 0 || floor >= count) continue;
                if (events[floor] == null) events[floor] = new List<int>();
                events[floor].Add(i);
            }
            double[] beats = new double[count];
            for (int floor = 0; floor < count; floor++)
            {
                if (events[floor] != null)
                    foreach (int index in events[floor])
                    {
                        double next = b[index] == 1.0 ? a[index] : bpm * a[index];
                        if (next <= 0.0 || double.IsNaN(next) || double.IsInfinity(next))
                            throw new FormatException("SetSpeed must produce a positive finite BPM.");
                        bpm = next;
                    }
                beats[floor] = 60.0 / bpm;
            }
            return beats;
        }

        private static void EnsureJsonAssembly()
        {
            try
            {
                Assembly.Load("Newtonsoft.Json");
            }
            catch
            {
                // already loaded by the game, or resolvable by the runtime
            }
        }

        private static double[] ReadAngles(JObject root)
        {
            JArray angleArray = root["angleData"] as JArray;
            if (angleArray != null)
            {
                double[] values = new double[angleArray.Count];
                for (int i = 0; i < angleArray.Count; i++)
                    values[i] = ToDouble(angleArray[i], 0.0);
                return values;
            }

            string pathData = root["pathData"] != null ? root["pathData"].ToString() : "";
            List<double> angles = new List<double>(pathData.Length);
            double previous = 0.0;
            for (int i = 0; i < pathData.Length; i++)
            {
                char ch = pathData[i];
                double a;
                if (!TryPathAngle(ch, ref previous, out a))
                    a = previous;
                angles.Add(a);
                previous = a;
            }
            return angles.ToArray();
        }

        private static bool TryPathAngle(char ch, ref double previous, out double angle)
        {
            switch (ch)
            {
                case 'R': angle = 0; return true;
                case 'p': angle = 15; return true;
                case 'J': angle = 30; return true;
                case 'E': angle = 45; return true;
                case 'T': angle = 60; return true;
                case 'o': angle = 75; return true;
                case 'U': angle = 90; return true;
                case 'q': angle = 105; return true;
                case 'G': angle = 120; return true;
                case 'Q': angle = 135; return true;
                case 'H': angle = 150; return true;
                case 'W': angle = 165; return true;
                case 'L': angle = 180; return true;
                case 'x': angle = 195; return true;
                case 'N': angle = 210; return true;
                case 'Z': angle = 225; return true;
                case 'F': angle = 240; return true;
                case 'V': angle = 255; return true;
                case 'D': angle = 270; return true;
                case 'Y': angle = 285; return true;
                case 'B': angle = 300; return true;
                case 'C': angle = 315; return true;
                case 'M': angle = 330; return true;
                case 'A': angle = 345; return true;
                case '!': angle = 999; return true;
                case '5': angle = previous + 72; return true;
                case '6': angle = previous - 72; return true;
                case '7': angle = previous + 52; return true;
                case '8': angle = previous - 52; return true;
                case '9': angle = previous - 30; return true;
                case 'h': angle = previous + 120; return true;
                case 'j': angle = previous - 120; return true;
                case 't': angle = previous + 60; return true;
                case 'y': angle = previous + 300; return true;
                default:
                    angle = previous;
                    return false;
            }
        }

        private static void ReadActions(JObject root, List<string> types, List<int> floors,
            List<double> a, List<double> b)
        {
            JArray actions = root["actions"] as JArray;
            if (actions == null)
                return;
            for (int i = 0; i < actions.Count; i++)
            {
                JObject action = actions[i] as JObject;
                if (action == null)
                    continue;
                string type = action["eventType"] != null ? action["eventType"].ToString() : "";
                if (type != "SetSpeed" && type != "Twirl" && type != "Pause")
                    continue;

                types.Add(type);
                floors.Add(ToInt(action["floor"], 0));

                double first = 0.0;
                double second = 0.0;
                if (type == "SetSpeed")
                {
                    bool byBpm = action["speedType"] != null
                                 && action["speedType"].ToString() == "Bpm";
                    first = byBpm
                        ? ToDouble(action["beatsPerMinute"], 0.0)
                        : ToDouble(action["bpmMultiplier"], 1.0);
                    second = byBpm ? 1.0 : 0.0;   // marker: 1 = "Bpm", 0 = "Multiplier"
                }
                else if (type == "Pause")
                {
                    first = ToDouble(action["duration"], 0.0);
                }
                a.Add(first);
                b.Add(second);
            }
        }

        // ------------------------------------------------------------------ the algorithm

        private static double AngleAt(double[] angleData, int floor)
        {
            return floor == 0 ? 0.0 : angleData[floor - 1];
        }

        private static bool IsMidSpin(double[] angleData, int floor)
        {
            return floor > 0 && floor <= angleData.Length && angleData[floor - 1] == 999.0;
        }

        private static double TrackDiff(double t1, double t2)
        {
            return Standardize(Standardize(t1 - 180.0) - t2);
        }

        private static double Standardize(double angle)
        {
            while (angle <= 0.0)
                angle += 360.0;
            while (angle > 360.0)
                angle -= 360.0;
            return angle;
        }

        private static List<double> ComputeRotationAngles(double[] angleData)
        {
            int floorCount = angleData.Length + 1;
            List<double> rot = new List<double>(floorCount);
            int index = 0;
            while (index < floorCount - 1)
            {
                double a1 = AngleAt(angleData, index);
                double a2 = AngleAt(angleData, index + 1);
                if (a2 == 999.0)
                {
                    if (index + 2 == floorCount)
                    {
                        rot.Add(999.0);
                        break;
                    }
                    a2 = AngleAt(angleData, index + 2);
                    rot.Add(999.0);
                    rot.Add(Standardize(a1 - a2));
                    index += 2;
                }
                else
                {
                    rot.Add(TrackDiff(a1, a2));
                    index++;
                }
            }
            return rot;
        }

        private static void ProcessTwirl(List<double> rot, double[] angleData,
            List<string> types, List<int> floors)
        {
            bool twirled = false;
            int lastFloor = 0;
            List<int> lo = new List<int>();
            List<int> hi = new List<int>();

            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] != "Twirl")
                    continue;
                if (twirled)
                {
                    lo.Add(lastFloor);
                    hi.Add(floors[i]);
                }
                twirled = !twirled;
                lastFloor = floors[i];
            }

            for (int r = 0; r < lo.Count; r++)
            {
                for (int floor = lo[r]; floor < hi[r]; floor++)
                {
                    if (floor < rot.Count && rot[floor] != 360.0 && !IsMidSpin(angleData, floor + 1))
                        rot[floor] = 360.0 - rot[floor];
                }
            }
        }

        private static void ProcessPause(List<double> rot, double[] angleData,
            List<string> types, List<int> floors, List<double> a)
        {
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] != "Pause")
                    continue;
                int floor = floors[i];
                if (floor < rot.Count && !IsMidSpin(angleData, floor + 1))
                    rot[floor] += a[i] * 180.0;
            }
        }

        private static void ProcessSetSpeed(List<double> rot, double[] angleData,
            List<string> types, List<int> floors, List<double> a, List<double> b,
            double baseBpm, double levelBpm)
        {
            // Rebuild the macro's speedList: ranges where one SetSpeed setting is in effect.
            List<int> lo = new List<int>();
            List<int> hi = new List<int>();
            List<double> bpm = new List<double>();
            List<double> mult = new List<double>();

            int floorCount = angleData.Length + 1;
            int lastFloor = 0;
            double lastBpm = levelBpm;
            double multiplier = 1.0;
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] != "SetSpeed")
                    continue;
                lo.Add(lastFloor);
                hi.Add(floors[i]);
                bpm.Add(lastBpm);
                mult.Add(multiplier);

                if (b[i] == 1.0)
                {
                    multiplier = a[i] / lastBpm;
                    lastBpm = a[i];
                }
                else
                {
                    multiplier = a[i];
                    lastBpm *= multiplier;
                }
                lastFloor = floors[i];
            }
            lo.Add(lastFloor);
            hi.Add(floorCount - 1);
            bpm.Add(lastBpm);
            mult.Add(multiplier);

            double globalMultiplier = baseBpm / levelBpm;
            for (int r = 0; r < lo.Count; r++)
            {
                for (int floor = lo[r]; floor < hi[r]; floor++)
                {
                    if (floor >= rot.Count || IsMidSpin(angleData, floor + 1))
                        continue;
                    double m = bpm[r] / levelBpm / globalMultiplier;
                    if (m != 0.0)
                        rot[floor] /= m;
                }
            }
        }

        private static void RemoveMidSpins(List<double> rot, double[] angleData)
        {
            for (int floor = rot.Count; floor > 0; floor--)
            {
                if (IsMidSpin(angleData, floor))
                {
                    int index = floor - 1;
                    if (index < rot.Count)
                        rot.RemoveAt(index);
                }
            }
        }

        private static double NormalizeBpm(double bpm)
        {
            while (bpm <= 550.0)
                bpm *= 2.0;
            while (bpm > 1100.0)
                bpm /= 2.0;
            if (bpm > 840.0)
                bpm /= 2.0;
            return bpm;
        }

        // ------------------------------------------------------------------ token helpers

        private static double ToDouble(JToken token, double fallback)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;
            try
            {
                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                    return (double)token;
                return Convert.ToDouble(token.ToString(), CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static int ToInt(JToken token, int fallback)
        {
            return (int)Math.Round(ToDouble(token, fallback));
        }
    }
}
