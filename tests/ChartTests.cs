using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// Minimal host boundary for exercising the real readers and screen geometry without
// launching Unity. ChartReader, MacroPlan, NoteInstance, FallingView and the allocator
// below are all compiled from src, not copied into the test harness.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float red, float green, float blue, float alpha) { r = red; g = green; b = blue; a = alpha; }
    }
    public static class Screen { public static int height = 1080; public static int width = 1920; }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
}

namespace AdoFalling8K
{
    internal sealed class TestEntry<T> { public T Value; public TestEntry(T value) { Value = value; } }
    internal sealed class FallingConfig
    {
        public TestEntry<double> FingeringWindowMs = new TestEntry<double>(180);
        public TestEntry<FingeringMode> FingeringMode = new TestEntry<FingeringMode>(AdoFalling8K.FingeringMode.Stable);
        public TestEntry<double> FingeringMinIntervalMs = new TestEntry<double>(FingeringPlanner.DefaultMinGroupIntervalMs);
        public TestEntry<double> ChordWindowMs = new TestEntry<double>(ChordVisuals.DefaultWindowMs);
        public TestEntry<double> FingeringBpm = new TestEntry<double>(0);
        public TestEntry<bool> FingeringFollowSpeed = new TestEntry<bool>(false);
        public TestEntry<string> FilePath = new TestEntry<string>("");
        public TestEntry<double> FileOffsetSeconds = new TestEntry<double>(.02);
        public TestEntry<bool> SkipAutoTiles = new TestEntry<bool>(true);
        public TestEntry<bool> SkipHoldReleaseTiles = new TestEntry<bool>(true);
        public TestEntry<bool> MergeSameBeat = new TestEntry<bool>(true);
        public TestEntry<double> SameBeatEpsilon = new TestEntry<double>(.003);
        public TestEntry<float> NoteSpeedPxPerSec = new TestEntry<float>(620);
        public TestEntry<float> LaneWidth = new TestEntry<float>(70);
        public TestEntry<float> LaneGap = new TestEntry<float>(8);
        public TestEntry<float> FJGap = new TestEntry<float>(0);
        public TestEntry<float> JudgeLineY = new TestEntry<float>(.18f);
        public TestEntry<bool> CountdownLead = new TestEntry<bool>(false);
        public TestEntry<bool> DebugHud = new TestEntry<bool>(false);
        public TestEntry<bool> DemoHits = new TestEntry<bool>(false);
        public TestEntry<bool> HoldShrinkWhileHeld = new TestEntry<bool>(true);
        public TestEntry<double> LeadFraction = new TestEntry<double>(0);
        public TestEntry<double> LeadInSeconds = new TestEntry<double>(0);
        public TestEntry<double> LeadOffsetManual = new TestEntry<double>(0);
        public TestEntry<double> HoldOffsetSeconds = new TestEntry<double>(0);
        public TestEntry<double> HoldScale = new TestEntry<double>(1);
        public UnityEngine.Color[] NormalColors = new UnityEngine.Color[8];
        public UnityEngine.Color[] HighlightColors = new UnityEngine.Color[8];
        public UnityEngine.Color NoteColorValue;
        public UnityEngine.Color ChordColorValue = new UnityEngine.Color(173f/255, 216f/255, 230f/255, 1);
        public UnityEngine.Color NoteColorFor(int lane, bool groupStart) { return (groupStart ? HighlightColors : NormalColors)[lane]; }
        public UnityEngine.Color NoteColorFor(NoteInstance note) { return note.IsChord ? ChordColorValue : NoteColorFor(note.Lane, note.IsGroupStart); }
    }
    internal sealed class LogSink
    {
        public void LogInfo(object text) { }
        public void LogWarning(object text) { }
        public void LogError(object text) { }
    }
    internal static class FallingPlugin
    {
        public static FallingConfig Cfg = new FallingConfig();
        public static LogSink Log = new LogSink();
    }
    internal sealed class GameClock
    {
        public bool Playing = true;
        public string StateName = "PlayerControl";
        public double Bpm = 120;
        public double Crotchet = .5;
        public double SongPosition = 10;
        public double SongTime = 10;
        public bool InLevelScene = true, DemoDriven, HitHookAttached = true;
        public double LeadOffset, LeadTarget, LeadRoom, MeasuredStep;
        public float MeasuredDt;
        public int SeqId, HitCount, HitSeqId = -1;
        public double HitAngle;
        public string HitGrade;
        public bool ConsumeHit() { return false; }
        public void ClearLead() { LeadOffset = 0; }
        public void SetLead(double first, double fall, double lead, double fraction, double trim) { }
        public void RegisterSyntheticHit(int seq, double angle, string grade) { }
    }
    internal sealed class TestFloor
    {
        public int seqID;
        public double entryTime;
        public double entryangle;
        public double exitangle = Math.PI;
        public int holdLength = -1;
        public double speed = 1;
        public bool isFake, auto, midSpin;
        public double holdCompletion;
    }
    internal sealed class TestLevel
    {
        public static TestLevel instance = new TestLevel();
        public IList listFloors = new List<TestFloor>();
    }

    internal static class ChartTests
    {
        private static int _passed;
        private static string _fixtures;
        private static void Check(bool ok, string message)
        {
            if (!ok) { Console.Error.WriteLine("FAIL " + message); throw new Exception(message); }
        }
        private static void Pass(string name) { _passed++; Console.WriteLine("PASS " + name); }
        private static string Keys(List<NoteInstance> notes)
        {
            string keys = "";
            foreach (NoteInstance n in notes) keys += FingeringPlanner.Keys[n.Lane];
            return keys;
        }
        private static MacroPlan Load(string name, string json)
        {
            string path = Path.Combine(_fixtures, name + ".adofai");
            File.WriteAllText(path, json);
            string message;
            MacroPlan plan = MacroPlan.TryLoad(path, out message);
            Check(plan != null, message);
            return plan;
        }
        private static List<TestFloor> FileFloors(MacroPlan plan, double anchor)
        {
            var floors = new List<TestFloor>();
            for (int i = 0; i < plan.PressCount; i++) floors.Add(new TestFloor {
                seqID = plan.PressSeqIds[i], entryTime = anchor + plan.PressTimes[i] });
            return floors;
        }
        private static void BindFloors()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            typeof(ChartReader).GetField("_probed", flags).SetValue(null, true);
            typeof(ChartReader).GetField("_levelMakerInstance", flags).SetValue(null, typeof(TestLevel).GetField("instance"));
            typeof(ChartReader).GetField("_listFloors", flags).SetValue(null, typeof(TestLevel).GetField("listFloors"));
            string[] members = { "SeqId", "EntryTime", "EntryAngle", "ExitAngle", "HoldLength", "Speed", "IsFake", "IsAuto", "IsMidSpin", "HoldCompletion" };
            string[] fields = { "seqID", "entryTime", "entryangle", "exitangle", "holdLength", "speed", "isFake", "auto", "midSpin", "holdCompletion" };
            for (int i = 0; i < members.Length; i++)
                typeof(ChartReader).GetField("_floor" + members[i], flags).SetValue(null, typeof(TestFloor).GetField(fields[i]));
        }

