using System;
using System.Collections.Generic;

namespace AdoFalling8K
{
    internal enum FingeringMode { Stable, Aggressive, FourKey }

    /// <summary>
    /// Alternating hand groups on ASDF / JKL;, using one to four fingers from the
    /// outside towards the index finger. Aggressive prefers D/F/J/K until twenty-fourths.
    /// A bounded search keeps the pulse through subdivisions, but can move a boundary
    /// or its phase to avoid a very short cross-hand interval on syncopated passages.
    /// Lanes and group heads are assigned; timing, chart order and hold lengths are preserved.
    /// </summary>
    internal static class FingeringPlanner
    {
        public const int LaneCount = 8;
        public const int FingersPerHand = 4;
        public const string Keys = "ASDFJKL;";
        public const double DefaultWindowMs = 180.0;
        public const double DefaultMinGroupIntervalMs = 60000.0 / 180.0 / 2.0;
        public const double RollIntervalToleranceMs = 5.0;
        private const int BeamWidth = 12;
        private const double Epsilon = 0.000001;

        private sealed class Plan
        {
            public Plan Previous;
            public int Start;
            public int Count;
            public int Hand = -1;
            public int FingerMask;
            public double Cost;
            public double NextPulse;
            public double Period = 1.0;
            public double[] HeldUntil = new double[LaneCount];
        }

