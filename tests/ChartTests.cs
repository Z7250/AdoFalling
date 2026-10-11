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
    public struct Color { }
    public static class Screen { public static int height = 1080; public static int width = 1920; }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
}

namespace AdoFalling
{
    internal sealed class TestEntry<T> { public T Value; public TestEntry(T value) { Value = value; } }
    internal sealed class FallingConfig
    {
        public TestEntry<LaneMapping> LaneMapping = new TestEntry<LaneMapping>(AdoFalling.LaneMapping.Fingering);
        public TestEntry<double> FingeringWindowMs = new TestEntry<double>(180);
        public TestEntry<string> FilePath = new TestEntry<string>("");
        public TestEntry<string> LoadKey = new TestEntry<string>("F");
        public TestEntry<double> FileOffsetSeconds = new TestEntry<double>(.02);
        public TestEntry<bool> SkipAutoTiles = new TestEntry<bool>(true);
        public TestEntry<bool> SkipHoldReleaseTiles = new TestEntry<bool>(true);
        public TestEntry<bool> MergeSameBeat = new TestEntry<bool>(true);
        public TestEntry<double> SameBeatEpsilon = new TestEntry<double>(.003);
        public TestEntry<float> NoteSpeedPxPerSec = new TestEntry<float>(620);
        public TestEntry<float> LaneWidth = new TestEntry<float>(70);
        public TestEntry<float> LaneGap = new TestEntry<float>(8);
        public TestEntry<float> JudgeLineY = new TestEntry<float>(.18f);
        public TestEntry<bool> MirrorDirection = new TestEntry<bool>(false);
        public UnityEngine.Color NoteColorValue;
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
    }
    internal static class GameState
    {
        public static Type FindType(Assembly assembly, params string[] names) { return null; }
        public static MemberInfo FindMember(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                FieldInfo field = type.GetField(name);
                if (field != null) return field;
            }
            return null;
        }
        public static object Read(object target, MemberInfo member)
        { return member == null ? null : ((FieldInfo)member).GetValue(target); }
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
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
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

            reader.MarkConsumed(reader.Notes[0]);
            FallingPlugin.Cfg.FingeringWindowMs.Value = 60; reader.Refresh(clock);
            Check(reader.Notes[0].Consumed && Math.Abs(reader.Notes[0].HitTime - 10.02) < 1e-8,
                "protection tuning restored hit notes or moved anchor");
            FallingPlugin.Cfg.LaneMapping.Value = LaneMapping.Macro; reader.Refresh(clock);
            Check(Keys(reader.Notes) == "FJDFKJDF" && reader.Notes[0].Consumed, "legacy Macro config mismatch");
            Pass("tuning preserves consumed notes and legacy mapping");

            var speed = Load("speed", "{\"settings\":{\"bpm\":120},\"angleData\":[0,90,180,270,0,90],\"actions\":[{\"floor\":3,\"eventType\":\"SetSpeed\",\"speedType\":\"Bpm\",\"beatsPerMinute\":240}]}");
            Check(speed.PressBeatSeconds[1] == .5 && speed.PressBeatSeconds[2] == .25, "BPM event floor mismatch");
            Check(Math.Abs(speed.PressTimes[3] - .625) < 1e-8, "speed timing mismatch");
            reader.Rebuild(); reader.Refresh(clock); Check(Keys(reader.Notes) == "FJFJFJ", "speed became a roll");
            Pass("SetSpeed local tempo reaches file allocator");

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
            Check(Keys(reader.Notes) == "FJFJFJ" && reader.Notes[2].BeatSeconds == .25, "live speed metadata missing");
            Pass("live reader tempo matches file reader");

            var view = new FallingView();
            for (int i = 1; i < 8; i++) Check(view.LaneLeft(i) > view.LaneLeft(i - 1), "ASDFJKL; geometry is swapped");
            FallingPlugin.Cfg.MirrorDirection.Value = true; view.Layout(false);
            for (int i = 1; i < 8; i++) Check(view.LaneLeft(i) < view.LaneLeft(i - 1), "mirrored geometry mismatch");
            Pass("eight lanes and mirrored geometry");
            Console.WriteLine(_passed + " integration checks passed");
        }
    }
}
