using System;
using UnityEngine;

namespace AdoFalling8K
{
    /// <summary>
    /// The Chinese control panel opened with F5.
    ///
    /// Every tunable is exposed as a slider plus a text field; edits apply immediately so the
    /// effect is visible while playing, and the values are saved to the config file when the panel
    /// is closed.
    ///
    /// IMGUI is used because it is the only UI path that reliably draws over this game. The font
    /// is replaced with an OS font that has Chinese glyphs, since Unity's built-in IMGUI font does
    /// not, and Chinese text would otherwise render as boxes.
    /// </summary>
    internal sealed class ControlPanel
    {
        private const int WindowId = 0x41444F46;   // "ADOF"
        private const float Width = 470f;

        private Rect _window = new Rect(40f, 40f, Width, 640f);
        private Font _font;
        private bool _fontTried;

        public bool Visible;
        private bool _colorPage;
        private int _keyboardFrame = -1;
        private readonly NoteColorEditor _colors = new NoteColorEditor();
        private Vector2 _scroll;
        private float _contentHeight = 1200f;
        private bool _revealSelection;

        /// <summary>
        /// Keyboard navigation index. The game routes input through Rewired, which swallows
        /// IMGUI mouse events, so the panel must be fully usable from the keyboard.
        /// </summary>
        private float _repeatAt;

        private PanelControls _controls;

        internal void Configure(FallingConfig c, FallingEngine engine, GameClock clock,
            Action loadFile, Action stopFile, Action logAlignment)
        {
            _controls = new PanelControls(c, delegate { engine.Retune(clock); }, engine.ResetJudgeStats,
                loadFile, stopFile, logAlignment);
        }

        public void SyncFromConfig(FallingConfig c)
        {
            if (_controls != null) _controls.Sync();
            _colors.Sync(c);
        }

        // ---------------------------------------------------------------- font

        private void EnsureFont()
        {
            if (_fontTried)
                return;
            _fontTried = true;

            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                string[] wanted = { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei",
                                    "黑体", "DengXian", "等线", "SimSun", "宋体", "Arial Unicode MS" };
                for (int i = 0; i < wanted.Length; i++)
                {
                    for (int j = 0; j < installed.Length; j++)
                    {
                        if (!string.Equals(installed[j], wanted[i], StringComparison.OrdinalIgnoreCase))
                            continue;
                        Font f = Font.CreateDynamicFontFromOSFont(installed[j], 16);
                        if (f != null)
                        {
                            _font = f;
                            FallingPlugin.Log.LogInfo("panel font: " + installed[j]);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FallingPlugin.Log.LogWarning("font enumeration failed: " + ex.Message);
            }

            string[] paths = { @"C:\Windows\Fonts\msyh.ttc", @"C:\Windows\Fonts\Deng.ttf",
                                @"C:\Windows\Fonts\simhei.ttf" };
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    Font f = Font.CreateDynamicFontFromOSFont(paths[i], 16);
                    if (f != null)
                    {
                        _font = f;
                        FallingPlugin.Log.LogInfo("panel font: " + paths[i]);
                        return;
                    }
                }
                catch
                {
                    // try the next path
                }
            }
            FallingPlugin.Log.LogWarning("no Chinese font found; panel text will show as boxes");
        }

        private GUIStyle _label, _title, _section, _windowStyle;
        private Texture2D _panelFill, _borderTex, _swatchTex;
        private GUISkin _skin;

        internal void Dispose()
        {
            if (_panelFill != null) UnityEngine.Object.Destroy(_panelFill);
            if (_borderTex != null) UnityEngine.Object.Destroy(_borderTex);
            if (_swatchTex != null) UnityEngine.Object.Destroy(_swatchTex);
            if (_font != null) UnityEngine.Object.Destroy(_font);
            if (_skin != null) UnityEngine.Object.Destroy(_skin);
            _panelFill = null;
            _borderTex = null;
            _swatchTex = null;
            _font = null;
            _skin = null;
        }