        private static void Main(string[] args)
        {
            _fixtures = args[0]; Directory.CreateDirectory(_fixtures); BindFloors();
            var mixed = Load("mixed", "{\"settings\":{\"bpm\":120},\"angleData\":[0,90,180,315,90,225,0,135],\"actions\":[]}");
            Check(mixed.PressCount == 8, "file lost presses");
            double[] expected = { 0, .25, .5, .625, .75, .875, 1, 1.125 };
            for (int i = 0; i < expected.Length; i++)
            {
                Check(Math.Abs(mixed.PressTimes[i] - expected[i]) < 1e-8, "file timing changed");
                Check(mixed.PressBeatSeconds[i] == .5, "normalised BPM leaked into grouping");
                Check(mixed.PressSeqIds[i] == i + 1, "file seq mismatch");
            }
            Pass("file timing and unnormalised local tempo");

            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            FallingPlugin.Cfg.FilePath.Value = "configured";
            var clock = new GameClock(); var reader = new ChartReader(); reader.Refresh(clock);
            Check(Keys(reader.Notes) == "FJDFKJDF", "file did not use hand groups");
            Check(Math.Abs(reader.Notes[0].HitTime - 10.02) < 1e-8, "file anchor/offset changed");
            Pass("file reader uses common allocator");
            CheckFrameColors(reader, clock);

            Check(GroupStarts(reader.Notes) == "11101010", "file group heads differ from planner rolls");
            FallingPlugin.Cfg.FingeringWindowMs.Value = 150; reader.Refresh(clock);
            Check(GroupStarts(reader.Notes) == "11101010", "fixed eight-key replanning lost group heads");
            Pass("file group heads survive fixed eight-key replanning");

            reader.MarkConsumed(reader.Notes[0]);
            FallingPlugin.Cfg.FingeringWindowMs.Value = 60; reader.Refresh(clock);
            Check(reader.Notes[0].Consumed && Math.Abs(reader.Notes[0].HitTime - 10.02) < 1e-8,
                "protection tuning restored hit notes or moved anchor");
            Check(reader.Notes[0].IsGroupStart && !reader.Notes[3].IsGroupStart, "consumption/tuning promoted a later note to group head");
            Check(Keys(reader.Notes) == "FJDFKJDF" && reader.Notes[0].Consumed, "fixed eight-key fingering changed");
            Pass("tuning preserves consumed notes and fixed eight-key fingering");

            var speed = Load("speed", "{\"settings\":{\"bpm\":120},\"angleData\":[0,90,180,270,0,90],\"actions\":[{\"floor\":3,\"eventType\":\"SetSpeed\",\"speedType\":\"Bpm\",\"beatsPerMinute\":240}]}");
            Check(speed.PressBeatSeconds[1] == .5 && speed.PressBeatSeconds[2] == .25, "BPM event floor mismatch");
            Check(Math.Abs(speed.PressTimes[3] - .625) < 1e-8, "speed timing mismatch");
            reader.Rebuild(); reader.Refresh(clock); Check(Keys(reader.Notes) == "FJDFKJ", "speed encoded sixteenths lost rolls");
            Check(reader.Notes[2].BeatSeconds == .25 && reader.FingeringReference == "120 BPM", "reference/local tempo mixed");
            Pass("SetSpeed subdivisions keep the song hand pulse");

            reader.MarkConsumed(reader.Notes[0]);
            FallingPlugin.Cfg.FingeringMinIntervalMs.Value = 0;
            FallingPlugin.Cfg.FingeringFollowSpeed.Value = true; reader.Refresh(clock);
            Check(Keys(reader.Notes) == "FJFJFJ" && reader.Notes[0].Consumed, "local tempo mode ignored or restored consumed note");
            Check(Math.Abs(reader.Notes[0].HitTime - 10.02) < 1e-8, "tempo mode moved the file anchor");
            Pass("explicit local tempo mode preserves genuine tempo changes");

            FallingPlugin.Cfg.FingeringBpm.Value = 120; reader.Refresh(clock);
            Check(Keys(reader.Notes) == "FJDFKJ" && reader.Notes[0].Consumed && reader.FingeringReference == "120 BPM", "manual reference failed");
            FallingPlugin.Cfg.FingeringBpm.Value = 0;
            FallingPlugin.Cfg.FingeringFollowSpeed.Value = false;
            FallingPlugin.Cfg.FingeringMinIntervalMs.Value = FingeringPlanner.DefaultMinGroupIntervalMs;
            Pass("manual musical BPM overrides local tempo without changing timing");

            var encoded = Load("speed-sixteenths", "{\"settings\":{\"bpm\":180},\"angleData\":[0,90,180,270,0,90,180,270],\"actions\":[{\"floor\":0,\"eventType\":\"SetSpeed\",\"speedType\":\"Bpm\",\"beatsPerMinute\":360}]}");
            reader.Rebuild(); reader.Refresh(clock);
            Check(Keys(reader.Notes) == "DFKJDFKJ", "360 BPM encoded sixteenths lost rolls");
            var encodedQuarter = Load("speed-quarters", "{\"settings\":{\"bpm\":180},\"angleData\":[0,0,0,0,0,0,0,0],\"actions\":[{\"floor\":0,\"eventType\":\"SetSpeed\",\"speedType\":\"Bpm\",\"beatsPerMinute\":720}]}");
            reader.Rebuild(); reader.Refresh(clock);
            Check(Keys(reader.Notes) == "DFKJDFKJ", "720 BPM encoded sixteenths lost rolls");
            for (int i = 0; i < encoded.PressCount; i++)
                Check(Math.Abs(encoded.PressTimes[i] - encodedQuarter.PressTimes[i]) < 1e-8, "equivalent chart timing differs");
            Pass("equivalent 360/720 BPM encodings produce the same sixteenth rolls");

            var multiplier = Load("multiplier", "{\"settings\":{\"bpm\":120},\"angleData\":[0,90,180,270],\"actions\":[{\"floor\":1,\"eventType\":\"SetSpeed\",\"speedType\":\"Multiplier\",\"bpmMultiplier\":2},{\"floor\":2,\"eventType\":\"SetSpeed\",\"speedType\":\"Multiplier\",\"bpmMultiplier\":0.5}]}");
            Check(multiplier.PressBeatSeconds[0] == .25 && multiplier.PressBeatSeconds[1] == .5, "multiplier tempo mismatch");
            Pass("multiplicative tempo metadata");

            var spin = Load("spin", "{\"settings\":{\"bpm\":120},\"angleData\":[0,999,90,180],\"actions\":[]}");
            Check(spin.PressCount == 3 && spin.PressSeqIds[1] == 3, "midspin alignment mismatch");
            Check(Math.Abs(spin.PressTimes[1] - .75) < 1e-8, "midspin timing changed");
            Pass("midspin keeps playable seq alignment");

            MacroPlan.Active = false; FallingPlugin.Cfg = new FallingConfig();
            var floors = new List<TestFloor>();
            for (int i = 0; i < 6; i++)
            {
                var f = new TestFloor(); f.seqID = i; f.entryTime = i < 3 ? i * .25 : .5 + (i - 2) * .125;
                f.speed = i < 2 ? 1 : 2; floors.Add(f);
            }
            TestLevel.instance.listFloors = floors;
            reader = new ChartReader(); reader.Refresh(clock);
            Check(Keys(reader.Notes) == "FJDFKJ" && reader.Notes[2].BeatSeconds == .25, "live speed encoded subdivisions lost rolls");
            Pass("live speed encoded subdivisions match file reader");

            Load("fast-eighths", "{\"settings\":{\"bpm\":600},\"angleData\":[0,90,180,270,0,90],\"actions\":[]}");
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            reader.Rebuild(); reader.Refresh(clock);
            Check(Keys(reader.Notes).Contains("D") && Keys(reader.Notes).Contains("K") && reader.FingeringReference == "180 BPM",
                "fast song bypassed absolute interval guard");
            Pass("fast song uses absolute interval guard instead of index-only taps");
            reader.MarkConsumed(reader.Notes[0]);
            double anchor = reader.Notes[0].HitTime;
            FallingPlugin.Cfg.FingeringMinIntervalMs.Value = 0; reader.Refresh(clock);
            Check(Keys(reader.Notes) == "FJFJFJ" && reader.FingeringReference == "600 BPM" && reader.Notes[0].Consumed
                && reader.Notes[0].HitTime == anchor, "guard tuning moved time or revived notes");
            FallingPlugin.Cfg.FingeringMinIntervalMs.Value = FingeringPlanner.DefaultMinGroupIntervalMs; reader.Refresh(clock);
            Check(reader.FingeringReference == "180 BPM" && reader.Notes[0].Consumed, "guard not restored");
            Pass("changing guard preserves consumed state and file anchor");
            MacroPlan.Active = false;

            var view = new FallingView();
            for (int i = 1; i < 8; i++) Check(view.LaneLeft(i) > view.LaneLeft(i - 1), "ASDFJKL; geometry is swapped");
            Pass("eight lanes use the fixed ASDFJKL; order");
            CheckCentralSpacing();
            Check320BpmGuard();
            CheckUniformReaders();
            CheckFingeringModes();
            CheckAggressiveFourKeyReaders();
            CheckFourKeySchemeReaders();
            CheckOrdinaryDenseNotes();
            CheckChordVisualRecognition();
            CheckChordVisualFingering();
            CheckChordVisualFileParity();
            CheckChordVisualHolds();
            CheckChordVisualMergePolicy();
            if (args.Length > 1) CheckReportedChart(args[1]);
            Console.WriteLine(_passed + " integration checks passed");
        }

