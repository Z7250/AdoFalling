using System;
using System.Collections.Generic;

namespace AdoFalling8K
{
    /// <summary>Marks display-only chords after notes and lanes have been generated.</summary>
    internal static class ChordVisuals
    {
        public const double DefaultWindowMs = 25.0;
        private const double BoundaryTolerance = 0.000000001;

        public static void Mark(List<NoteInstance> notes, double windowMs)
        {
            if (windowMs < 0.0 || double.IsNaN(windowMs) || double.IsInfinity(windowMs))
                windowMs = DefaultWindowMs;
            double window = windowMs / 1000.0;
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                n.IsChord = false;
                n.VisualHitTime = n.HitTime;
                notes[i] = n;
            }
            for (int start = 0; start < notes.Count; )
            {
                int end = start + 1;
                while (end < notes.Count && window > 0.0
                    && notes[end].HitTime - notes[end - 1].HitTime < window - BoundaryTolerance) end++;
                if (end - start > 1)
                    for (int i = start; i < end; i++)
                    {
                        NoteInstance n = notes[i];
                        n.IsChord = true;
                        n.VisualHitTime = notes[start].HitTime;
                        notes[i] = n;
                    }
                start = end;
            }
        }
    }
}
