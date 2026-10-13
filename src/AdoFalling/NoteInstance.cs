using UnityEngine;

namespace AdoFalling8K
{
    /// <summary>One note in the falling timeline.</summary>
    internal struct NoteInstance
    {
        /// <summary>Song time (seconds) at which the note must reach the judgement line.</summary>
        public double HitTime;

        /// <summary>Display-only chord alignment; judgement always uses HitTime.</summary>
        public bool IsChord;
        public double VisualHitTime;
        public double DisplayHitTime { get { return IsChord ? VisualHitTime : HitTime; } }

        /// <summary>Local quarter-note duration, including tempo changes; 0 if unknown.</summary>
        public double BeatSeconds;

        /// <summary>0 .. FallingView.LaneCount-1.</summary>
        public int Lane;

        /// <summary>First note of the hand group chosen by the fingering planner.</summary>
        public bool IsGroupStart;

        /// <summary>Signed distance in pixels from the judgement line (positive = above).</summary>
        public float DistancePx;

        /// <summary>Chart tile this note came from (scrFloor.seqID), or -1 for demo notes.</summary>
        public int SeqId;

        /// <summary>Seconds the tile must be held (from the game's own timeline), or 0.</summary>
        public double HoldSeconds;

        /// <summary>
        /// The live scrFloor this note came from, so the renderer can read the game's own hold
        /// progress (scrFloor.holdCompletion) instead of guessing it from the clock.
        /// </summary>
        public object Floor;

        public Color Color;
        public bool Active;

        /// <summary>Set once the game has registered a successful hit on this note, so the
        /// overlay can hide it instead of letting it keep falling past the line.</summary>
        public bool Consumed;
    }
}
