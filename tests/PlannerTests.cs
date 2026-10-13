using System;
using System.Collections.Generic;
using System.Diagnostics;

// The planner depends only on NoteInstance's scalar fields. Keep this harness
// independent of a Unity installation while compiling the actual production files.
namespace UnityEngine { public struct Color { } }

namespace AdoFalling8K
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
            if (!condition) { Console.Error.WriteLine("FAIL " + message); throw new Exception(message); }
        }

        private static void Expect(string name, List<NoteInstance> notes, string expected,
            double minimumMs = FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode mode = FingeringMode.Stable,
            double chordWindowMs = 0.0)
        {
            double[] times = new double[notes.Count];
            for (int i = 0; i < notes.Count; i++) times[i] = notes[i].HitTime;
            ChordVisuals.Mark(notes, chordWindowMs);
            FingeringPlanner.Assign(notes, 180.0, 0.0, minimumMs, mode);
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

        private static void UniformLongRolls(List<NoteInstance> notes)
        {
            for (int start = 0; start < notes.Count; )
            {
                int end = start + 1;
                while (end < notes.Count && !notes[end].IsGroupStart) end++;
                if (end - start >= 3)
                {
                    double first = notes[start + 1].HitTime - notes[start].HitTime;
                    for (int i = start + 2; i < end; i++)
                        Check(Math.Abs(notes[i].HitTime - notes[i - 1].HitTime - first) <= .005000001,
                            "three/four-note roll contains an uneven interval at " + notes[i].HitTime);
                }
                start = end;
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

            foreach (int groupSize in new[] { 1, 2, 3, 4 })
            {
                var grouped = Regular(groupSize * 4, .25 / groupSize, 0);
                for (int i = 0; i < grouped.Count; i++) { NoteInstance n = grouped[i]; n.IsGroupStart = true; grouped[i] = n; }
                FingeringPlanner.Assign(grouped, 180);
                for (int i = 0; i < grouped.Count; i++)
                    Check(grouped[i].IsGroupStart == (i % groupSize == 0), "incorrect/stale group head for size " + groupSize);
            }
            Console.WriteLine("PASS singleton and two/three/four-finger group heads"); _passed++;

            var repeated = Regular(16, .00001, 0);
            FingeringPlanner.Assign(repeated, 180, 0, FingeringPlanner.DefaultMinGroupIntervalMs);
            bool sameHandHead = false;
            for (int i = 1; i < repeated.Count; i++)
                if (repeated[i].IsGroupStart && repeated[i].Lane / 4 == repeated[i - 1].Lane / 4) sameHandHead = true;
            Check(sameHandHead, "same-hand exception fixture no longer exercises a distinct same-hand group");
            Console.WriteLine("PASS same-hand exception still marks a new group head"); _passed++;

            var tempo = Chart(new double[] { 0, .25, .5, .625, .75, .875 }, .5);
            for (int i = 2; i < tempo.Count; i++) { NoteInstance n = tempo[i]; n.BeatSeconds = .25; tempo[i] = n; }
            Expect("unprotected local tempo change preserves eighth taps", tempo, "FJFJFJ", 0);

            var fastTempo = Regular(8, .1, 0);
            for (int i = 0; i < fastTempo.Count; i++) { NoteInstance n = fastTempo[i]; n.BeatSeconds = .2; fastTempo[i] = n; }
            Expect("guard can be disabled for fast musical eighths", fastTempo, "FJFJFJFJ", 0);

            Expect("180 BPM eighth benchmark stays on indexes", Chart(new double[] { 0, 1.0/6, 2.0/6, 3.0/6, 4.0/6, 5.0/6, 1, 7.0/6 }, 1.0/3), "FJFJFJFJ");
            Check320Protection();
            CheckEvenRolls();
            CheckAggressiveRolls();
            CheckAggressiveFourKeys();
            CheckFourKeyScheme();

            var simultaneous = Chart(new double[] { 0, 0, 0, 0, 0, 0, 0, 0 }, .5);
            FingeringPlanner.Assign(simultaneous, 180);
            var used = new HashSet<int>();
            foreach (NoteInstance n in simultaneous) Check(used.Add(n.Lane) && n.HitTime == 0, "simultaneous key reused");
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
            UniformLongRolls(mixed);
            Console.WriteLine("PASS mixed 10000-note chart in " + timer.ElapsedMilliseconds + "ms"); _passed++;
            timer.Restart();
            FingeringPlanner.Assign(mixed, 180, 0, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            NoHoldCollisions(mixed); UniformLongRolls(mixed);
            int aggressiveGroupSize = 0;
            for (int i = 0; i < mixed.Count; i++)
            {
                aggressiveGroupSize = mixed[i].IsGroupStart ? 1 : aggressiveGroupSize + 1;
                Check(aggressiveGroupSize <= 4, "aggressive mode exceeds hand capacity");
                if (i > 0 && !mixed[i].IsGroupStart)
                    Check(mixed[i].HitTime - mixed[i - 1].HitTime <= 1.0 / 6.0 + .000000001
                        && mixed[i].Lane / 4 == mixed[i - 1].Lane / 4,
                        "aggressive roll crossed a twelfth gap or changed hands internally");
            }
            Console.WriteLine("PASS aggressive mixed 10000-note chart preserves uniformity, twelfth boundaries, capacity and holds in " + timer.ElapsedMilliseconds + "ms"); _passed++;
            var fourKeyNotes = new List<NoteInstance>(mixed);
            FingeringPlanner.Assign(fourKeyNotes, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
            NoHoldCollisions(fourKeyNotes); UniformLongRolls(fourKeyNotes);
            for (int i = 0; i < fourKeyNotes.Count; i++) Check(fourKeyNotes[i].HitTime == mixed[i].HitTime
                && fourKeyNotes[i].SeqId == mixed[i].SeqId && fourKeyNotes[i].HoldSeconds == mixed[i].HoldSeconds,
                "4K scheme changed original note data");
            Console.WriteLine("PASS 4K mixed 10000-note chart retains holds, uniformity and timing"); _passed++;
            Console.WriteLine(_passed + " planner checks passed");
        }

        private static void CheckAggressiveRolls()
        {
            Expect("aggressive sixteenths prefer four central keys", Regular(8, .125, 0), "DFKJDFKJ", mode: FingeringMode.Aggressive);
            Expect("aggressive twenty-fourths may pack four fingers", Regular(12, 1.0/12, 0), "ASDF;LKJASDF", mode: FingeringMode.Aggressive);
            Expect("aggressive mode includes intervals exactly one twelfth", Regular(8, 1.0 / 6.0, 0), "DFKJDFKJ", mode: FingeringMode.Aggressive);
            Expect("aggressive mode splits intervals beyond one twelfth", Regular(8, 1.0 / 6.0 + .000001, 0), "FJFJFJFJ", mode: FingeringMode.Aggressive);
            Expect("aggressive eighth singles alternate indexes", Regular(8, .25, 0), "FJFJFJFJ", mode: FingeringMode.Aggressive);
            Expect("aggressive mode starts a new roll after a rest", Chart(new double[] { 0, .125, .25, .75, .875, 1 }, .5), "DFJDFJ", mode: FingeringMode.Aggressive);
            Expect("aggressive uneven phrase remains ordinary pairs", Chart(new double[] { 0, .05, .13, .18 }, .5), "DFKJ", mode: FingeringMode.Aggressive);
            foreach (double gap in new[] { .055, .065, .054999, .065001 })
            {
                var notes = Chart(new double[] { 0, .06, .06 + gap }, .5);
                FingeringPlanner.Assign(notes, 180, 0, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
                bool grouped = !notes[1].IsGroupStart && !notes[2].IsGroupStart;
                Check(grouped == (gap == .055 || gap == .065), "aggressive mode changed +/-5ms uniformity boundary");
                UniformLongRolls(notes);
            }
            Console.WriteLine("PASS aggressive long rolls retain the inclusive +/-5ms interval tolerance"); _passed++;
            var guarded = Regular(8, .1, 0);
            FingeringPlanner.Assign(guarded, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            Check(Keys(guarded) == "DFKJDFKJ", "aggressive mode bypassed the 180 BPM interval safeguard: " + Keys(guarded));
            FingeringPlanner.Assign(guarded, 180, .1, 0, FingeringMode.Aggressive);
            Check(Keys(guarded) == "FJFJFJFJ", "disabled safeguard did not use the faster musical twelfth boundary");
            Console.WriteLine("PASS aggressive twelfth cutoff follows explicit musical BPM and the configurable period lower bound"); _passed++;
        }

        private static void CheckAggressiveFourKeys()
        {
            Expect("aggressive intervals just slower than twenty-fourths stay on four keys",
                Regular(8, 1.0/12 + .000001, 0), "DFKJDFKJ", mode: FingeringMode.Aggressive);
            Expect("aggressive thirty-seconds expand to eight keys",
                Regular(8, .0625, 0), "ASDF;LKJ", mode: FingeringMode.Aggressive);
            Expect("aggressive slower outlier leaves a fast roll",
                Chart(new double[] { 0, .08, .16, .244 }, .5), "SDFJ", mode: FingeringMode.Aggressive);
            var guarded = Regular(8, 1.0/18, 0);
            FingeringPlanner.Assign(guarded, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            Check(Keys(guarded) == "ASDF;LKJ", "protected twenty-fourth boundary did not allow outer lanes");
            FingeringPlanner.Assign(guarded, 180, .1, 0, FingeringMode.Aggressive);
            Check(Keys(guarded) == "FJFJFJFJ", "disabled guard retained a protected density threshold");
            Console.WriteLine("PASS aggressive density expansion uses the protected musical reference and configurable lower bound"); _passed++;

            var notes = Chart(new double[] { 0, .01, .5, .51, 1, 1.125 }, .5);
            FingeringPlanner.Assign(notes, 180, 0, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            string baseline = Keys(notes);
            Check(baseline == "DFKJDF", "aggressive mode retained a left/right pattern bias");
            ChordVisuals.Mark(notes, 25);
            FingeringPlanner.Assign(notes, 180, 0, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            Check(Keys(notes) == baseline && notes[1].DisplayHitTime == 0, "visual chords still affect aggressive allocation");
            Console.WriteLine("PASS aggressive allocation alternates hands and ignores visual chord markers"); _passed++;

            var holds = Regular(12, .125, 0);
            for (int i = 0; i < 4; i++) { NoteInstance n = holds[i]; n.HoldSeconds = 1; holds[i] = n; }
            FingeringPlanner.Assign(holds, 180, 0, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            NoHoldCollisions(holds);
            bool outer = false;
            foreach (NoteInstance n in holds) outer |= n.Lane < 2 || n.Lane > 5;
            Check(outer, "four-key preference prevented falling back around held keys");
            Console.WriteLine("PASS aggressive four-key preference still avoids held keys using outer lanes"); _passed++;
        }

        private static void CheckFourKeyScheme()
        {
            Expect("4K ordinary sixteenths retain aggressive eight-note protection", Regular(8, .125, 0), "DFKJDFKJ", mode: FingeringMode.FourKey);
            foreach (double gap in new[] { .25, .125, .0833333333333333, .0625 })
            {
                var aggressive = Regular(8, gap, 0); var four = Regular(8, gap, 0);
                FingeringPlanner.Assign(aggressive, 180, .5, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
                FingeringPlanner.Assign(four, 180, .5, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
                Check(Keys(four) == Keys(aggressive), "4K altered a musical pulse already above the guard");
            }
            Console.WriteLine("PASS 4K matches aggressive when the absolute safeguard is inactive"); _passed++;
            var slow = Regular(8, .08, 0);
            FingeringPlanner.Assign(slow, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
            Check(Keys(slow) == "DFKJDFKJ", "4K removed protection before an expansion trigger");
            Console.WriteLine("PASS 4K keeps high-BPM ordinary pairs protected"); _passed++;

            var dense = Regular(8, .04, 0);
            FingeringPlanner.Assign(dense, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            Check(Keys(dense) == "ASDF;LKJ", "dense fixture no longer exercises protected expansion");
            FingeringPlanner.Assign(dense, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
            Check(Keys(dense) == "FJFJFJFJ", "4K did not release the lower bound inside the expansion span");
            Console.WriteLine("PASS 4K expansion span uses the original BPM instead of the protected lower bound"); _passed++;

            foreach (double gap in new[] { 1.0/18, 1.0/18 + .000001 })
            {
                var boundary = Regular(8, gap, 0);
                FingeringPlanner.Assign(boundary, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
                Check(Keys(boundary) == (gap == 1.0/18 ? "FJFJFJFJ" : "DFKJDFKJ"),
                    "4K shifted the original inclusive expansion trigger");
            }
            Console.WriteLine("PASS 4K releases protection at the original inclusive outer threshold"); _passed++;

            foreach (double[] times in new[] { new double[] { 0, .04, .25, .29 }, new double[] { 0, .01, .05 } })
            {
                var aggressive = Chart(times, .1); var four = Chart(times, .1);
                FingeringPlanner.Assign(aggressive, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
                FingeringPlanner.Assign(four, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
                Check(Keys(four) == Keys(aggressive), "isolated pair or uneven triple falsely released the guard");
            }
            Console.WriteLine("PASS 4K does not release protection for isolated fast pairs or uneven triples"); _passed++;

            var mixed = Chart(new double[] { 0, .08, .4, .44, .48, .52, .56, .6, .64, .68, 1, 1.08 }, .1);
            FingeringPlanner.Assign(mixed, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
            Check(!mixed[1].IsGroupStart && !mixed[11].IsGroupStart, "4K did not restore the guard outside the dense span");
            for (int i = 2; i < 10; i++) Check(mixed[i].IsGroupStart, "4K dense span retained protected rolls");
            Console.WriteLine("PASS 4K restores eight-note protection immediately before and after a dense span"); _passed++;

            var holds = Regular(12, .08, 0);
            for (int i = 0; i < 4; i++) { NoteInstance n = holds[i]; n.HoldSeconds = 1; holds[i] = n; }
            var heldCopy = new List<NoteInstance>(holds);
            FingeringPlanner.Assign(holds, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.Aggressive);
            FingeringPlanner.Assign(heldCopy, 180, .1, FingeringPlanner.DefaultMinGroupIntervalMs, FingeringMode.FourKey);
            Check(Keys(heldCopy) == Keys(holds), "borrowing an outer key around a hold falsely released protection");
            NoHoldCollisions(heldCopy);
            Console.WriteLine("PASS 4K outer keys borrowed around holds do not trigger guard release"); _passed++;

            var unprotected = Regular(8, .04, 0); var rawAggressive = Regular(8, .04, 0);
            FingeringPlanner.Assign(unprotected, 180, .1, 0, FingeringMode.FourKey);
            FingeringPlanner.Assign(rawAggressive, 180, .1, 0, FingeringMode.Aggressive);
            Check(Keys(unprotected) == Keys(rawAggressive), "disabled lower bound altered 4K allocation");
            Console.WriteLine("PASS 4K matches aggressive when the lower bound is explicitly disabled"); _passed++;
        }

        private static void CheckEvenRolls()
        {
            Expect("uneven third note moves out of the two-note roll", Chart(new double[] { 0, .06, .15 }, .5), "DFJ");
            Expect("uneven four-note phrase remains two ordinary pairs", Chart(new double[] { 0, .05, .13, .18 }, .5), "DFKJ");
            Expect("even three-note prefix retains its group before an outlier", Chart(new double[] { 0, .05, .1, .17 }, .5), "SDFJ");
            foreach (double second in new[] { .055, .065, .054999, .065001 })
            {
                var notes = Chart(new double[] { -1, -.94, -.94 + second }, .5);
                bool inside = second == .055 || second == .065;
                FingeringPlanner.Assign(notes, 180);
                bool triple = !notes[1].IsGroupStart && !notes[2].IsGroupStart;
                Check(triple == inside && (!inside || Keys(notes) == "SDF"),
                    "+/-5ms inclusive cutoff incorrect: " + second + " " + Keys(notes));
                UniformLongRolls(notes);
            }
            Console.WriteLine("PASS long-roll interval tolerance includes +/-5ms and excludes 5.001ms"); _passed++;
            Expect("four-note roll accepts both sides of the first-gap tolerance", Chart(new double[] { 0, .05, .105, .15 }, .5), "ASDF");
            Expect("interval drift is compared to the first gap", Chart(new double[] { 0, .05, .104, .162 }, .5), "SDFJ");
        }

        private static void Check320Protection()
        {
            double beat = 60.0 / 320.0;
            double[] times = new double[32];
            for (int i = 0; i < times.Length; i++) times[i] = i * beat / 4;
            var ordinary = Chart(times, beat);
            var overstated = Chart(times, beat / 2);
            FingeringPlanner.Assign(ordinary, 180);
            FingeringPlanner.Assign(overstated, 180);
            string keys = Keys(ordinary);
            Check(keys == Keys(overstated) && keys.Contains("D") && keys.Contains("K"), "BPM metadata defeated interval protection");
            int size = 0;
            for (int i = 0; i < ordinary.Count; i++)
            {
                NoteInstance n = ordinary[i];
                Check(n.HitTime == times[i] && n.SeqId == i, "guard changed note timing or identity");
                if (n.IsGroupStart) size = 0;
                Check(++size <= 4, "guard overflowed hand capacity");
                if (i > 0 && !n.IsGroupStart)
                {
                    int previous = ordinary[i - 1].Lane;
                    Check(n.Lane / 4 == previous / 4, "group changed hands internally");
                    Check(n.Lane < 4 ? n.Lane > previous : n.Lane < previous, "guard changed outside-to-inside rolls");
                }
            }
            Console.WriteLine("PASS 320 BPM sixteenths and overstated BPM both use protected rolls -> " + keys); _passed++;
            FingeringPlanner.Assign(overstated, 180, 0, 0);
            Check(Keys(overstated) == "FJFJFJFJFJFJFJFJFJFJFJFJFJFJFJFJ", "disabled guard did not restore old classification");
            FingeringPlanner.Assign(overstated, 180, beat / 2);
            Check(Keys(overstated) == keys, "explicit BPM bypassed protection");
            Console.WriteLine("PASS protection covers explicit BPM and is independently switchable"); _passed++;
            double[] eighthTimes = new double[16];
            for (int i = 0; i < eighthTimes.Length; i++) eighthTimes[i] = i * beat / 2;
            Expect("320 BPM eighths also use rolls under the absolute safeguard", Chart(eighthTimes, beat), "DFKJDFKJDFKJDFKJ");
        }
    }
}