        private static void CheckCentralSpacing()
        {
            FallingPlugin.Cfg = new FallingConfig();
            var cfg = FallingPlugin.Cfg;
            var view = new FallingView();
            float[] before = new float[8], expanded = new float[8];
            for (int i = 0; i < 8; i++) before[i] = view.LaneLeft(i);
            cfg.FJGap.Value = 180; view.Layout(false);
            for (int i = 0; i < 8; i++)
            {
                expanded[i] = view.LaneLeft(i);
                Check(Math.Abs(expanded[i] - before[i] - (i < 4 ? -90 : 90)) < .001,
                    "F/J separation did not move each hand symmetrically");
                if (i > 0 && i != 4)
                    Check(Math.Abs(expanded[i] - expanded[i - 1] - view.LaneWidthPx - 8) < .001,
                        "F/J separation altered within-hand spacing");
            }
            Check(Math.Abs(expanded[4] - expanded[3] - view.LaneWidthPx - 188) < .001
                && Math.Abs((expanded[0] + expanded[7] + view.LaneWidthPx) / 2 - UnityEngine.Screen.width / 2f) < .001,
                "F/J extra distance or overall centering is incorrect");

            UnityEngine.Screen.height = 720; UnityEngine.Screen.width = 1280; view.Layout(false);
            Check(Math.Abs(view.LaneLeft(4) - view.LaneLeft(3) - view.LaneWidthPx - 188 * 2f / 3) < .001,
                "central separation does not scale with screen height");
            cfg.FJGap.Value = 0; view.Layout(false);
            Check(Math.Abs(view.LaneLeft(4) - view.LaneLeft(3) - view.LaneWidthPx - 8 * 2f / 3) < .001,
                "zero central separation did not restore ordinary lane spacing");
            UnityEngine.Screen.height = 1080; UnityEngine.Screen.width = 1920;
            Pass("F/J extra spacing preserves centering, within-hand gaps, resolution scaling and zero default");
        }

