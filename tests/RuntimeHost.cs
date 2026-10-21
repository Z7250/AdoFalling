// Minimal managed host for the real UMM entry, configuration, clock and runtime.
// These checks do not replace testing rendering/input in a running Unity game.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        private static readonly List<GameObject> Pending = new List<GameObject>();
        public HideFlags hideFlags;
        public static T Instantiate<T>(T value) where T : Object, new() { return new T(); }
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (value is GUISkin) AdoFalling8K.RuntimeStats.PanelsDisposed++;
            GameObject host = value as GameObject;
            if (host != null) Pending.Add(host);
        }
        public static void FlushDestroy()
        {
            foreach (GameObject host in Pending) host.DestroyNow();
            Pending.Clear();
        }
    }
    public class MonoBehaviour : Object
    {
        internal GameObject Host;
        private bool _enabled = true;
        public bool enabled
        {
            get { return _enabled; }
            set
            {
                bool disabling = _enabled && !value && Host.activeSelf;
                _enabled = value;
                if (disabling) Invoke("OnDisable");
            }
        }
        internal void Invoke(string method)
        {
            MethodInfo callback = GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (callback != null) callback.Invoke(this, null);
        }
    }
    public class GameObject : Object
    {
        public static int Created;
        public bool activeSelf = true;
        private MonoBehaviour _component;
        public GameObject(string name) { Created++; }
        public void SetActive(bool active) { activeSelf = active; }
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            T component = new T(); component.Host = this; _component = component; return component;
        }
        public T GetComponent<T>() where T : MonoBehaviour { return _component as T; }
        internal void DestroyNow() { if (_component != null) _component.Invoke("OnDestroy"); }
    }
    public struct Color
    {
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public float r, g, b, a;
        public Color(float red, float green, float blue, float alpha) { r = red; g = green; b = blue; a = alpha; }
    }
    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string text, out Color color)
        {
            color = new Color(1, 0, 0, 1);
            if (text == null || (text.Length != 7 && text.Length != 9) || text[0] != '#') return false;
            uint rgba;
            if (!uint.TryParse(text.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out rgba)) return false;
            if (text.Length == 7) rgba = rgba << 8 | 255;
            color = new Color((rgba >> 24 & 255) / 255f, (rgba >> 16 & 255) / 255f, (rgba >> 8 & 255) / 255f, (rgba & 255) / 255f);
            return true;
        }
    }
    public enum KeyCode
    {
        A, S, D, F, J, K, L, Semicolon, F3, F4, F5, F6, F7, F8, F9,
        LeftShift, RightShift, Minus, Equals, Backspace, LeftBracket, RightBracket, End, PageUp, PageDown,
        Tab, Return, KeypadEnter, UpArrow, DownArrow, LeftArrow, RightArrow, Home
    }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Down = new HashSet<KeyCode>();
        public static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
        public static bool GetKeyDown(KeyCode key) { return Down.Contains(key); }
        public static bool GetKey(KeyCode key) { return Held.Contains(key); }
        public static void Clear() { Down.Clear(); Held.Clear(); }
    }
    public static class Time { public static int frameCount; public static float unscaledTime; public static float unscaledDeltaTime = 0.016f; }
    public static class Screen { public static int height = 1080; }
    public static class Mathf { public static int RoundToInt(float value) { return (int)Math.Round(value); } }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float xx, float yy, float w, float h) { x = xx; y = yy; width = w; height = h; }
    }
    public struct Vector2 { public float x, y; }
    public enum TextAnchor { UpperLeft, MiddleLeft }
    public enum FontStyle { Normal, Bold }
    public enum HideFlags { HideAndDontSave }
    public enum TextureFormat { RGBA32 }
    public class RectOffset { public RectOffset(int l, int r, int t, int b) { } }
    public class Font : Object
    {
        public static string[] GetOSInstalledFontNames() { return new[] { "Microsoft YaHei UI" }; }
        public static Font CreateDynamicFontFromOSFont(string name, int size) { return new Font(); }
    }
    public class GUIStyleState { public Color textColor; public Texture2D background; }
    public class GUIStyle
    {
        public static GUIStyle none = new GUIStyle();
        public int fontSize;
        public Font font;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public RectOffset border, padding;
        public GUIStyleState normal = new GUIStyleState(), hover = new GUIStyleState(), active = new GUIStyleState(),
            focused = new GUIStyleState(), onNormal = new GUIStyleState();
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public Vector2 CalcSize(GUIContent content) { return new Vector2(); }
    }
    public class GUISkin : Object
    {
        public GUIStyle label = new GUIStyle(), button = new GUIStyle(), textField = new GUIStyle(),
            window = new GUIStyle(), box = new GUIStyle(), toggle = new GUIStyle();
    }
    public class GUIContent { public GUIContent(string text) { } }
    public class Texture2D : Object
    {
        public static Texture2D whiteTexture = new Texture2D();
        public Texture2D() { }
        public Texture2D(int w, int h, TextureFormat format, bool mipmaps) { }
        public void SetPixel(int x, int y, Color color) { }
        public void Apply() { }
    }
    public enum EventType { Repaint, KeyDown }
    public class Event
    {
        public static Event current = new Event();
        public EventType type;
        public KeyCode keyCode;
        public void Use() { }
    }
    public static class GUI
    {
        public static Color color;
        public static GUISkin skin = new GUISkin();
        public static string ClickLabel, Focused;
        public static readonly List<string> Labels = new List<string>();
        public static void DrawTexture(Rect rect, Texture2D texture) { }
        public static void Label(Rect rect, string text, GUIStyle style) { Labels.Add(text); }
        public static Rect Window(int id, Rect rect, Action<int> draw, string title, GUIStyle style) { draw(id); return rect; }
        public static bool Button(Rect rect, string text, GUIStyle style = null)
        { if (ClickLabel != text) return false; ClickLabel = null; return true; }
        public static float HorizontalSlider(Rect rect, float value, float min, float max) { return value; }
        public static string TextField(Rect rect, string text, int length, GUIStyle style = null) { return text; }
        public static void DragWindow(Rect rect) { }
        public static Vector2 BeginScrollView(Rect rect, Vector2 position, Rect content) { return position; }
        public static void EndScrollView() { }
        public static void SetNextControlName(string name) { }
        public static string GetNameOfFocusedControl() { return Focused; }
        public static void FocusControl(string name) { Focused = name; }
    }
    public static class GUILayout
    {
        public static void Label(string text) { }
        public static bool Button(string text) { return false; }
    }
}