        private void EnsureStyles()
        {
            EnsureFont();
            if (_label != null)
                return;

            // Keep font/style changes local so UMM and other mods retain their skin.
            _skin = UnityEngine.Object.Instantiate(GUI.skin);
            _skin.hideFlags = HideFlags.HideAndDontSave;

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 14;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.normal.textColor = Color.white;
            if (_font != null)
                _label.font = _font;

            _title = new GUIStyle(_label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _section = new GUIStyle(_label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _section.normal.textColor = new Color(0.62f, 0.84f, 1f, 1f);

            // A flat 40% black fill for the panel body. Unity IMGUI has no border property on a
            // plain style, so the outline is drawn as four thin rectangles on top.
            _panelFill = Solid(new Color(0f, 0f, 0f, 0.40f));
            _borderTex = Solid(new Color(0.55f, 0.78f, 1f, 1f));
            _swatchTex = Solid(Color.white);
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _panelFill;
            _windowStyle.hover.background = _panelFill;
            _windowStyle.active.background = _panelFill;
            _windowStyle.focused.background = _panelFill;
            _windowStyle.onNormal.background = _panelFill;
            _windowStyle.normal.textColor = Color.white;
            _windowStyle.fontSize = 17;
            _windowStyle.fontStyle = FontStyle.Bold;
            if (_font != null)
                _windowStyle.font = _font;
            _windowStyle.border = new RectOffset(6, 6, 6, 6);
            _windowStyle.padding = new RectOffset(6, 6, 26, 6);

            if (_font != null)
            {
                _skin.button.font = _font;
                _skin.textField.font = _font;
                _skin.label.font = _font;
                _skin.box.font = _font;
                _skin.toggle.font = _font;
                _skin.window.font = _font;
            }
            _skin.button.fontSize = 14;
            _skin.textField.fontSize = 14;
        }

        // ---------------------------------------------------------------- drawing

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>
        /// A label style using the panel's Chinese font, for text drawn outside the panel
        /// (for example the F5 hint on the game's menus).
        /// </summary>
        public GUIStyle TextStyle()
        {
            EnsureStyles();
            return _label;
        }

        /// <summary>Draw a 2px outline around the panel, since IMGUI styles have no border.</summary>
        private void DrawBorder()
        {
            float w = _window.width;
            float h = _window.height;
            float t = 2f;
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(_window.x, _window.y, w, t), _borderTex);
            GUI.DrawTexture(new Rect(_window.x, _window.y + h - t, w, t), _borderTex);
            GUI.DrawTexture(new Rect(_window.x, _window.y, t, h), _borderTex);
            GUI.DrawTexture(new Rect(_window.x + w - t, _window.y, t, h), _borderTex);
            GUI.color = prev;
        }

        public void Draw(FallingEngine engine, FallingConfig c, GameClock clock)
        {
            EnsureStyles();
            GUISkin previous = GUI.skin;
            try
            {
                GUI.skin = _skin;
                _window = GUI.Window(WindowId, _window, id => Contents(engine, c), " AdoFalling8K 控制面板 ", _windowStyle);
                DrawBorder();
            }
            finally { GUI.skin = previous; }
        }

        private void HandlePageKeyboard(FallingConfig c, FallingEngine engine)
        {
            if (Input.GetKeyDown(KeyCode.Tab)) SwitchPage(c);
            if (!_colorPage) { HandleKeyboard(c, engine); return; }

            string focused = GUI.GetNameOfFocusedControl();
            bool typing = focused != null && focused.StartsWith("LaneNoteColor", StringComparison.Ordinal);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                GUI.FocusControl(typing ? null : "LaneNoteColor" + _colors.Selection);
                return;
            }
            if (typing) return;
            if (Input.GetKeyDown(KeyCode.UpArrow)) _colors.Move(-1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) _colors.Move(1);
            if (Input.GetKeyDown(KeyCode.PageUp)) _colors.MoveChannel(-1);
            if (Input.GetKeyDown(KeyCode.PageDown)) _colors.MoveChannel(1);
            if (Input.GetKeyDown(KeyCode.Backspace)) _colors.ResetSelected(c);
            if (Input.GetKeyDown(KeyCode.Home)) _colors.ResetAll(c);
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow))
            {
                if (Time.unscaledTime >= _repeatAt)
                {
                    _repeatAt = Time.unscaledTime + 0.06f;
                    int direction = Input.GetKey(KeyCode.LeftArrow) ? -1 : 1;
                    int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
                    _colors.Adjust(c, direction, step);
                }
            }
            else _repeatAt = 0f;
        }

        private void SwitchPage(FallingConfig c)
        {
            _colorPage = !_colorPage;
            GUI.FocusControl(null);
            _repeatAt = 0f;
            if (_colorPage) _colors.Sync(c);
        }

        /// <summary>
        /// Arrow keys navigate or adjust the same controls used by the mouse.
        /// Enter activates the selected row; scrolling keeps it visible.
        /// </summary>
        private void HandleKeyboard(FallingConfig c, FallingEngine engine)
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (focused != null && focused.StartsWith("PanelNumber", StringComparison.Ordinal))
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) GUI.FocusControl(null);
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { _controls.MoveSelection(1); _revealSelection = true; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) { _controls.MoveSelection(-1); _revealSelection = true; }
            PanelControl row = _controls.Rows[_controls.Selection];
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow))
            {
                if (Time.unscaledTime >= _repeatAt)
                {
                    _repeatAt = Time.unscaledTime + .06f;
                    int coarse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
                    row.Nudge(Input.GetKey(KeyCode.LeftArrow) ? -1 : 1, coarse);
                }
            }
            else _repeatAt = 0;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) row.Activate();
        }

        /// <summary>One line about the chart-file plan, shown in the 排键 section.</summary>
        private static string MacroPlanSummary(FallingConfig c)
        {
            MacroPlan plan = MacroPlan.Current;
            if (plan == null)
            {
                if (!string.IsNullOrEmpty(c.FilePath.Value))
                    return "谱面文件：未载入，请使用面板载入";
                return "谱面文件：未配置（Chart/FilePath）";
            }

            string name = plan.FilePath;
            int slash = name.LastIndexOfAny(new char[] { '\\', '/' });
            if (slash >= 0)
                name = name.Substring(slash + 1);

            if (MacroPlan.Active)
                return string.Format("谱面文件：已开始 {0}（锚点 {1:F2}s）", name, MacroPlan.AnchorSongTime);
            return "谱面文件：已载入 " + name + "，使用面板开始";
        }

        private void Contents(FallingEngine engine, FallingConfig c)
        {
            // Resolve named text controls in this window, once per frame.
            if (_keyboardFrame != Time.frameCount)
            {
                _keyboardFrame = Time.frameCount;
                HandlePageKeyboard(c, engine);
            }
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Tab
                || _colorPage && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)))
                Event.current.Use();
            GUI.Label(new Rect(16f, 32f, 245f, 25f), _colorPage ? "轨道音符颜色" : "显示与指法参数", _section);
            if (GUI.Button(new Rect(300f, 30f, 154f, 25f), _colorPage ? "参数页 (Tab)" : "轨道颜色 (Tab)"))
                SwitchPage(c);
            if (_colorPage) { ColorContents(c); return; }
            _window.height = Math.Max(240f, Math.Min(Screen.height - 60f, _contentHeight + 90f));
            float viewport = _window.height - 90f;
            _scroll = GUI.BeginScrollView(new Rect(0f, 60f, Width, viewport), _scroll,
                new Rect(0f, 0f, Width - 20f, _contentHeight));
            float y = 0f;
            const float rowH = 25f;
            const float labelW = 150f;
            const float fieldW = 62f;
            const float sliderW = Width - labelW - fieldW - 62f;

            string section = null;
            for (int index = 0; index < _controls.Rows.Count; index++)
            {
                PanelControl row = _controls.Rows[index];
                if (section != row.Section)
                {
                    if (section == "八轨指法") y = Key(y, "指法基准：" + engine.FingeringReference);
                    section = row.Section;
                    y = Section(y, section);
                    if (section == "文件谱面") y = Key(y, MacroPlanSummary(c));
                }
                if (index == _controls.Selection)
                {
                    GUI.Label(new Rect(4f, y, 12f, rowH), ">", _label);
                    if (_revealSelection)
                    {
                        if (y < _scroll.y) _scroll.y = y;
                        else if (y + rowH > _scroll.y + viewport) _scroll.y = y + rowH - viewport;
                    }
                }
                y = DrawControl(row, index, y, rowH, labelW, sliderW, fieldW);
            }
            y += 8f;

            y = Note(y, "对齐实测：最近 {0:F0}px  平均 {1:F0}px  样本 {2}   {3}",
                engine.JudgeOffsetPx, engine.JudgeAvgPx, engine.JudgeSamples, Verdict(engine));
            y = Note(y, "音符 {0}   长按 {1}   屏上 {2}   判定线 y={3:F0}",
                engine.NoteCount, engine.HoldCount, engine.VisibleCount, engine.JudgeLineY);
            y = Key(y, "键盘（本游戏会吞掉鼠标点击，请用键盘）：" + (_controls.Selection + 1) + " " + _controls.Rows[_controls.Selection].Label +
                       "   ↑↓ 选择   ←→ 调整(Shift 加速)   Enter 切换");
            y = Key(y, "关闭面板（F5）自动保存；鼠标若可用也可直接点");

            _contentHeight = y + 34f;
            _revealSelection = false;
            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, Width, 26f));
        }

        private void ColorContents(FallingConfig c)
        {
            float y = 66f;
            GUI.Label(new Rect(64f, y, 178f, 22f), "普通色", _section);
            GUI.Label(new Rect(256f, y, 178f, 22f), "组首音高亮色", _section);
            y += 26f;
            for (int lane = 0; lane < 8; lane++)
            {
                GUI.Label(new Rect(22f, y, 30f, 25f), FingeringPlanner.Keys[lane].ToString(), _title);
                ColorCell(c, lane * 2, 64f, y);
                ColorCell(c, lane * 2 + 1, 256f, y);
                y += 30f;
            }
            GUI.Label(new Rect(22f, y, 190f, 25f), "多押统一色", _section);
            ColorCell(c, NoteColorEditor.ChordColorIndex, 256f, y);
            y += 40f;
            string selected = _colors.Selection == NoteColorEditor.ChordColorIndex ? "多押统一色"
                : FingeringPlanner.Keys[_colors.Selection / 2] + (_colors.Selection % 2 == 1 ? " 高亮色" : " 普通色");
            y = Section(y, "当前：" + selected);
            string[] channels = { "R", "G", "B", "A" };
            for (int channel = 0; channel < 4; channel++)
            {
                Color color = _colors.SelectedColor(c);
                float[] values = { color.r, color.g, color.b, color.a };
                int current = Mathf.RoundToInt(values[channel] * 255f);
                if (GUI.Button(new Rect(20f, y, 80f, 25f), (_colors.Channel == channel ? "> " : "") + channels[channel] + " " + current))
                    _colors.Channel = channel;
                int value = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(112f, y + 8f, 324f, 16f), current, 0f, 255f));
                if (value != current) { _colors.Channel = channel; _colors.SetComponent(c, channel, value); }
                y += 28f;
            }
            Color parsed;
            if (!FallingConfig.TryParseNoteColor(_colors.Text[_colors.Selection], out parsed))
                y = Key(y, "颜色格式未完成，暂时保留上一次有效颜色");
            else y = Key(y, "HEX: #RRGGBB[AA] 或 RGB: 255,0,0[,255]");
            y = Key(y, "上下选颜色；左右调分量；Shift 加速");
            y = Key(y, "PageUp/Down 选 RGBA；Enter 开始/结束输入");
            y = Key(y, "Backspace 重置当前色；Home 重置全部颜色");
            y = Key(y, "Tab 返回参数页；" + "F5 关闭并保存；修改即时生效");
            _window.height = y + 24f;
            GUI.DragWindow(new Rect(0f, 0f, Width, 26f));
        }

        private void ColorCell(FallingConfig c, int index, float x, float y)
        {
            if (index == _colors.Selection) GUI.Label(new Rect(x - 12f, y, 12f, 25f), ">", _label);
            Rect swatch = new Rect(x, y + 2f, 24f, 22f);
            Color previous = GUI.color;
            GUI.color = index == NoteColorEditor.ChordColorIndex ? c.ChordColorValue : c.NoteColorFor(index / 2, index % 2 == 1);
            GUI.DrawTexture(swatch, _swatchTex);
            GUI.color = previous;
            if (GUI.Button(swatch, "", GUIStyle.none)) _colors.Selection = index;
            GUI.SetNextControlName("LaneNoteColor" + index);
            string text = GUI.TextField(new Rect(x + 30f, y, 148f, 25f), _colors.Text[index] ?? "", 24);
            if (text != _colors.Text[index])
            {
                _colors.Selection = index;
                _colors.ApplyText(c, index, text);
            }
        }

        private float Section(float y, string title)
        {
            GUI.Label(new Rect(16f, y, Width - 30f, 20f), title, _section);
            return y + 22f;
        }

        private float DrawControl(PanelControl row, int index, float y, float rowH,
            float labelW, float sliderW, float fieldW)
        {
            if (row.Kind != PanelControlKind.Number)
            {
                string text = row.Label + (row.Display != null ? "  " + row.Display() : "");
                if (GUI.Button(new Rect(16f, y, Width - 52f, rowH), text, _label))
                { _controls.Selection = index; row.Activate(); }
                return y + rowH;
            }
            GUI.Label(new Rect(16f, y, labelW, rowH), row.Label, _label);
            float value = GUI.HorizontalSlider(new Rect(16f + labelW, y + 8f, sliderW, 16f),
                (float)row.Number(), (float)row.Min, (float)row.Max);
            if (Math.Abs(value - row.Number()) > .00001)
            { _controls.Selection = index; row.SetNumber(value); row.Sync(); }
            GUI.SetNextControlName("PanelNumber" + index);
            string typed = GUI.TextField(new Rect(16f + labelW + sliderW + 8f, y, fieldW, rowH), row.Text, 20, _label);
            if (typed != row.Text) { _controls.Selection = index; row.ApplyText(typed); }
            return y + rowH;
        }

        private float Note(float y, string format, params object[] args)
        {
            GUIStyle st = _label;
            Color prev = st.normal.textColor;
            st.normal.textColor = new Color(0.82f, 0.92f, 1f, 1f);
            GUI.Label(new Rect(20f, y, Width - 40f, 20f), string.Format(format, args), st);
            st.normal.textColor = prev;
            return y + 20f;
        }

        /// <summary>A hint line: smaller and in a dimmer colour.</summary>
        private float Key(float y, string text)
        {
            GUIStyle st = _label;
            Color prev = st.normal.textColor;
            st.normal.textColor = new Color(0.75f, 1f, 0.8f, 1f);
            GUI.Label(new Rect(20f, y, Width - 36f, 20f), text, st);
            st.normal.textColor = prev;
            return y + 19f;
        }

        private static string Verdict(FallingEngine engine)
        {
            if (engine.JudgeSamples < 5)
                return "请先打几个音符";
            float avg = engine.JudgeAvgPx;
            if (Math.Abs(avg) <= 6f)
                return "已对齐";
            return avg > 0f ? "音符偏晚 → 减小提前量" : "音符偏早 → 增大提前量";
        }

    }
}