        private static void CheckUniformReaders()
        {
            Load("uneven-rolls", "{\"settings\":{\"bpm\":120},\"angleData\":[0,162,313.2,115.2],\"actions\":[]}");
            MacroPlan plan = MacroPlan.Current;
            FallingPlugin.Cfg = new FallingConfig(); FallingPlugin.Cfg.FileOffsetSeconds.Value = 0;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock { SongTime = 9.9, Bpm = 120 };
            var file = new ChartReader(); file.Refresh(clock);
            Check(Keys(file.Notes) == "DFKJ" && GroupStarts(file.Notes) == "1010",
                "file reader kept unequal intervals in a long roll: " + Keys(file.Notes));
            var floors = FileFloors(plan, 10);
            MacroPlan.Active = false; TestLevel.instance.listFloors = floors;
            var live = new ChartReader(); live.Refresh(clock);
            Check(Keys(live.Notes) == Keys(file.Notes) && GroupStarts(live.Notes) == GroupStarts(file.Notes),
                "live/file unequal-interval allocation or group highlights differ");
            live.MarkConsumed(live.Notes[2]); FallingPlugin.Cfg.FingeringWindowMs.Value = 80; live.Refresh(clock);
            Check(live.Notes[2].Consumed && live.Notes[2].IsGroupStart && !live.Notes[3].IsGroupStart,
                "replanning unequal-interval rolls revived notes or changed the head highlight");
            for (int i = 0; i < live.Count; i++)
                Check(live.Notes[i].HitTime == 10 + plan.PressTimes[i] && live.Notes[i].SeqId == plan.PressSeqIds[i],
                    "unequal-interval allocation changed original note timing/identity");
            Pass("live/file readers split uneven long rolls into ordinary pairs with correct highlights and preserved timing/state");
        }

        private static void CheckFingeringModes()
        {
            Load("fingering-modes", "{\"settings\":{\"bpm\":120},\"angleData\":[0,150,300,90,240,30,180,330,120,270,60,210],\"actions\":[]}");
            MacroPlan plan = MacroPlan.Current;
            FallingPlugin.Cfg = new FallingConfig(); var cfg = FallingPlugin.Cfg;
            cfg.FileOffsetSeconds.Value = 0;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock { SongTime = 9.9, Bpm = 120 };
            var file = new ChartReader(); file.Refresh(clock);
            Check(Keys(file.Notes) == "SDFLKJSDFLKJ" && cfg.FingeringMode.Value == FingeringMode.Stable,
                "default stable mode changed the previous allocation");
            file.MarkConsumed(file.Notes[0]);
            cfg.FingeringMode.Value = FingeringMode.Aggressive; file.Refresh(clock);
            Check(Keys(file.Notes) == "ASDF;LKJASDF" && GroupStarts(file.Notes) == "100010001000" && file.Notes[0].Consumed,
                "aggressive mode did not expand file rolls or preserved stale heads/state");
            for (int i = 0; i < file.Count; i++) Check(file.Notes[i].HitTime == 10 + plan.PressTimes[i]
                && file.Notes[i].SeqId == plan.PressSeqIds[i], "mode switch changed file timing/identity");
            cfg.FingeringMode.Value = FingeringMode.Stable; file.Refresh(clock);
            Check(Keys(file.Notes) == "SDFLKJSDFLKJ" && GroupStarts(file.Notes) == "100100100100" && file.Notes[0].Consumed,
                "switching back did not restore stable rolls/highlights with judged notes intact");
            Pass("stable/aggressive mode switches expand file rolls and restore original groups without moving or reviving notes");

            var floors = FileFloors(plan, 10);
            MacroPlan.Active = false; TestLevel.instance.listFloors = floors;
            cfg.FingeringMode.Value = FingeringMode.Aggressive;
            var live = new ChartReader(); live.Refresh(clock);
            Check(Keys(live.Notes) == "ASDF;LKJASDF" && GroupStarts(live.Notes) == "100010001000",
                "live reader did not use the same aggressive allocation as the file");
            live.MarkConsumed(live.Notes[0]); cfg.FingeringMode.Value = FingeringMode.Stable; live.Refresh(clock);
            Check(Keys(live.Notes) == "SDFLKJSDFLKJ" && live.Notes[0].Consumed,
                "live mode switch changed timing/state instead of only lanes/groups");
            for (int i = 0; i < live.Count; i++) Check(live.Notes[i].HitTime == 10 + plan.PressTimes[i], "live mode switch moved a note");
            Pass("live/file readers share both fingering modes and preserve original timing and consumed state");
        }