        /// <param name="windowMs">Cross-hand protection in milliseconds, capped at a
        /// quarter of the local hand-group period. It never determines note subdivision.</param>
        /// <param name="referenceBeatSeconds">Musical quarter-note duration. Chart speed
        /// events affect note times without redefining this hand pulse when it is set.</param>
        /// <param name="minGroupIntervalMs">Lower bound for the base hand pulse. Zero
        /// disables the safeguard; very dense passages still invoke four-finger capacity scaling.</param>
        /// <param name="mode">Stable retains the musical pulse; Aggressive prefers larger
        /// uniform rolls with each adjacent interval no longer than two thirds of the
        /// protected eighth pulse. Three/four-finger rolls require protected twenty-fourths.
        /// FourKey uses Aggressive's rules, releasing the lower bound only in spans
        /// that qualify for outer-finger expansion.</param>
        public static string Assign(List<NoteInstance> notes, double windowMs, double referenceBeatSeconds = 0.0,
            double minGroupIntervalMs = DefaultMinGroupIntervalMs, FingeringMode mode = FingeringMode.Stable)
        {
            if (notes.Count == 0)
                return ModeName(mode) + ": no notes";
            bool aggressive = mode == FingeringMode.Aggressive || mode == FingeringMode.FourKey;
            double protection = Positive(windowMs) ? windowMs / 1000.0 : DefaultWindowMs / 1000.0;
            double minimum = MinGroupIntervalSeconds(minGroupIntervalMs);
            double[] pulse = new double[notes.Count];
            double[] seconds = new double[notes.Count];
            double[] outerIntervals = new double[notes.Count];
            double[] musicalSeconds = new double[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                seconds[i] = Positive(referenceBeatSeconds) ? referenceBeatSeconds * 0.5 : PeriodSeconds(notes, i);
                musicalSeconds[i] = seconds[i];
                // Tempo metadata must not turn rapid taps back into single-finger groups.
                seconds[i] = Math.Max(seconds[i], minimum);
                outerIntervals[i] = seconds[i] / 3.0;
            }
            if (mode == FingeringMode.FourKey)
            {
                // Test the original protected expansion condition first. A single fast
                // pair or an outer key borrowed around a hold does not release the guard.
                var expanded = new bool[notes.Count];
                for (int start = 0; start + 2 < notes.Count; start++)
                    if (FitsAggressiveGroup(notes, seconds, outerIntervals, start, 3))
                        for (int j = 0; j < 3; j++) expanded[start + j] = true;
                for (int i = 0; i < notes.Count; i++)
                    if (expanded[i]) seconds[i] = musicalSeconds[i];
            }
            for (int i = 0; i < notes.Count; i++)
            {
                if (i > 0)
                {
                    double gap = notes[i].HitTime - notes[i - 1].HitTime;
                    if (gap < -Epsilon)
                        throw new ArgumentException("Notes must be in chronological order.");
                    pulse[i] = pulse[i - 1] + Math.Max(0.0, gap) / seconds[i - 1];
                }
            }

            var frontier = new List<Plan>[notes.Count + 1];
            frontier[0] = new List<Plan>();
            Plan root = new Plan();
            for (int lane = 0; lane < LaneCount; lane++)
                root.HeldUntil[lane] = double.NegativeInfinity;
            frontier[0].Add(root);

            for (int start = 0; start < notes.Count; start++)
            {
                List<Plan> paths = frontier[start];
                if (paths == null)
                    continue;
                double period = aggressive ? 1.0 : CapacityPeriod(pulse, start);
                foreach (Plan previous in paths)
                {
                    for (int size = 1; size <= FingersPerHand && start + size <= notes.Count; size++)
                    {
                        if (size > 1 && !(aggressive ? FitsAggressiveGroup(notes, seconds, outerIntervals, start, size)
                            : FitsGroup(notes, pulse, seconds, start, size, period)))
                            break;
                        double nextPulse, rhythmCost;
                        double groupPeriod = period;
                        if (aggressive)
                        {
                            // A wider roll can span multiple eighths. Keep the base period
                            // safeguard, and prefer fewer groups, packing earlier groups first.
                            double lastInterval = size > 1
                                ? (notes[start + size - 1].HitTime - notes[start + size - 2].HitTime) / seconds[start + size - 2] : 0.0;
                            groupPeriod = Math.Max(1.0, pulse[start + size - 1] - pulse[start] + lastInterval);
                            nextPulse = pulse[start] + groupPeriod;
                            rhythmCost = 4.0 + (FingersPerHand - size) * .01 / (start + 1.0);
                        }
                        else
                        {
                            double phaseCost;
                            nextPulse = NextPulse(previous, pulse[start], period, out phaseCost);
                            rhythmCost = phaseCost + (size - 1) * 0.08;
                            for (int j = 1; j < size; j++)
                                rhythmCost += Math.Max(0.0, pulse[start + j] - nextPulse + Epsilon) / period * 6.0;
                        }

                        double boundary = Math.Min(protection, seconds[start] * groupPeriod * 0.25);
                        if (start > 0)
                        {
                            double gap = notes[start].HitTime - notes[start - 1].HitTime;
                            // This is paid only when changing hands, below.
                            boundary = Math.Max(0.0, 1.0 - gap / Math.Max(Epsilon, boundary));
                        }
                        else
                            boundary = 0.0;

                        for (int hand = 0; hand < 2; hand++)
                        {
                            bool repeatedHand = previous.Hand == hand;
                            double handCost = previous.Hand < 0 ? hand * 0.01
                                : repeatedHand ? 12.0 : boundary * boundary * 20.0;
                            for (int mask = 1; mask < 16; mask++)
                            {
                                if (BitCount(mask) != size)
                                    continue;
                                double fingerCost;
                                if (!Available(notes, previous, start, size, hand, mask, out fingerCost))
                                    continue;
                                double cost = previous.Cost + rhythmCost + handCost + fingerCost;
                                int end = start + size;
                                if (frontier[end] == null)
                                    frontier[end] = new List<Plan>();
                                if (frontier[end].Count >= BeamWidth && cost >= frontier[end][BeamWidth - 1].Cost)
                                    continue;

                                Plan candidate = new Plan();
                                candidate.Previous = previous;
                                candidate.Start = start;
                                candidate.Count = size;
                                candidate.Hand = hand;
                                candidate.FingerMask = mask;
                                candidate.Cost = cost;
                                candidate.NextPulse = nextPulse;
                                candidate.Period = groupPeriod;
                                Array.Copy(previous.HeldUntil, candidate.HeldUntil, LaneCount);
                                int noteIndex = start;
                                for (int finger = 3; finger >= 0; finger--)
                                {
                                    if ((mask & (1 << finger)) == 0)
                                        continue;
                                    NoteInstance n = notes[noteIndex++];
                                    int lane = Lane(hand, finger);
                                    // A tap also occupies its key at this exact timestamp;
                                    // simultaneous distinct notes must not reuse one key.
                                    candidate.HeldUntil[lane] = n.HitTime + Math.Max(Epsilon * 2.0, n.HoldSeconds);
                                }
                                Offer(frontier[end], candidate, notes[Math.Min(end, notes.Count - 1)].HitTime);
                            }
                        }
                    }
                }
                frontier[start] = null;
            }

            List<Plan> completed = frontier[notes.Count];
            if (completed == null || completed.Count == 0)
                throw new InvalidOperationException("No eight-key fingering is available without reusing a held key.");
            int groups = 0;
            int rolls = 0;
            int exceptions = 0;
            for (Plan p = completed[0]; p.Previous != null; p = p.Previous)
            {
                groups++;
                if (p.Count > 1) rolls++;
                if (p.Hand == p.Previous.Hand) exceptions++;
                int noteIndex = p.Start;
                for (int finger = 3; finger >= 0; finger--)
                {
                    if ((p.FingerMask & (1 << finger)) == 0)
                        continue;
                    NoteInstance n = notes[noteIndex];
                    n.Lane = Lane(p.Hand, finger);
                    n.IsGroupStart = noteIndex == p.Start;
                    notes[noteIndex++] = n;
                }
            }
            return string.Format("{4}: {0} notes, {1} groups ({2} rolls), ASDF/JKL;, same-hand exceptions {3}",
                notes.Count, groups, rolls, exceptions, ModeName(mode));
        }

