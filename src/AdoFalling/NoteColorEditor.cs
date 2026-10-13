using System;
using UnityEngine;

namespace AdoFalling8K
{
    /// <summary>Colour editor state shared by mouse text fields and keyboard adjustments.</summary>
    internal sealed class NoteColorEditor
    {
        public int Selection;
        public int Channel;
        public const int ChordColorIndex = 16;
        public readonly string[] Text = new string[17];

        public void Sync(FallingConfig cfg)
        {
            for (int i = 0; i < Text.Length; i++) Text[i] = Entry(cfg, i).Value;
        }

        public void Move(int delta) { Selection = (Selection + delta + Text.Length) % Text.Length; }
        public void MoveChannel(int delta) { Channel = (Channel + delta + 4) % 4; }

        public bool ApplyText(FallingConfig cfg, int index, string text)
        {
            Text[index] = text;
            Color parsed;
            if (!FallingConfig.TryParseNoteColor(text, out parsed)) return false;
            Entry(cfg, index).Value = text;
            return true;
        }

        public void Adjust(FallingConfig cfg, int direction, int step)
        {
            Color color = SelectedColor(cfg);
            float[] values = { color.r, color.g, color.b, color.a };
            SetComponent(cfg, Channel, (int)Math.Round(values[Channel] * 255.0) + direction * step);
        }

        public void SetComponent(FallingConfig cfg, int channel, int value)
        {
            Color color = SelectedColor(cfg);
            float[] values = { color.r, color.g, color.b, color.a };
            values[channel] = Math.Max(0, Math.Min(255, value)) / 255f;
            color = new Color(values[0], values[1], values[2], values[3]);
            ApplyText(cfg, Selection, FallingConfig.FormatNoteColor(color));
        }

        public void ResetSelected(FallingConfig cfg)
        {
            ApplyText(cfg, Selection, DefaultColor(cfg, Selection));
        }

        public void ResetAll(FallingConfig cfg)
        {
            for (int i = 0; i < Text.Length; i++) ApplyText(cfg, i, DefaultColor(cfg, i));
        }

        public Color SelectedColor(FallingConfig cfg)
        {
            return Selection == ChordColorIndex ? cfg.ChordColorValue
                : cfg.NoteColorFor(Selection / 2, Selection % 2 == 1);
        }

        private static string DefaultColor(FallingConfig cfg, int index)
        {
            return index == ChordColorIndex ? FallingConfig.DefaultChordColor
                : index % 2 == 1 ? FallingConfig.DefaultHighlightColor : FallingConfig.FormatNoteColor(cfg.NoteColorValue);
        }

        private static Setting<string> Entry(FallingConfig cfg, int index)
        {
            if (index == ChordColorIndex) return cfg.ChordColor;
            return index % 2 == 1 ? cfg.NoteHighlightColors[index / 2] : cfg.NoteNormalColors[index / 2];
        }
    }
}