        private static void CheckAggressiveFourKeyReaders()
        {
            Load("aggressive-four-key", "{\"settings\":{\"bpm\":120},\"angleData\":[0,135,270,45,180,315,90,225],\"actions\":[]}");
            MacroPlan plan = MacroPlan.Current;
            FallingPlugin.Cfg = new FallingConfig(); var cfg = FallingPlugin.Cfg;
            cfg.FileOffsetSeconds.Value = 0; cfg.FingeringMode.Value = FingeringMode.Aggressive;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock { SongTime = 9.9 };
            var file = new ChartReader(); file.Refresh(clock);
            Check(Keys(file.Notes) == "DFKJDFKJ" && GroupStarts(file.Notes) == "10101010",
                "file sixteenths did not use central four-key pairs");
            var floors = FileFloors(plan, 10);
            MacroPlan.Active = false; TestLevel.instance.listFloors = floors;
            var live = new ChartReader(); live.Refresh(clock);
            Check(Keys(live.Notes) == Keys(file.Notes) && GroupStarts(live.Notes) == GroupStarts(file.Notes),
                "live/file aggressive four-key preference differs");
            for (int i = 0; i < live.Count; i++) Check(live.Notes[i].HitTime == file.Notes[i].HitTime
                && live.Notes[i].SeqId == file.Notes[i].SeqId, "four-key allocation changed timing/identity");
            Pass("aggressive live/file sixteenths use alternating four-key pairs with original timing");

            TestLevel.instance.listFloors = new List<TestFloor> {
                new TestFloor { seqID = 0, entryTime = 0 }, new TestFloor { seqID = 1, entryTime = .01 },
                new TestFloor { seqID = 2, entryTime = .5 }, new TestFloor { seqID = 3, entryTime = .51 },
                new TestFloor { seqID = 4, entryTime = 1 }, new TestFloor { seqID = 5, entryTime = 1.125 }
            };
            cfg.ChordWindowMs.Value = 0; live = new ChartReader(); live.Refresh(clock);
            string keys = Keys(live.Notes), heads = GroupStarts(live.Notes);
            Check(keys == "DFKJDF", "aggressive allocation retained a side preference");
            live.MarkConsumed(live.Notes[0]); cfg.ChordWindowMs.Value = 25; live.Refresh(clock);
            Check(Keys(live.Notes) == keys && GroupStarts(live.Notes) == heads && live.Notes[0].Consumed
                && live.Notes[1].IsChord && live.Notes[1].DisplayHitTime == 0 && live.Notes[1].HitTime == .01,
                "aggressive chord visuals changed allocation or judged notes");
            cfg.ChordWindowMs.Value = 0; live.Refresh(clock);
            Check(Keys(live.Notes) == keys && GroupStarts(live.Notes) == heads && live.Notes[0].Consumed
                && !live.Notes[1].IsChord && live.Notes[1].DisplayHitTime == .01,
                "disabling chord visuals changed allocation/state or retained alignment");
            Pass("aggressive chord threshold changes only visuals and preserves alternating lanes/groups/state");
        }

        private static void CheckFourKeySchemeReaders()
        {
            Load("four-key-scheme", "{\"settings\":{\"bpm\":600},\"angleData\":[0,108,216,324,72,180,288,36],\"actions\":[]}");
            MacroPlan plan = MacroPlan.Current;
            FallingPlugin.Cfg = new FallingConfig(); var cfg = FallingPlugin.Cfg;
            cfg.FileOffsetSeconds.Value = 0; cfg.FingeringMode.Value = FingeringMode.Aggressive;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock { SongTime = 9.9, Bpm = 600, Crotchet = .1 };
            var file = new ChartReader(); file.Refresh(clock);
            Check(Keys(file.Notes) == "ASDF;LKJ", "file expansion fixture did not use the protected pulse");
            file.MarkConsumed(file.Notes[0]); cfg.FingeringMode.Value = FingeringMode.FourKey; file.Refresh(clock);
            Check(Keys(file.Notes) == "FJFJFJFJ" && GroupStarts(file.Notes) == "11111111" && file.Notes[0].Consumed,
                "4K mode did not replan the dense file span while preserving consumed state");
            for (int i = 0; i < file.Count; i++) Check(file.Notes[i].HitTime == 10 + plan.PressTimes[i]
                && file.Notes[i].SeqId == plan.PressSeqIds[i], "4K changed original file anchor or timing");
            var floors = FileFloors(plan, 10);
            MacroPlan.Active = false; TestLevel.instance.listFloors = floors;
            var live = new ChartReader(); live.Refresh(clock);
            Check(Keys(live.Notes) == Keys(file.Notes) && GroupStarts(live.Notes) == GroupStarts(file.Notes),
                "4K live/file allocations differ");
            live.MarkConsumed(live.Notes[0]); cfg.FingeringMode.Value = FingeringMode.Aggressive; live.Refresh(clock);
            Check(Keys(live.Notes) == "ASDF;LKJ" && live.Notes[0].Consumed, "leaving 4K mode failed to restore aggressive safely");
            Pass("4K live/file mode switches release dense-span protection and preserve timing/state");

            cfg.FingeringMode.Value = FingeringMode.FourKey; cfg.ChordWindowMs.Value = 0; live.Refresh(clock);
            string keys = Keys(live.Notes), heads = GroupStarts(live.Notes);
            cfg.ChordWindowMs.Value = 50; live.Refresh(clock);
            Check(Keys(live.Notes) == keys && GroupStarts(live.Notes) == heads && live.Notes[0].Consumed,
                "4K chord alignment changed the allocator or restored consumed notes");
            for (int i = 0; i < live.Count; i++) Check(live.Notes[i].IsChord && live.Notes[i].DisplayHitTime == 10
                && live.Notes[i].HitTime == 10 + plan.PressTimes[i], "4K chord display did not retain real timing");
            Pass("4K visual chord tuning changes only alignment and preserves generated lanes/groups/state");
        }

        private static void CheckOrdinaryDenseNotes()
        {
            FallingPlugin.Cfg = new FallingConfig();
            FallingPlugin.Cfg.ChordWindowMs.Value = 0;
            MacroPlan.Active = false;
            var clock = new GameClock { SongTime = -.1 };
            TestLevel.instance.listFloors = new List<TestFloor> {
                new TestFloor { seqID = 0, entryTime = 0 }, new TestFloor { seqID = 1, entryTime = .01 },
                new TestFloor { seqID = 2, entryTime = .02 }, new TestFloor { seqID = 3, entryTime = .25 }
            };
            var reader = new ChartReader(); reader.Refresh(clock);
            Check(Keys(reader.Notes) == "SDFJ" && GroupStarts(reader.Notes) == "1001",
                "dense notes did not use ordinary outside-to-inside rolls: " + Keys(reader.Notes));
            var engine = new FallingEngine(new FallingView(), reader, FallingPlugin.Cfg);
            engine.Tick(clock, .016f, clock.SongPosition);
            Check(engine.Visible.Count == 4 && engine.Visible[0].DistancePx < engine.Visible[1].DistancePx
                && engine.Visible[1].DistancePx < engine.Visible[2].DistancePx,
                "nearby notes were visually aligned instead of using original hit times");
            for (int i = 0; i < reader.Count; i++)
                Check(reader.Notes[i].SeqId == i && reader.Notes[i].HitTime == new double[] { 0, .01, .02, .25 }[i],
                    "ordinary dense allocation changed note timing/identity");
            TestLevel.instance.listFloors = new List<TestFloor> {
                new TestFloor { seqID = 0, entryTime = 0 }, new TestFloor { seqID = 1, entryTime = .001 },
                new TestFloor { seqID = 2, entryTime = .25 }
            };
            reader = new ChartReader(); reader.Refresh(clock);
            Check(reader.Count == 2 && reader.Notes[0].SeqId == 0 && reader.Notes[1].SeqId == 2,
                "same-beat merging did not restore its original first-tile policy");
            Pass("nearby notes retain ordinary rolls and original visual timing, with legacy same-beat merging");
        }

