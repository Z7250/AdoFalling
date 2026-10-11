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
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
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
        public float r, g, b, a;
        public Color(float red, float green, float blue, float alpha) { r = red; g = green; b = blue; a = alpha; }
    }
    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string text, out Color color)
        {
            color = new Color(1, 0, 0, 1);
            if (text == null || text.Length != 7 || text[0] != '#') return false;
            int rgb;
            if (!int.TryParse(text.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out rgb)) return false;
            color = new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1);
            return true;
        }
    }
    public enum KeyCode
    {
        A, S, D, F, J, K, L, Semicolon, F3, F4, F5, F6, F7, F8, F9,
        LeftShift, RightShift, Minus, Equals, Backspace, LeftBracket, RightBracket, End, PageUp, PageDown
    }
    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetKey(KeyCode key) { return false; }
    }
    public static class Time { public static float unscaledTime; public static float unscaledDeltaTime = 0.016f; }
    public static class Screen { public static int height = 1080; }
    public static class Mathf { public static int RoundToInt(float value) { return (int)Math.Round(value); } }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float xx, float yy, float w, float h) { x = xx; y = yy; width = w; height = h; }
    }
    public struct Vector2 { public float x, y; }
    public enum TextAnchor { UpperLeft }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle
    {
        public int fontSize;
        public TextAnchor alignment;
        public GUIStyleState normal = new GUIStyleState();
        public Vector2 CalcSize(GUIContent content) { return new Vector2(); }
    }
    public class GUIContent { public GUIContent(string text) { } }
    public class Texture2D : Object { public static Texture2D whiteTexture = new Texture2D(); }
    public static class GUI
    {
        public static Color color;
        public static void DrawTexture(Rect rect, Texture2D texture) { }
        public static void Label(Rect rect, string text, GUIStyle style) { }
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

namespace AdoFalling
{
    internal enum LaneMapping { Fingering, Macro, Cycle, TilePosition }
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
        public void Retune(GameClock clock) { }
        public void ResetJudgeStats() { }
    }
    internal class OverlayRenderer
    {
        public void Draw(List<NoteInstance> notes, FallingEngine engine, FallingView view, FallingConfig config, double time) { }
        public void Dispose() { RuntimeStats.RenderersDisposed++; }
    }
    internal class ControlPanel
    {
        public bool Visible;
        public void SyncFromConfig(FallingConfig config) { }
        public void SetClock(GameClock clock) { }
        public void Draw(FallingEngine engine, FallingConfig config, GameClock clock) { }
        public UnityEngine.GUIStyle TextStyle() { return new UnityEngine.GUIStyle(); }
        public void Dispose() { RuntimeStats.PanelsDisposed++; }
    }
    internal class MacroPlan
    {
        public static bool Active;
        public static double AnchorSongTime;
        public int PressCount;
        public static MacroPlan TryLoad(string path, out string message) { message = "fixture"; return new MacroPlan(); }
    }
    internal class GameState
    {
        public bool Playing = true, InLevelScene = true;
        public double SongPosition, Bpm = 120, Crotchet = 0.5;
        public int SeqId;
        public string StateName = "PlayerControl", Diagnostics = "fixture";
        public static GameState Capture() { return new GameState(); }
        public static bool CaptureTime(out double raw, out bool playing, out bool inLevel)
        { raw = 0; playing = true; inLevel = true; return true; }
        public static Type FindType(Assembly assembly, string name) { return assembly.GetType(name); }
        public static MemberInfo FindMember(Type type, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            foreach (string name in names)
            {
                MemberInfo member = (MemberInfo)type.GetProperty(name, flags) ?? type.GetField(name, flags);
                if (member != null) return member;
            }
            return null;
        }
        public static object Read(object target, MemberInfo member)
        {
            PropertyInfo property = member as PropertyInfo;
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = member as FieldInfo;
            return field == null ? null : field.GetValue(target);
        }
    }
}
