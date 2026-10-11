using System;
using System.Collections.Generic;
using System.Diagnostics;

// The planner depends only on NoteInstance's scalar fields. Keep this harness
// independent of a Unity installation while compiling the actual production files.
namespace UnityEngine { public struct Color { } }

namespace AdoFalling
{
    internal static class PlannerTests
    {
        private static int _passed;

        private static List<NoteInstance> Chart(double[] times, double beat)
        {
            var notes = new List<NoteInstance>();
            for (int i = 0; i < times.Length; i++)
            {
                NoteInstance n = new NoteInstance();
                n.HitTime = times[i]; n.BeatSeconds = beat; n.SeqId = i; n.Active = true;
                notes.Add(n);
            }
            return notes;
        }

        private static List<NoteInstance> Regular(int count, double gap, double offset)
        {
            double[] times = new double[count];
            for (int i = 0; i < count; i++) times[i] = offset + gap * i;
            return Chart(times, 0.5);
        }

        private static string Keys(List<NoteInstance> notes)
        {
            string keys = "";
            foreach (NoteInstance n in notes) keys += FingeringPlanner.Keys[n.Lane];
            return keys;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Expect(string name, List<NoteInstance> notes, string expected)
        {
            double[] times = new double[notes.Count];
            for (int i = 0; i < notes.Count; i++) times[i] = notes[i].HitTime;
            FingeringPlanner.Assign(notes, 180.0);
            string actual = Keys(notes);
            Check(actual == expected, name + ": expected " + expected + ", got " + actual);
            for (int i = 0; i < notes.Count; i++)
                Check(notes[i].HitTime == times[i] && notes[i].SeqId == i && notes[i].Active,
                    name + ": timing / identity changed");
            Console.WriteLine("PASS " + name + " -> " + actual);
            _passed++;
        }

        private static void NoHoldCollisions(List<NoteInstance> notes)
        {
            double[] until = new double[8];
            for (int lane = 0; lane < 8; lane++) until[lane] = double.NegativeInfinity;
            foreach (NoteInstance n in notes)
            {
                Check(n.Lane >= 0 && n.Lane < 8, "lane out of range");
                Check(until[n.Lane] <= n.HitTime + 0.000001, "held key reused at " + n.HitTime);
                until[n.Lane] = n.HitTime + n.HoldSeconds;
            }
        }

        private static void Main()
        {
            Expect("eighth index alternation", Regular(8, 0.25, 0), "FJFJFJFJ");
            Expect("eighth into sixteenth", Chart(new double[] { 0, .25, .5, .625, .75, .875, 1, 1.125 }, .5), "FJDFKJDF");
            Expect("sixteenth back to eighth", Chart(new double[] { 0, .125, .25, .375, .5, .75, 1 }, .5), "DFKJFJF");
            Expect("sixteenth", Regular(8, .125, 0), "DFKJDFKJ");
            Expect("twenty-fourth", Regular(12, 1.0 / 12.0, 0), "SDFLKJSDFLKJ");
            Expect("thirty-second", Regular(16, .0625, 0), "ASDF;LKJASDF;LKJ");
            Expect("continuous offbeat", Regular(8, .25, .125), "FJFJFJFJ");
            Expect("continuous phase shift", Chart(new double[] { 0, .25, .625, .875, 1.125, 1.375 }, .5), "FJFJFJ");
            Expect("offbeat rolls", Regular(8, .125, .125), "DFKJDFKJ");
            Expect("inserted offbeat", Chart(new double[] { 0, .125, .25, .5, .75 }, .5), "DFJFJ");
            Expect("near-boundary offbeat", Chart(new double[] { 0, .225, .25, .5, .75 }, .5), "FKJFJ");
            Expect("rest does not switch empty hands", Chart(new double[] { 0, .25, 1, 1.25 }, .5), "FJFJ");
            Expect("negative song time", Regular(8, .125, -1), "DFKJDFKJ");
            Expect("sixty-fourth capacity", Regular(16, .03125, 0), "ASDF;LKJASDF;LKJ");

            var tempo = Chart(new double[] { 0, .25, .5, .625, .75, .875 }, .5);
            for (int i = 2; i < tempo.Count; i++) { NoteInstance n = tempo[i]; n.BeatSeconds = .25; tempo[i] = n; }
            Expect("tempo change preserves eighth taps", tempo, "FJFJFJ");

            var fastTempo = Regular(8, .1, 0);
            for (int i = 0; i < fastTempo.Count; i++) { NoteInstance n = fastTempo[i]; n.BeatSeconds = .2; fastTempo[i] = n; }
            Expect("fast BPM eighths still use indexes", fastTempo, "FJFJFJFJ");

            var chord = Chart(new double[] { 0, 0, 0, 0, 0, 0, 0, 0 }, .5);
            FingeringPlanner.Assign(chord, 180);
            var used = new HashSet<int>();
            foreach (NoteInstance n in chord) Check(used.Add(n.Lane) && n.HitTime == 0, "simultaneous key reused");
            Console.WriteLine("PASS simultaneous notes retain time and distinct keys"); _passed++;

            var holds = Regular(16, .125, 0);
            NoteInstance hold = holds[1]; hold.HoldSeconds = 1.0; holds[1] = hold;
            FingeringPlanner.Assign(holds, 180);
            NoHoldCollisions(holds);
            Console.WriteLine("PASS held index avoids collisions -> " + Keys(holds)); _passed++;

            var random = new Random(7);
            var mixed = new List<NoteInstance>();
            double time = 0;
            for (int i = 0; i < 10000; i++)
            {
                NoteInstance n = new NoteInstance();
                n.HitTime = time; n.BeatSeconds = .5; n.SeqId = i;
                if (i % 173 == 0) n.HoldSeconds = .75;
                mixed.Add(n);
                time += new double[] { .25, .125, .0625, .0833333333, .02 }[random.Next(5)];
            }
            var timer = Stopwatch.StartNew();
            FingeringPlanner.Assign(mixed, 180);
            NoHoldCollisions(mixed);
            Console.WriteLine("PASS mixed 10000-note chart in " + timer.ElapsedMilliseconds + "ms"); _passed++;
            Console.WriteLine(_passed + " planner checks passed");
        }
    }
}