        private static void CheckChordVisualRecognition()
        {
            var notes = new List<NoteInstance>();
            double[] times = { -.5, -.475001, -.450001, -.424999 };
            for (int i = 0; i < times.Length; i++) notes.Add(new NoteInstance {
                HitTime = times[i], Lane = i, SeqId = 100 + i, HoldSeconds = i == 1 ? .4 : 0,
                IsGroupStart = i % 2 == 0, Consumed = i == 0, BeatSeconds = .5, Active = true });
            ChordVisuals.Mark(notes, 25);
            Check(notes[0].IsChord && notes[1].IsChord && !notes[2].IsChord && !notes[3].IsChord
                && notes[1].DisplayHitTime == times[0], "visual chord cutoff is not strict at 25ms");
            ChordVisuals.Mark(notes, 26);
            foreach (NoteInstance n in notes) Check(n.IsChord && n.DisplayHitTime == times[0], "adjacent chord chain was split at its anchor");
            ChordVisuals.Mark(notes, 0);
            for (int i = 0; i < notes.Count; i++) Check(!notes[i].IsChord && notes[i].DisplayHitTime == times[i]
                && notes[i].HitTime == times[i] && notes[i].Lane == i && notes[i].SeqId == 100 + i
                && notes[i].IsGroupStart == (i % 2 == 0) && notes[i].Consumed == (i == 0)
                && notes[i].Active && notes[i].BeatSeconds == .5 && notes[i].HoldSeconds == (i == 1 ? .4 : 0),
                "visual recognition altered generation, timing, hold or judgement metadata");
            notes.Clear();
            for (int i = 0; i < 12; i++) notes.Add(new NoteInstance { HitTime = i * .01, Lane = i % 8 });
            ChordVisuals.Mark(notes, 25);
            foreach (NoteInstance n in notes) Check(n.IsChord && n.DisplayHitTime == 0, "display-only chord was capped at four/eight keys");
            Pass("visual-only chords have a strict adjustable cutoff, transitive unlimited alignment and preserved note fields");
        }

        private static void CheckChordVisualFingering()
        {
            FallingPlugin.Cfg = new FallingConfig(); var cfg = FallingPlugin.Cfg;
            MacroPlan.Active = false;
            TestLevel.instance.listFloors = new List<TestFloor> {
                new TestFloor { seqID = 0, entryTime = 0 }, new TestFloor { seqID = 1, entryTime = .01 },
                new TestFloor { seqID = 2, entryTime = .02 }, new TestFloor { seqID = 3, entryTime = .25 }
            };
            for (int lane = 0; lane < 8; lane++) {
                cfg.NormalColors[lane] = new UnityEngine.Color(1, 0, 0, 1);
                cfg.HighlightColors[lane] = new UnityEngine.Color(1, 1, 0, 1);
            }
            var clock = new GameClock { SongTime = -.1 };
            cfg.ChordWindowMs.Value = 0;
                var reader = new ChartReader(); reader.Refresh(clock);
                var baseline = new List<NoteInstance>(reader.Notes);
                cfg.ChordWindowMs.Value = 25; reader.Refresh(clock);
                var engine = new FallingEngine(new FallingView(), reader, cfg);
                engine.Tick(clock, .016f, clock.SongPosition);
                Check(reader.Count == baseline.Count && engine.Visible.Count == 4, "visual recognition changed note count");
                for (int i = 0; i < reader.Count; i++) Check(reader.Notes[i].Lane == baseline[i].Lane
                    && reader.Notes[i].IsGroupStart == baseline[i].IsGroupStart && reader.Notes[i].HitTime == baseline[i].HitTime
                    && reader.Notes[i].SeqId == baseline[i].SeqId, "visual recognition changed fixed eight-key lanes/group/timing");
                for (int i = 0; i < 3; i++) Check(engine.Visible[i].Color.r == cfg.ChordColorValue.r
                    && engine.Visible[i].Color.g == cfg.ChordColorValue.g && engine.Visible[i].Color.b == cfg.ChordColorValue.b
                    && engine.Visible[i].DistancePx == engine.Visible[0].DistancePx, "chord colours/highlights or head alignment differ");
                Check(!reader.Notes[3].IsChord && engine.Visible[3].Color.r == 1, "ordinary note received chord colouring");
                cfg.ChordColorValue = new UnityEngine.Color(.1f, .6f, .8f, .5f);
                engine.Tick(clock, .016f, clock.SongPosition);
                Check(engine.Visible[0].Color.r == .1f && engine.Visible[1].Color.a == .5f, "chord colour did not update every frame");
                reader.MarkConsumed(reader.Notes[0]); cfg.ChordWindowMs.Value = 10; reader.Refresh(clock);
                Check(reader.Notes[0].Consumed, "visual threshold tuning revived a judged note");
                for (int i = 0; i < reader.Count; i++) Check(!reader.Notes[i].IsChord
                    && reader.Notes[i].DisplayHitTime == baseline[i].HitTime && reader.Notes[i].Lane == baseline[i].Lane
                    && reader.Notes[i].IsGroupStart == baseline[i].IsGroupStart, "tuning retained alignment or changed generation");
                cfg.ChordWindowMs.Value = 25; reader.Refresh(clock);
                Check(reader.Notes[0].Consumed && reader.Notes[1].DisplayHitTime == 0,
                    "hitting the first chord member promoted the next display anchor");
            Pass("fixed eight-key fingering keeps generated notes/lanes/groups while visual chords update safely");
        }