        public static string ModeName(FingeringMode mode)
        {
            return mode == FingeringMode.FourKey ? "4K"
                : mode == FingeringMode.Aggressive ? "伪4K" : "8K";
        }

        private static bool FitsAggressiveGroup(List<NoteInstance> notes, double[] seconds,
            double[] outerIntervals, int start, int size)
        {
            double firstGap = notes[start + 1].HitTime - notes[start].HitTime;
            for (int j = 1; j < size; j++)
            {
                double gap = notes[start + j].HitTime - notes[start + j - 1].HitTime;
                if (gap > seconds[start + j - 1] * (2.0 / 3.0) + 0.000000001) return false;
                // Slower rolls use at most two fingers per hand (D/F or K/J).
                // Held keys can still require an outer finger through Available().
                if (size >= 3 && gap > outerIntervals[start + j - 1] + 0.000000001) return false;
                if (size >= 3 && j >= 2 && Math.Abs(gap - firstGap)
                    > RollIntervalToleranceMs / 1000.0 + 0.000000001) return false;
                if (Math.Abs(seconds[start + j] - seconds[start]) > seconds[start] * .01) return false;
            }
            return true;
        }

        internal static double MinGroupIntervalSeconds(double milliseconds)
        {
            if (milliseconds < 0.0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
                milliseconds = DefaultMinGroupIntervalMs;
            return milliseconds / 1000.0;
        }

        private static double PeriodSeconds(List<NoteInstance> notes, int index)
        {
            if (Positive(notes[index].BeatSeconds))
                return notes[index].BeatSeconds * 0.5;
            // Older/incomplete chart sources: estimate the slower local pulse, not a
            // single inserted subdivision. Official live/file sources supply tempo.
            var gaps = new List<double>();
            for (int i = Math.Max(1, index - 8); i <= Math.Min(notes.Count - 1, index + 8); i++)
            {
                double gap = notes[i].HitTime - notes[i - 1].HitTime;
                if (Positive(gap) && gap <= 2.0) gaps.Add(gap);
            }
            if (gaps.Count == 0) return 0.25;
            gaps.Sort();
            return gaps[(gaps.Count - 1) * 3 / 4];
        }

        private static double CapacityPeriod(double[] pulse, int start)
        {
            double period = 1.0;
            if (start + FingersPerHand < pulse.Length)
            {
                double span = pulse[start + FingersPerHand] - pulse[start];
                while (period > span + Epsilon && period > 1.0 / 64.0)
                    period *= 0.5;
            }
            return period;
        }

        private static bool FitsGroup(List<NoteInstance> notes, double[] pulse, double[] seconds,
            int start, int size, double period)
        {
            if (pulse[start + size - 1] - pulse[start] >= period - Epsilon)
                return false;
            double firstGap = notes[start + 1].HitTime - notes[start].HitTime;
            for (int j = 1; j < size; j++)
            {
                // Two-note rolls retain their original eligibility. Larger rolls must
                // keep each real interval within +/-5ms of the first interval.
                if (size >= 3 && j >= 2 && Math.Abs(notes[start + j].HitTime - notes[start + j - 1].HitTime
                    - firstGap) > RollIntervalToleranceMs / 1000.0 + 0.000000001)
                    return false;
                if (pulse[start + j] - pulse[start + j - 1] > period * 0.75 + Epsilon)
                    return false;
                // A tempo event starts a new group; its quarter-note length changes
                // grouping speed, rather than masquerading as a subdivision.
                if (Math.Abs(seconds[start + j] - seconds[start]) > seconds[start] * 0.01)
                    return false;
            }
            return true;
        }

        private static double NextPulse(Plan previous, double start, double period, out double cost)
        {
            cost = 0.0;
            if (previous.Hand < 0 || start - previous.NextPulse > period * 1.75)
                return start + period; // first note / rest: silent slots do not change hands
            double expected = previous.NextPulse;
            if (Math.Abs(previous.Period - period) > Epsilon)
                expected += period - previous.Period;
            double error = (start - expected) / period;
            cost = error * error * 3.0;
            if (Math.Abs(error) > 0.35)
            {
                // A persistent offbeat can establish a new phase. Switching twice
                // for one inserted offbeat costs more than absorbing it in a group.
                cost = 0.7 + Math.Abs(error) * 0.3;
                return start + period;
            }
            return expected + period + Math.Max(-0.15 * period, Math.Min(0.15 * period, start - expected));
        }

        private static bool Available(List<NoteInstance> notes, Plan previous, int start, int size,
            int hand, int mask, out double cost)
        {
            cost = 0.0;
            int index = start;
            int depth = 0;
            for (int finger = 3; finger >= 0; finger--)
            {
                if ((mask & (1 << finger)) == 0) continue;
                int lane = Lane(hand, finger);
                if (previous.HeldUntil[lane] > notes[index].HitTime + Epsilon)
                    return false;
                depth += finger;
                index++;
            }
            // Inner-most available fingers win. In particular every normal singleton
            // is F/J, including repeated index use across alternating hand groups.
            cost = (depth - size * (size - 1) / 2) * 0.35;
            return true;
        }

        private static void Offer(List<Plan> paths, Plan candidate, double now)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                Plan other = paths[i];
                if (other.Hand != candidate.Hand || Math.Abs(other.NextPulse - candidate.NextPulse) > 0.01
                    || Math.Abs(other.Period - candidate.Period) > Epsilon)
                    continue;
                bool sameHolds = true;
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    if (Math.Max(now, other.HeldUntil[lane]) != Math.Max(now, candidate.HeldUntil[lane]))
                    { sameHolds = false; break; }
                }
                if (!sameHolds) continue;
                if (other.Cost <= candidate.Cost) return;
                paths.RemoveAt(i);
                break;
            }
            int position = 0;
            while (position < paths.Count && paths[position].Cost <= candidate.Cost) position++;
            paths.Insert(position, candidate);
            if (paths.Count > BeamWidth) paths.RemoveAt(BeamWidth);
        }

        private static int Lane(int hand, int finger) { return hand == 0 ? 3 - finger : 4 + finger; }
        private static int BitCount(int mask)
        {
            int count = 0;
            for (; mask != 0; mask >>= 1) count += mask & 1;
            return count;
        }
        private static bool Positive(double value) { return value > Epsilon && !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
