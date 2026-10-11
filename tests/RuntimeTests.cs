using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AdoFalling;
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
        typeof(FallingPlugin).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
    }
    private static int Handlers(scrPlayer player) { return player.onHit == null ? 0 : player.onHit.GetInvocationList().Length; }
    private static int Main(string[] args)
    {
        try
        {
            _root = args[0]; Directory.CreateDirectory(_root);
            FallingPlugin.Log = new ModLog(new UnityModManager.ModEntry.ModLogger());
            Case("settings round trip preserves options and unknown keys", RoundTrip);
            Case("invalid settings fall back with diagnostics", InvalidValues);
            Case("malformed JSON is backed up before defaults are saved", Malformed);
            Case("file hotkey migration leaves non-lane hotkeys intact", HotkeyMigration);
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
        cfg.LaneMapping.Value = LaneMapping.Cycle;
        cfg.LaneColor.Value = new Color(.1f, .2f, .3f, .4f);
        cfg.LoadKey.Value = KeyCode.F3;
        settings.Save(); settings.Save();
        cfg = new FallingConfig(new SettingsFile(path, null));
        Check(cfg.NoteSpeedPxPerSec.Value == 733.5f && cfg.LeadOffsetManual.Value == -.125, "numeric values changed");
        Check(cfg.AutoPlay.Value && cfg.FilePath.Value == @"D:\谱面\测试.adofai", "bool or unicode path changed");
        Check(cfg.LaneMapping.Value == LaneMapping.Cycle && cfg.LoadKey.Value == KeyCode.F3, "enum values changed");
        Check(Math.Abs(cfg.LaneColor.Value.a - .4f) < .00001, "color alpha changed");
        Check(Read(path)["Custom"]["Retained"].Value<int>() == 42 && Read(path)["General"]["FutureOption"].Value<bool>(), "unknown settings lost");
        Check(!File.Exists(path + ".tmp"), "temporary file left after atomic save");
    }
    private static void InvalidValues()
    {
        string path = Path.Combine(_root, "invalid.json");
        File.WriteAllText(path, "{\"General\":{\"ToggleKey\":\"InvalidKey\",\"DemoBpm\":\"NaN\",\"AutoPlay\":{}},"
            + "\"Style\":{\"LaneColor\":{\"r\":1},\"JudgeLineColor\":{\"r\":\"Infinity\",\"g\":1,\"b\":1,\"a\":1}}}");
        var warnings = new List<string>();
        var cfg = new FallingConfig(new SettingsFile(path, warnings.Add));
        Check(cfg.ToggleKey.Value == KeyCode.F8 && cfg.DemoBpm.Value == 150 && !cfg.AutoPlay.Value, "bad primitives accepted");
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
        foreach (string key in new[] { "A", "S", "D", "F", "J", "K", "L", "Semicolon", "F3" })
        {
            File.WriteAllText(path, "{\"General\":{\"LoadKey\":\"" + key + "\"}}");
            var cfg = new FallingConfig(new SettingsFile(path, null));
            Check(cfg.LoadKey.Value == (key == "F3" ? KeyCode.F3 : KeyCode.F4), "hotkey migration failed: " + key);
        }
    }
    private static UnityModManager.ModEntry _entry;
    private static void LoadOnly()
    {
        string folder = Path.Combine(_root, "mod"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        // A repeat run starts from a known fixture, not the preceding run's settings.
        File.WriteAllText(path, "{\"General\":{\"AutoPlay\":true}}");
        _entry = new UnityModManager.ModEntry { Path = folder };
        Check(AdoFalling.Main.Load(_entry), "UMM Load returned false");
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