        private static void CheckChordVisualFileParity()
        {
            Load("visual-chord", "{\"settings\":{\"bpm\":120},\"angleData\":[0,172.8,345.6,158.4],\"actions\":[]}");
            MacroPlan plan = MacroPlan.Current;
            FallingPlugin.Cfg = new FallingConfig(); var cfg = FallingPlugin.Cfg;
            cfg.FileOffsetSeconds.Value = 0; cfg.ChordWindowMs.Value = 0;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock { SongTime = 9.9 };
            var file = new ChartReader(); file.Refresh(clock);
            string originalKeys = Keys(file.Notes), originalHeads = GroupStarts(file.Notes);
            cfg.ChordWindowMs.Value = 25; file.Refresh(clock);
            Check(Keys(file.Notes) == originalKeys && GroupStarts(file.Notes) == originalHeads, "file visual recognition changed fingering");
            var floors = new List<TestFloor>();
            for (int i = 0; i < plan.PressCount; i++) floors.Add(new TestFloor { seqID = plan.PressSeqIds[i], entryTime = 10 + plan.PressTimes[i] });
            MacroPlan.Active = false; TestLevel.instance.listFloors = floors;
            var live = new ChartReader(); live.Refresh(clock);
            Check(Keys(live.Notes) == originalKeys && GroupStarts(live.Notes) == originalHeads, "file/live visual chord changed lane parity");
            for (int i = 0; i < file.Count; i++) Check(file.Notes[i].IsChord && live.Notes[i].IsChord
                && file.Notes[i].DisplayHitTime == 10 && live.Notes[i].DisplayHitTime == 10
                && file.Notes[i].HitTime == 10 + plan.PressTimes[i] && live.Notes[i].HitTime == file.Notes[i].HitTime,
                "file/live visual alignment or original timing differs");
            Pass("file/live visual chords align identically and preserve the previous ordinary fingering");
        }

        private static void CheckChordVisualHolds()
        {
            FallingPlugin.Cfg = new FallingConfig(); var cfg = FallingPlugin.Cfg;
            MacroPlan.Active = false;
            TestLevel.instance.listFloors = new List<TestFloor> {
                new TestFloor { seqID = 0, entryTime = 0 }, new TestFloor { seqID = 1, entryTime = .01 },
                new TestFloor { seqID = 2, entryTime = .02 }, new TestFloor { seqID = 3, entryTime = .25 }
            };
            var clock = new GameClock { SongTime = -.1 };
            var reader = new ChartReader(); reader.Refresh(clock);
            NoteInstance hold = reader.Notes[1]; hold.HoldSeconds = .4; reader.Notes[1] = hold;
            var engine = new FallingEngine(new FallingView(), reader, cfg);
            engine.Tick(clock, .016f, clock.SongPosition);
            Check(engine.Visible[0].DistancePx == engine.Visible[1].DistancePx && engine.Visible[1].DistancePx == engine.Visible[2].DistancePx,
                "tap/hold chord heads are not aligned");
            clock.SongTime = .005; engine.Tick(clock, .016f, clock.SongPosition);
            Check(engine.Visible[0].DistancePx == engine.Visible[1].DistancePx
                && engine.Visible[1].Color.r == cfg.ChordColorValue.r, "hold entered the held phase before its original hit time");
            clock.SongTime = .455; engine.Tick(clock, .016f, clock.SongPosition);
            bool present = false;
            foreach (NoteInstance n in engine.Visible) if (n.SeqId == 1) present = true;
            Check(present && reader.Notes[1].HitTime == .01 && reader.Notes[1].HoldSeconds == .4,
                "display alignment shortened the real hold lifetime");
            clock.SongTime = .461; engine.Tick(clock, .016f, clock.SongPosition);
            foreach (NoteInstance n in engine.Visible) Check(n.SeqId != 1, "hold was not removed after its original release");
            Pass("aligned light-blue hold heads retain their real start, length and release lifetime");
        }

        private static void CheckChordVisualMergePolicy()
        {
            FallingPlugin.Cfg = new FallingConfig(); MacroPlan.Active = false;
            TestLevel.instance.listFloors = new List<TestFloor> {
                new TestFloor { seqID = 0, entryTime = 0 }, new TestFloor { seqID = 1, entryTime = .001 },
                new TestFloor { seqID = 2, entryTime = .25 }
            };
            var clock = new GameClock(); var reader = new ChartReader(); reader.Refresh(clock);
            Check(reader.Count == 2 && reader.Notes[0].SeqId == 0 && reader.Notes[1].SeqId == 2,
                "visual recognition bypassed the original same-beat merge policy");
            reader.MarkConsumed(reader.Notes[0]); FallingPlugin.Cfg.ChordWindowMs.Value = 0; reader.Refresh(clock);
            FallingPlugin.Cfg.ChordWindowMs.Value = 25; reader.Refresh(clock);
            Check(reader.Count == 2 && reader.Notes[0].Consumed && reader.Notes[1].SeqId == 2,
                "visual threshold toggles changed generation/merged presses");
            Pass("visual chord recognition leaves existing same-beat merging and consumed state intact");
        }

        private static void Check320BpmGuard()
        {
            Load("320-sixteenths", "{\"settings\":{\"bpm\":320},\"angleData\":[0,135,270,45,180,315,90,225,0,135,270,45],\"actions\":[]}");
            MacroPlan plan = MacroPlan.Current;
            FallingPlugin.Cfg = new FallingConfig(); FallingPlugin.Cfg.FileOffsetSeconds.Value = 0;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock { Bpm = 320 };
            var fileReader = new ChartReader(); fileReader.Refresh(clock);
            string expected = Keys(fileReader.Notes);
            Check(expected.Contains("D") && expected.Contains("K") && fileReader.FingeringReference == "180 BPM",
                "320 BPM sixteenths stayed on index lanes");
            for (int i = 1; i < plan.PressCount; i++)
                Check(Math.Abs(plan.PressTimes[i] - plan.PressTimes[i - 1] - 60.0 / 320 / 4) < 1e-8, "320 BPM fixture is not sixteenths");
            var floors = new List<TestFloor>();
            for (int i = 0; i < plan.PressCount; i++) floors.Add(new TestFloor { seqID = i + 1, entryTime = plan.PressTimes[i] });
            TestLevel.instance.listFloors = floors; MacroPlan.Active = false;
            var liveReader = new ChartReader(); liveReader.Refresh(clock);
            Check(Keys(liveReader.Notes) == expected && liveReader.FingeringReference == "180 BPM", "320 BPM live/file guard differs");
            for (int i = 0; i < plan.PressCount; i++)
                Check(liveReader.Notes[i].HitTime == plan.PressTimes[i] && !liveReader.Notes[i].Consumed, "guard altered timing/consumption");
            Pass("320 BPM sixteenths use protected live/file rolls: " + expected);
        }

