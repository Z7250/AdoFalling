using System;
using System.Collections.Generic;

namespace AdoFalling
{
    /// <summary>
    /// Alternating hand groups on ASDF / JKL;. A group normally occupies an eighth
    /// beat and uses one to four fingers from the outside towards the index finger.
    /// A bounded search keeps the pulse through subdivisions, but can move a boundary
    /// or its phase to avoid a very short cross-hand interval on syncopated passages.
    /// Only Lane is changed; timing, chart order and hold lengths are preserved.
    /// </summary>
    internal static class FingeringPlanner
    {
        public const int LaneCount = 8;
        public const int FingersPerHand = 4;
        public const string Keys = "ASDFJKL;";
        public const double DefaultWindowMs = 180.0;
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
        public static string Assign(List<NoteInstance> notes, double windowMs)
        {
            if (notes.Count == 0)
                return "hand groups: no notes";
            double protection = Positive(windowMs) ? windowMs / 1000.0 : DefaultWindowMs / 1000.0;
            double[] pulse = new double[notes.Count];
            double[] seconds = new double[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                seconds[i] = PeriodSeconds(notes, i);
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
                double period = CapacityPeriod(pulse, start);
                foreach (Plan previous in paths)
                {
                    for (int size = 1; size <= FingersPerHand && start + size <= notes.Count; size++)
                    {
                        if (size > 1 && !FitsGroup(pulse, seconds, start, size, period))
                            break;
                        double phaseCost;
                        double nextPulse = NextPulse(previous, pulse[start], period, out phaseCost);
                        double rhythmCost = phaseCost + (size - 1) * 0.08;
                        for (int j = 1; j < size; j++)
                            rhythmCost += Math.Max(0.0, pulse[start + j] - nextPulse + Epsilon) / period * 6.0;

                        double boundary = Math.Min(protection, seconds[start] * period * 0.25);
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
                                candidate.Period = period;
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
                    notes[noteIndex++] = n;
                }
            }
            return string.Format("hand groups: {0} notes, {1} groups ({2} rolls), ASDF/JKL;, same-hand exceptions {3}",
                notes.Count, groups, rolls, exceptions);
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

        private static bool FitsGroup(double[] pulse, double[] seconds, int start, int size, double period)
        {
            if (pulse[start + size - 1] - pulse[start] >= period - Epsilon)
                return false;
            for (int j = 1; j < size; j++)
            {
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