namespace UnityModManagerNet
{
    public class UnityModManager
    {
        public class ModEntry
        {
            public string Path;
            public ModLogger Logger = new ModLogger();
            public Func<ModEntry, bool, bool> OnToggle;
            public Action<ModEntry> OnGUI, OnSaveGUI;
            public Func<ModEntry, bool> OnUnload;
            public class ModLogger
            {
                public readonly List<string> Messages = new List<string>();
                public void Log(string text) { Messages.Add(text); }
                public void Warning(string text) { Messages.Add(text); }
                public void Error(string text) { Messages.Add(text); }
            }
        }
    }
}

namespace AdoFalling8K
{
    internal static class RuntimeStats { public static int PanelsDisposed, RenderersDisposed; }
    internal class FallingView
    {
        public bool Visible = true;
        public void Build() { }
        public void SetVisible(bool value) { Visible = value; }
    }
    internal class ChartReader
    {
        public int Count { get { return 0; } }
        public void Refresh(GameClock clock) { }
        public void Rebuild() { }
    }
    internal class FallingEngine
    {
        public readonly List<NoteInstance> Visible = new List<NoteInstance>();
        public FallingEngine(FallingView view, ChartReader chart, FallingConfig config) { }
        public void Tick(GameClock clock, float delta, double raw) { }
        public void Retune(GameClock clock) { Retunes++; }
        public static int Retunes, Resets;
        public float JudgeOffsetPx, JudgeAvgPx, JudgeLineY;
        public int JudgeSamples, NoteCount, HoldCount, VisibleCount;
        public string FingeringReference = "120 BPM";
        public void ResetJudgeStats() { Resets++; }
    }
    internal class OverlayRenderer
    {
        public void Draw(List<NoteInstance> notes, FallingEngine engine, FallingView view, FallingConfig config, double time) { }
        public void Dispose() { RuntimeStats.RenderersDisposed++; }
    }
    internal class MacroPlan
    {
        public static MacroPlan Current;
        public string FilePath = "fixture.adofai";
        public static int Loads;
        public static bool Active;
        public static double AnchorSongTime;
        public int PressCount;
        public static MacroPlan TryLoad(string path, out string message) { message = "fixture"; Loads++; Current = new MacroPlan(); return Current; }
    }
}