        private static string GroupStarts(List<NoteInstance> notes)
        {
            string result = "";
            foreach (NoteInstance n in notes) result += n.IsGroupStart ? "1" : "0";
            return result;
        }

        private static void CheckFrameColors(ChartReader reader, GameClock clock)
        {
            FallingConfig cfg = FallingPlugin.Cfg;
            cfg.NormalColors[3] = new UnityEngine.Color(1, 0, 0, .5f);
            cfg.HighlightColors[3] = new UnityEngine.Color(1, 1, 0, .75f);
            cfg.NormalColors[2] = new UnityEngine.Color(0, 1, 0, .25f);
            cfg.HighlightColors[2] = new UnityEngine.Color(0, 1, 1, .9f);
            NoteInstance hold = reader.Notes[2]; hold.HoldSeconds = 1; reader.Notes[2] = hold;
            var engine = new FallingEngine(new FallingView(), reader, cfg);
            engine.Tick(clock, .016f, clock.SongPosition);
            Check(engine.Visible.Count == 8, "frame fixture lost visible notes");
            Check(engine.Visible[0].Color.g == 1 && engine.Visible[3].Color.g == 0, "highlight/normal frame colours not selected");
            Check(engine.Visible[2].Color.b == 1 && engine.Visible[2].Color.a == .9f, "hold head/body colour or alpha missing");
            cfg.HighlightColors[2] = new UnityEngine.Color(.2f, .3f, .4f, .6f);
            cfg.NormalColors[3] = new UnityEngine.Color(.6f, .5f, .4f, .3f);
            engine.Tick(clock, .016f, clock.SongPosition);
            Check(engine.Visible[2].Color.r == .2f && engine.Visible[2].Color.a == .6f && engine.Visible[2].HoldSeconds == 1,
                "visible hold did not update after palette edit/mirroring");
            Check(engine.Visible[3].Color.r == .6f && engine.Visible[3].Color.a == .3f && !engine.Visible[3].IsGroupStart,
                "visible tap did not update after palette edit");
            Check(!reader.Notes[2].Consumed && reader.Notes[2].HitTime == 10.52, "palette edits moved or consumed the hold");
            hold.HoldSeconds = 0; reader.Notes[2] = hold;
            Pass("frame colours follow lane/group palettes and update taps/holds immediately");
        }

        // Optional user-provided fixture: do not store the exported chart in the repository.
        private static void CheckReportedChart(string path)
        {
            string message;
            MacroPlan plan = MacroPlan.TryLoad(path, out message);
            Check(plan != null, message);
            Check(plan.LevelBpm == 180, "reported fixture must use 180 BPM music");
            FallingPlugin.Cfg = new FallingConfig();
            FallingPlugin.Cfg.FileOffsetSeconds.Value = 0;
            MacroPlan.Active = true; MacroPlan.AnchorSongTime = 10;
            var clock = new GameClock();
            var fileReader = new ChartReader(); fileReader.Refresh(clock);
            string dense = "";
            int denseStart = -1;
            for (int i = 0; i < fileReader.Count; i++)
            {
                NoteInstance n = fileReader.Notes[i];
                Check(Math.Abs(n.HitTime - 10 - plan.PressTimes[i]) < 1e-8, "reported chart timing changed");
                if (n.SeqId < 322 || n.SeqId > 336) continue;
                if (denseStart < 0) denseStart = i;
                Check(Math.Abs((plan.PressTimes[i] - plan.PressTimes[i - 1]) - 1.0 / 12) < 1e-8, "reported dense passage differs");
                Check(n.Lane >= 2 && n.Lane <= 5, "unexpected finger in reported sixteenths");
                dense += FingeringPlanner.Keys[n.Lane];
            }
            Check(dense.Length == 15 && dense.Contains("D") && dense.Contains("K"), "reported sixteenths still only use F/J: " + dense);
            for (int i = denseStart + 1; i <= denseStart + 14; i++)
            {
                int a = fileReader.Notes[i - 1].Lane, b = fileReader.Notes[i].Lane;
                Check(a != b, "same track repeated in the dense passage");
                if (a / 4 == b / 4) Check(a == 2 && b == 3 || a == 5 && b == 4, "roll is not outside to inside");
                if (i >= denseStart + 3)
                    Check(fileReader.Notes[i].Lane == fileReader.Notes[i - 4].Lane, "dense passage lost the four-note cycle");
            }
            Pass("reported R chart uses middle/index rolls: " + dense);

            // Feed the same times and game-local speeds to the live reader.
            var floors = new List<TestFloor>();
            for (int i = 0; i < plan.PressCount; i++)
                floors.Add(new TestFloor { seqID = plan.PressSeqIds[i], entryTime = plan.PressTimes[i],
                    speed = (60.0 / plan.PressBeatSeconds[i]) / plan.LevelBpm });
            TestLevel.instance.listFloors = floors;
            MacroPlan.Active = false; clock.Bpm = plan.LevelBpm;
            var liveReader = new ChartReader(); liveReader.Refresh(clock);
            Check(Keys(liveReader.Notes) == Keys(fileReader.Notes), "reported live/file fingering differs");
            for (int i = 0; i < liveReader.Count; i++)
            {
                Check(liveReader.Notes[i].HitTime == plan.PressTimes[i], "reported live timings changed");
                Check(liveReader.Notes[i].IsGroupStart == fileReader.Notes[i].IsGroupStart, "reported live/file group heads differ");
            }
            Pass("reported chart live/file parity and preserved timings");
        }
    }
}
