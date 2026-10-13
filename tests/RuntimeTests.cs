using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AdoFalling8K;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;

internal static class RuntimeTests
{
    private static int _passed;
    private static string _root;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Case(string name, Action test) { test(); _passed++; Console.WriteLine("PASS " + name); }
    private static JObject Read(string path) { return JObject.Parse(File.ReadAllText(path)); }
    private static void Tick(FallingPlugin runtime)
    {
        Time.unscaledTime += 0.1f;
        Time.frameCount++;
        typeof(FallingPlugin).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
    }
    private static int Handlers(scrPlayer player) { return player.onHit == null ? 0 : player.onHit.GetInvocationList().Length; }
    private static void Render(FallingPlugin runtime)
    { typeof(FallingPlugin).GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null); }
    private static ControlPanel Panel(FallingPlugin runtime)
    { return (ControlPanel)typeof(FallingPlugin).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime); }
    private static PanelControls Controls(FallingPlugin runtime)
    { return (PanelControls)typeof(ControlPanel).GetField("_controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Panel(runtime)); }
    private static int Main(string[] args)
    {
        try
        {
            _root = args[0]; Directory.CreateDirectory(_root);
            FallingPlugin.Log = new ModLog(new UnityModManager.ModEntry.ModLogger());
            Case("settings round trip preserves options and unknown keys", RoundTrip);
            Case("4K mode persists and the removed sixteenth switch is cleaned up", FourKeyMode);
            Case("removed experimental mode falls back to stable without losing settings", RemovedMode);
            Case("invalid settings fall back with diagnostics", InvalidValues);
            Case("malformed JSON is backed up before defaults are saved", Malformed);
            Case("obsolete hotkeys are removed without losing unrelated settings", HotkeyMigration);
            Case("shared panel controls preserve bounds, defaults and action navigation", PanelDefinitions);
            Case("shared reflection preserves static, instance and safe-read behavior", ReflectionReads);
            Case("sixteen lane colours migrate and round trip independently", LaneColors);
            Case("palette changes invalidate caches and invalid colours fall back", ColorFallbacks);
            Case("colour editor handles keyboard channels and partial input", ColorEditor);
            Case("unified chord colour defaults, overrides and threshold settings round trip", ChordColour);
            Case("obsolete layout switches and mappings are removed without losing supported settings", ObsoleteLayout);
            Case("UMM load registers callbacks without starting the host", LoadOnly);
            Lifecycle();
            Case("auto-play restoration preserves an already enabled flag", AutoPlayRestore);
            Console.WriteLine(_passed + " UMM/config/lifecycle checks passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void RoundTrip()
    {
        string path = Path.Combine(_root, "roundtrip.json");
        File.WriteAllText(path, "{\"Custom\":{\"Retained\":42},\"General\":{\"FutureOption\":true}}");
        var settings = new SettingsFile(path, null);
        var cfg = new FallingConfig(settings);
        cfg.NoteSpeedPxPerSec.Value = 733.5f;
        cfg.LeadOffsetManual.Value = -0.125;
        cfg.AutoPlay.Value = true;
        cfg.FilePath.Value = @"D:\谱面\测试.adofai";
        Check(cfg.FingeringMode.Value == FingeringMode.Stable, "existing configs no longer default to stable rolls");
        cfg.FingeringMode.Value = FingeringMode.Aggressive;
        cfg.FingeringBpm.Value = 180;
        cfg.FingeringFollowSpeed.Value = true;
        Check(Math.Abs(cfg.FingeringMinIntervalMs.Value - 1000.0/6) < 1e-9, "default guard is not the 180 BPM eighth interval");
        cfg.FingeringMinIntervalMs.Value = 0;
        Check(cfg.FJGap.Value == 0, "default F/J extra distance changed existing layout");
        cfg.FJGap.Value = 180;
        cfg.LaneColor.Value = new Color(.1f, .2f, .3f, .4f);
        settings.Save(); settings.Save();
        cfg = new FallingConfig(new SettingsFile(path, null));
        Check(cfg.NoteSpeedPxPerSec.Value == 733.5f && cfg.LeadOffsetManual.Value == -.125, "numeric values changed");
        Check(cfg.AutoPlay.Value && cfg.FilePath.Value == @"D:\谱面\测试.adofai", "bool or unicode path changed");
        Check(cfg.FingeringBpm.Value == 180 && cfg.FingeringFollowSpeed.Value, "fingering tempo settings changed");
        Check(cfg.FingeringMode.Value == FingeringMode.Aggressive && Read(path)["Chart"]["FingeringMode"].Value<string>() == "Aggressive",
            "fingering mode enum did not round trip");
        Check(cfg.FingeringMinIntervalMs.Value == 0, "guard disable setting did not round trip");
        Check(cfg.FJGap.Value == 180 && Read(path)["Layout"]["FJGap"].Value<float>() == 180,
            "F/J extra distance setting did not round trip");
        Check(Math.Abs(cfg.LaneColor.Value.a - .4f) < .00001, "color alpha changed");
        Check(Read(path)["Custom"]["Retained"].Value<int>() == 42 && Read(path)["General"]["FutureOption"].Value<bool>(), "unknown settings lost");
        Check(!File.Exists(path + ".tmp"), "temporary file left after atomic save");
    }
    private static void FourKeyMode()
    {
        string path = Path.Combine(_root, "four-key-mode.json");
        File.WriteAllText(path, "{\"Chart\":{\"FingeringSixteenthProtection\":true,\"FingeringWindowMs\":60},\"Custom\":{\"Kept\":42}}");
        var settings = new SettingsFile(path, null); var cfg = new FallingConfig(settings);
        cfg.FingeringMode.Value = FingeringMode.FourKey; settings.Save();
        var loaded = new FallingConfig(new SettingsFile(path, null));
        Check(loaded.FingeringMode.Value == FingeringMode.FourKey
            && Read(path)["Chart"]["FingeringMode"].Value<string>() == "FourKey"
            && FingeringPlanner.ModeName(loaded.FingeringMode.Value) == "4K", "4K mode did not round trip");
        Check(Read(path)["Chart"]["FingeringSixteenthProtection"] == null
            && loaded.FingeringWindowMs.Value == 60 && Read(path)["Custom"]["Kept"].Value<int>() == 42,
            "removed switch remained or unrelated settings were lost");
    }
    private static void RemovedMode()
    {
        string path = Path.Combine(_root, "removed-mode.json");
        File.WriteAllText(path, "{\"Chart\":{\"FingeringMode\":\"Experimental\"},\"Custom\":{\"Kept\":42}}");
        var warnings = new List<string>();
        var settings = new SettingsFile(path, warnings.Add);
        var cfg = new FallingConfig(settings);
        Check(cfg.FingeringMode.Value == FingeringMode.Stable && warnings.Count == 1,
            "removed mode was not reset to stable with a diagnostic");
        settings.Save();
        Check(Read(path)["Chart"]["FingeringMode"].Value<string>() == "Stable"
            && Read(path)["Custom"]["Kept"].Value<int>() == 42, "mode fallback lost unrelated settings");
    }
    private static void InvalidValues()
    {
        string path = Path.Combine(_root, "invalid.json");
        File.WriteAllText(path, "{\"Chart\":{\"FingeringMode\":\"InvalidMode\"},\"General\":{\"DemoBpm\":\"NaN\",\"AutoPlay\":{}},"
            + "\"Style\":{\"LaneColor\":{\"r\":1},\"JudgeLineColor\":{\"r\":\"Infinity\",\"g\":1,\"b\":1,\"a\":1}}}");
        var warnings = new List<string>();
        var cfg = new FallingConfig(new SettingsFile(path, warnings.Add));
        Check(cfg.FingeringMode.Value == FingeringMode.Stable && cfg.DemoBpm.Value == 150 && !cfg.AutoPlay.Value, "bad primitives accepted");
        Check(cfg.LaneColor.Value.r == .05f && cfg.JudgeColor.Value.r == 1, "bad colors accepted");
        Check(warnings.Count == 5, "expected five diagnostics: " + warnings.Count);
    }
    private static void Malformed()
    {
        string path = Path.Combine(_root, "malformed.json");
        const string broken = "{ broken JSON";
        File.WriteAllText(path, broken);
        var settings = new SettingsFile(path, null);
        new FallingConfig(settings);
        settings.Save(); settings.Save();
        Check(File.ReadAllText(path + ".invalid.bak") == broken, "backup does not preserve original");
        Check(Read(path)["Layout"]["LaneWidth"].Value<float>() == 70, "defaults not saved");
    }
    private static void HotkeyMigration()
    {
        string path = Path.Combine(_root, "hotkey.json");
        File.WriteAllText(path, "{\"General\":{\"LoadKey\":\"F3\",\"ToggleKey\":\"F7\",\"FutureOption\":42},"
            + "\"Tuning\":{\"TuneKey\":\"F6\",\"ControlPanelKey\":\"F4\",\"TuneEarlierKey\":\"F7\",\"TuneLaterKey\":\"F9\","
            + "\"HoldLaterKey\":\"Minus\",\"HoldEarlierKey\":\"Equals\",\"HoldOffsetSeconds\":0.125}}");
        var settings = new SettingsFile(path, null); var cfg = new FallingConfig(settings); settings.Save();
        JObject data = Read(path);
        Check(data["General"]["LoadKey"] == null && data["General"]["ToggleKey"] == null, "general hotkeys were retained");
        foreach (string key in new[] { "TuneKey", "ControlPanelKey", "TuneEarlierKey", "TuneLaterKey", "HoldLaterKey", "HoldEarlierKey" })
            Check(data["Tuning"][key] == null, "removed shortcut stayed in saved settings: " + key);
        Check(cfg.HoldOffsetSeconds.Value == .125 && data["General"]["FutureOption"].Value<int>() == 42, "cleanup lost supported options");
    }

    private static PanelControl FindRow(PanelControls controls, string label)
    { foreach (PanelControl row in controls.Rows) if (row.Label == label) return row; throw new Exception("Unknown row: " + label); }
    private static void PanelDefinitions()
    {
        string path = Path.Combine(_root, "panel-controls.json");
        File.WriteAllText(path, "{\"Layout\":{\"NoteSpeed\":900},\"Chart\":{\"FilePath\":\"kept.adofai\"}}");
        var cfg = new FallingConfig(new SettingsFile(path, null));
        int retunes = 0, resets = 0, loads = 0, stops = 0, logs = 0;
        var controls = new PanelControls(cfg, delegate { retunes++; }, delegate { resets++; },
            delegate { loads++; }, delegate { stops++; }, delegate { logs++; });
        Check(controls.Rows.Count == 35, "panel rows are missing or duplicated");
        controls.MoveSelection(-1); Check(controls.Selection == 34, "backward row wrap failed");
        controls.MoveSelection(1); Check(controls.Selection == 0, "forward row wrap failed");
        PanelControl speed = FindRow(controls, "下落速度");
        speed.SetNumber(10000); Check(cfg.NoteSpeedPxPerSec.Value == 3000, "numeric upper bound ignored");
        speed.Nudge(-1, 5); Check(cfg.NoteSpeedPxPerSec.Value == 2900, "coarse step ignored");
        speed.Activate(); Check(cfg.NoteSpeedPxPerSec.Value == 620, "reset used loaded value instead of declared default");
        PanelControl trim = FindRow(controls, "音符提前量(秒)");
        Check(trim.ApplyText("0.000123456789") && cfg.LeadOffsetManual.Value == .000123456789, "double input lost precision");
        Check(!trim.ApplyText("NaN") && !trim.ApplyText("-") && cfg.LeadOffsetManual.Value == .000123456789, "invalid partial input changed numeric settings");
        int before = retunes; FindRow(controls, "多押对齐窗口(ms)").Nudge(1, 1);
        Check(retunes == before && cfg.ChordWindowMs.Value == 26, "visual chord tuning changed clock alignment");
        PanelControl mode = FindRow(controls, "排键方案");
        mode.Nudge(-1, 1); Check(cfg.FingeringMode.Value == FingeringMode.FourKey, "reverse mode cycle failed");
        mode.Activate(); Check(cfg.FingeringMode.Value == FingeringMode.Stable, "forward mode cycle failed");
        foreach (string label in new[] { "载入 / 重启谱面", "退出文件模式", "输出对齐日志" }) FindRow(controls, label).Nudge(1, 1);
        Check(loads + stops + logs == 0, "arrows triggered a panel action");
        foreach (string label in new[] { "载入 / 重启谱面", "退出文件模式", "输出对齐日志" }) FindRow(controls, label).Activate();
        Check(loads == 1 && stops == 1 && logs == 1, "panel actions did not invoke their callbacks");
        cfg.AutoPlay.Value = true; cfg.DebugHud.Value = true; cfg.ChordColor.Value = "#010203";
        controls.ResetAll();
        Check(cfg.AutoPlay.Value && cfg.DebugHud.Value && cfg.FilePath.Value == "kept.adofai" && cfg.ChordColor.Value == "#010203",
            "restore defaults broadened its previous scope");
        Check(cfg.LeadOffsetManual.Value == .07 && cfg.FingeringMode.Value == FingeringMode.Stable && resets == 1, "restore defaults did not reset supported controls/stats");
    }
    private sealed class ReflectionFixture
    {
        public static int StaticValue = 7;
        public double Value = 3.5;
        public int Broken { get { throw new Exception("unreadable"); } }
        public static int ReadOnly { get { return 9; } }
    }
    private static void ReflectionReads()
    {
        Type type = typeof(ReflectionFixture); var target = new ReflectionFixture();
        Check((int)GameReflection.Resolve(GameReflection.FindStatic(type, "StaticValue")) == 7, "static field resolution failed");
        Check((double)GameReflection.Read(target, GameReflection.FindMember(type, "Missing", "Value")) == 3.5, "member aliases or instance reads changed");
        Check(GameReflection.Read(null, GameReflection.FindMember(type, "Value")) == null, "null instance read changed");
        Check(GameReflection.SafeRead(target, GameReflection.FindMember(type, "Broken")) == null, "safe read did not handle throwing getters");
        Check(!GameReflection.Write(null, GameReflection.FindStatic(type, "ReadOnly"), 0), "readonly property was treated as writable");
        Check(GameReflection.ToDouble("invalid") == 0 && !GameReflection.ToBool("invalid"), "numeric/bool fallbacks changed");
    }
    private static UnityModManager.ModEntry _entry;

    private static void LaneColors()
    {
        string path = Path.Combine(_root, "colours.json");
        File.WriteAllText(path, "{\"Style\":{\"NoteColor\":\"#11223380\"},"
            + "\"NoteColorsNormal\":{\"F\":\"#AABBCC\"},\"NoteColorsHighlight\":{\"D\":\"60,70,80,90\"}}");
        var settings = new SettingsFile(path, null); var cfg = new FallingConfig(settings);
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(0, false)) == "#11223380", "legacy normal colour/alpha not migrated");
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(3, false)) == "#AABBCCFF", "existing lane normal colour overwritten");
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(2, true)) == "#3C46505A", "RGB lane highlight/alpha not loaded");
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(7, true)) == "#FFD966FF", "default highlight missing");
        for (int lane = 0; lane < 8; lane++)
        {
            cfg.NoteNormalColors[lane].Value = "#" + (lane * 17).ToString("X2") + "223344";
            cfg.NoteHighlightColors[lane].Value = "#EE" + (lane * 17).ToString("X2") + "5566";
        }
        settings.Save();
        var restored = new FallingConfig(new SettingsFile(path, null));
        for (int lane = 0; lane < 8; lane++)
        {
            Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(lane, false)) == FallingConfig.FormatNoteColor(restored.NoteColorFor(lane, false)), "normal palette did not round trip");
            Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(lane, true)) == FallingConfig.FormatNoteColor(restored.NoteColorFor(lane, true)), "highlight palette did not round trip");
        }
        JObject data = Read(path);
        Check(data["NoteColorsNormal"]["Semicolon"] != null && ((JObject)data["NoteColorsHighlight"]).Count == 8, "eight-key palette not saved");
        Check(FallingConfig.FormatNoteColor(restored.NoteColorFor(0, false)) == "#00223344", "saved logical palette changed");
    }

    private static void ColorFallbacks()
    {
        var logger = new UnityModManager.ModEntry.ModLogger(); FallingPlugin.Log = new ModLog(logger);
        var cfg = new FallingConfig(new SettingsFile(Path.Combine(_root, "unused-colour-settings.json"), null));
        cfg.NoteNormalColors[3].Value = "#01020304";
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(3, false)) == "#01020304", "normal colour not resolved");
        cfg.NoteNormalColors[3].Value = "#10203040";
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(3, false)) == "#10203040", "palette cache stale after editing");
        cfg.NoteNormalColors[3].Value = "invalid";
        cfg.NoteHighlightColors[3].Value = "#FF";
        for (int i = 0; i < 10; i++)
        {
            Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(3, false)) == "#FF0000FF", "normal fallback incorrect");
            Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(3, true)) == "#FFD966FF", "highlight fallback incorrect");
        }
        Check(logger.Messages.Count == 2, "invalid palette logs repeated every frame");
        cfg.NoteHighlightColors[3].Value = "0,255,0,128";
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(3, true)) == "#00FF0080", "corrected highlight remained cached");
    }

    private static void ColorEditor()
    {
        var cfg = new FallingConfig(new SettingsFile(Path.Combine(_root, "unused-editor-settings.json"), null));
        var editor = new NoteColorEditor(); editor.Sync(cfg);
        editor.Move(-1); Check(editor.Selection == 16, "editor cannot select the unified chord colour");
        Check(editor.ApplyText(cfg, 16, "#12345678"), "chord HEX colour not accepted");
        editor.SetComponent(cfg, 3, 255);
        Check(cfg.ChordColor.Value == "#123456FF", "chord editor changed a lane palette instead");
        editor.ResetSelected(cfg); Check(cfg.ChordColor.Value == FallingConfig.DefaultChordColor, "chord colour reset failed");
        editor.Move(-1); Check(editor.Selection == 15, "editor cannot select semicolon highlight");
        editor.MoveChannel(-1); Check(editor.Channel == 3, "alpha channel not selected");
        Check(editor.ApplyText(cfg, 15, "#12345678"), "HEX text not accepted");
        Check(!editor.ApplyText(cfg, 15, "#12"), "partial colour accepted");
        Check(cfg.NoteHighlightColors[7].Value == "#12345678", "partial input destroyed valid colour");
        editor.Adjust(cfg, 1, 5);
        Check(cfg.NoteHighlightColors[7].Value == "#1234567D", "alpha keyboard adjustment altered RGB");
        editor.SetComponent(cfg, 3, 999); editor.SetComponent(cfg, 0, -3);
        Check(cfg.NoteHighlightColors[7].Value == "#003456FF", "keyboard components not clamped");
        Check(cfg.NoteNormalColors[7].Value == "#FF0000FF", "highlight edit changed the normal palette");
        editor.ResetSelected(cfg);
        Check(cfg.NoteHighlightColors[7].Value == FallingConfig.DefaultHighlightColor, "selected reset failed");
        editor.ApplyText(cfg, 0, "#112233"); editor.ResetAll(cfg);
        for (int i = 0; i < 8; i++)
            Check(cfg.NoteNormalColors[i].Value == "#FF0000FF" && cfg.NoteHighlightColors[i].Value == FallingConfig.DefaultHighlightColor, "palette reset incomplete");
        Check(cfg.ChordColor.Value == FallingConfig.DefaultChordColor, "reset all omitted chord colour");
    }

    private static void ObsoleteLayout()
    {
        string path = Path.Combine(_root, "obsolete-layout.json");
        File.WriteAllText(path, "{\"Layout\":{\"NoteRotation\":true,\"RightToLeft\":true,\"LaneMapping\":\"Cycle\","
            + "\"FJGap\":180,\"FutureOption\":42},\"Style\":{\"ChordColor\":\"#102030\"},"
            + "\"NoteColorsNormal\":{\"F\":\"#112233\"}}");
        var settings = new SettingsFile(path, null); var cfg = new FallingConfig(settings);
        settings.Save();
        JObject data = Read(path);
        Check(data["Layout"]["NoteRotation"] == null && data["Layout"]["RightToLeft"] == null
            && data["Layout"]["LaneMapping"] == null, "removed layout options stayed in saved settings");
        Check(cfg.FJGap.Value == 180 && data["Layout"]["FutureOption"].Value<int>() == 42
            && cfg.ChordColor.Value == "#102030" && cfg.NoteNormalColors[3].Value == "#112233",
            "obsolete-option cleanup removed unrelated layout or colour settings");
    }

    private static void ChordColour()
    {
        string path = Path.Combine(_root, "chord-colour-settings.json");
        File.WriteAllText(path, "{}");
        var logger = new UnityModManager.ModEntry.ModLogger(); FallingPlugin.Log = new ModLog(logger);
        var settings = new SettingsFile(path, null); var cfg = new FallingConfig(settings);
        Check(cfg.ChordWindowMs.Value == 25 && FallingConfig.FormatNoteColor(cfg.ChordColorValue) == "#ADD8E6FF",
            "visual chord threshold / light blue defaults missing");
        cfg.ChordColor.Value = "#10203080"; cfg.ChordWindowMs.Value = 35;
        for (int lane = 0; lane < 8; lane++)
            foreach (bool head in new[] { false, true })
                Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(new NoteInstance { Lane = lane, IsGroupStart = head, IsChord = true })) == "#10203080",
                    "chord uniform colour did not override a normal/highlight lane colour");
        Check(FallingConfig.FormatNoteColor(cfg.NoteColorFor(new NoteInstance { Lane = 3, IsGroupStart = true })) == "#FFD966FF",
            "ordinary note lost its existing group highlight");
        settings.Save(); cfg = new FallingConfig(new SettingsFile(path, null));
        Check(cfg.ChordWindowMs.Value == 35 && FallingConfig.FormatNoteColor(cfg.ChordColorValue) == "#10203080",
            "chord colour / threshold did not round trip");
        cfg.ChordColor.Value = "invalid";
        for (int i = 0; i < 10; i++) Check(FallingConfig.FormatNoteColor(cfg.ChordColorValue) == "#ADD8E6FF",
            "invalid chord colour did not fall back to light blue");
        Check(logger.Messages.Count == 1, "invalid chord colour logged repeatedly");
        cfg.ChordColor.Value = "1,2,3,4";
        Check(FallingConfig.FormatNoteColor(cfg.ChordColorValue) == "#01020304", "corrected chord colour left the cache stale");
    }

    private static void LoadOnly()
    {
        string folder = Path.Combine(_root, "mod"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        // A repeat run starts from a known fixture, not the preceding run's settings.
        File.WriteAllText(path, "{\"General\":{\"AutoPlay\":true}}");
        _entry = new UnityModManager.ModEntry { Path = folder };
        Check(AdoFalling8K.Main.Load(_entry), "UMM Load returned false");
        Check(_entry.OnToggle != null && _entry.OnUnload != null && _entry.OnGUI != null && _entry.OnSaveGUI != null, "missing UMM callbacks");
        Check(GameObject.Created == 0 && FallingPlugin.Instance == null, "host started before toggle");
        Check(Read(path)["Layout"] != null, "default settings not persisted");
    }
    private static void Lifecycle()
    {
        int outsideHits = 0;
        scrPlayer original = scrController.instance.playerOne;
        original.onHit += delegate { outsideHits++; };
        RDC.auto = false;
        FallingPlugin runtime = null;
        Case("enable is idempotent and attaches one hit callback", delegate
        {
            Check(_entry.OnToggle(_entry, true), "enable failed");
            runtime = FallingPlugin.Instance;
            Tick(runtime); Tick(runtime);
            Check(_entry.OnToggle(_entry, true), "second enable failed");
            Check(ReferenceEquals(runtime, FallingPlugin.Instance) && GameObject.Created == 1, "duplicate runtime");
            Check(Handlers(original) == 2 && RDC.auto, "hook or auto-play not enabled");
            var clock = (GameClock)typeof(FallingPlugin).GetField("_clock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
            original.onHit(new scrFloor { seqID = 7 });
            Check(clock.HitCount == 1 && clock.LastHitSeqId == 7 && outsideHits == 1, "hit callbacks lost or duplicated");
        });
        Case("only F5 and F8 act as global shortcuts", delegate
        {
            var cfg = FallingPlugin.Cfg;
            cfg.LeadOffsetManual.Value = .321; cfg.HoldOffsetSeconds.Value = .123;
            cfg.HoldScale.Value = 1.7; cfg.LeadFraction.Value = .4;
            int loads = MacroPlan.Loads;
            foreach (KeyCode key in new[] { KeyCode.F3, KeyCode.F4, KeyCode.F6, KeyCode.F7, KeyCode.F9,
                KeyCode.Minus, KeyCode.Equals, KeyCode.Backspace, KeyCode.LeftBracket, KeyCode.RightBracket,
                KeyCode.End, KeyCode.PageUp, KeyCode.PageDown })
            {
                Input.Down.Add(key); Tick(runtime); Input.Clear();
                Check(cfg.LeadOffsetManual.Value == .321 && cfg.HoldOffsetSeconds.Value == .123
                    && cfg.HoldScale.Value == 1.7 && cfg.LeadFraction.Value == .4 && MacroPlan.Loads == loads,
                    "removed global shortcut still acted: " + key);
            }
            var view = (FallingView)typeof(FallingPlugin).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
            Input.Down.Add(KeyCode.F8); Tick(runtime); Input.Clear();
            Check(!view.Visible && !runtime.Tuning, "F8 did not toggle only the overlay");
            Input.Down.Add(KeyCode.F5); Tick(runtime); Input.Clear();
            GUISkin skin = GUI.skin; GUI.Labels.Clear(); Render(runtime);
            Check(runtime.Tuning && ReferenceEquals(GUI.skin, skin) && GUI.Labels.Contains(">"),
                "F5 did not open the real panel with a selected-row marker and isolated skin");
            Input.Down.Add(KeyCode.F5); Tick(runtime); Input.Clear();
            Check(!runtime.Tuning, "F5 did not close the panel");
        });
        Case("real panel keyboard and mouse share control definitions", delegate
        {
            runtime.TogglePanel(); PanelControls controls = Controls(runtime);
            Input.Down.Add(KeyCode.DownArrow); Tick(runtime); Render(runtime); Input.Clear();
            Check(controls.Selection == 1, "keyboard row selection did not move");
            bool before = FallingPlugin.Cfg.ShowLanes.Value;
            Input.Down.Add(KeyCode.Return); Tick(runtime); Render(runtime); Input.Clear();
            Check(FallingPlugin.Cfg.ShowLanes.Value != before, "Enter did not toggle the selected flag");
            PanelControl lane = controls.Rows[1];
            GUI.ClickLabel = lane.Label + "  " + lane.Display(); Tick(runtime); Render(runtime);
            Check(FallingPlugin.Cfg.ShowLanes.Value == before && controls.Selection == 1, "mouse did not invoke the same flag action");
            controls.Selection = 11;
            GUI.FocusControl("PanelNumber11");
            Input.Down.Add(KeyCode.Return); Tick(runtime); Render(runtime); Input.Clear();
            Check(FallingPlugin.Cfg.LeadOffsetManual.Value == .321 && GUI.Focused == null, "ending text edit reset its value");
            Input.Down.Add(KeyCode.Return); Tick(runtime); Render(runtime); Input.Clear();
            Check(FallingPlugin.Cfg.LeadOffsetManual.Value == .07, "Enter outside text editing did not reset the selected setting");
            controls.Selection = 33;
            Input.Down.Add(KeyCode.DownArrow); Tick(runtime); Render(runtime); Input.Clear();
            Vector2 scroll = (Vector2)typeof(ControlPanel).GetField("_scroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Panel(runtime));
            Check(controls.Selection == 34 && scroll.y > 0, "keyboard selection did not reveal bottom actions");
            Input.Down.Add(KeyCode.DownArrow); Tick(runtime); Render(runtime); Input.Clear();
            scroll = (Vector2)typeof(ControlPanel).GetField("_scroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Panel(runtime));
            Check(controls.Selection == 0 && scroll.y == 22, "wrapped selection did not reveal the top row");
            runtime.TogglePanel();
        });
        Case("file loading, stopping and alignment logs remain available in the panel", delegate
        {
            runtime.TogglePanel(); FallingPlugin.Cfg.FilePath.Value = "fixture.adofai";
            int loads = MacroPlan.Loads;
            GUI.ClickLabel = "载入 / 重启谱面"; Tick(runtime); Render(runtime);
            Check(MacroPlan.Active && MacroPlan.Loads == loads + 1, "panel load did not start file mode");
            GUI.ClickLabel = "退出文件模式"; Tick(runtime); Render(runtime);
            Check(!MacroPlan.Active, "panel stop did not leave file mode");
            int messages = _entry.Logger.Messages.Count;
            GUI.ClickLabel = "输出对齐日志"; Tick(runtime); Render(runtime);
            Check(_entry.Logger.Messages.Count > messages && _entry.Logger.Messages[_entry.Logger.Messages.Count - 1].StartsWith("alignment:"),
                "panel alignment action did not log the current timing");
            runtime.TogglePanel();
        });
        scrPlayer replacement = new scrPlayer();
        replacement.onHit += delegate { outsideHits++; };
        Case("player replacement detaches only the old mod callback", delegate
        {
            scrController.instance.playerOne = replacement; Tick(runtime);
            Check(Handlers(original) == 1 && Handlers(replacement) == 2, "old/new hook mismatch");
            original.onHit(new scrFloor()); replacement.onHit(new scrFloor());
            Check(outsideHits == 3, "outside callback lost");
        });
        Case("panel close and UMM Save persist edited options", delegate
        {
            runtime.TogglePanel();
            FallingPlugin.Cfg.LeadOffsetManual.Value = .321;
            runtime.TogglePanel();
            string path = Path.Combine(_entry.Path, "settings.json");
            Check(Read(path)["Chart"]["LeadOffsetSeconds"].Value<double>() == .321, "panel close did not save");
            FallingPlugin.Cfg.DebugHud.Value = true; _entry.OnSaveGUI(_entry);
            Check(Read(path)["General"]["DebugHud"].Value<bool>(), "UMM save callback did not save");
        });
        Case("disable releases hooks and restores the previous auto-play state", delegate
        {
            MacroPlan.Active = true;
            Check(_entry.OnToggle(_entry, false), "disable failed");
            Check(FallingPlugin.Instance == null && !MacroPlan.Active && !RDC.auto, "runtime state not cleared");
            Check(Handlers(replacement) == 1 && RuntimeStats.PanelsDisposed == 1 && RuntimeStats.RenderersDisposed == 1, "resources not released");
            Tick(runtime); Check(Handlers(replacement) == 1, "stopped runtime rebound hooks");
        });
        Case("immediate re-enable survives delayed destruction of the old host", delegate
        {
            Check(_entry.OnToggle(_entry, true), "re-enable failed");
            FallingPlugin next = FallingPlugin.Instance;
            Check(!ReferenceEquals(next, runtime), "old runtime reused");
            UnityEngine.Object.FlushDestroy(); Tick(next);
            Check(ReferenceEquals(FallingPlugin.Instance, next) && Handlers(replacement) == 2 && RDC.auto, "old destroy disrupted new runtime");
            Check(RuntimeStats.PanelsDisposed == 1, "old runtime disposed twice");
            next.TogglePanel(); Render(next); next.TogglePanel();
        });
        Case("unload stops the runtime and clears UMM callbacks", delegate
        {
            Check(_entry.OnUnload(_entry), "unload failed");
            UnityEngine.Object.FlushDestroy();
            Check(_entry.OnToggle == null && _entry.OnUnload == null && _entry.OnGUI == null && _entry.OnSaveGUI == null, "callbacks retained");
            Check(FallingPlugin.Instance == null && Handlers(replacement) == 1 && !RDC.auto, "unload leaked state");
            Check(RuntimeStats.PanelsDisposed == 2 && RuntimeStats.RenderersDisposed == 2, "unload dispose mismatch");
        });
    }
    private static void AutoPlayRestore()
    {
        var driver = new AutoPlayDriver();
        RDC.auto = true; driver.Apply(false); Check(RDC.auto, "inactive driver changed game state");
        driver.Apply(true); RDC.auto = false; driver.Apply(true); Check(RDC.auto, "scene reset not reapplied");
        driver.Apply(false); Check(RDC.auto, "previous true state not restored");
        RDC.auto = false; driver.Apply(true); Check(RDC.auto, "enable failed");
        driver.Apply(false); Check(!RDC.auto, "previous false state not restored");
    }
}
