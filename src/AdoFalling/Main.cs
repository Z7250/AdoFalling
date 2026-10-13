using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace AdoFalling8K
{
    /// <summary>Unity Mod Manager entry point and lifecycle ownership.</summary>
    public static class Main
    {
        private static GameObject _host;
        private static SettingsFile _settings;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            try
            {
                FallingPlugin.Log = new ModLog(entry.Logger);
                _settings = new SettingsFile(Path.Combine(entry.Path, "settings.json"), FallingPlugin.Log.LogWarning);
                FallingPlugin.Cfg = new FallingConfig(_settings);
                _settings.Save();
                entry.OnToggle = OnToggle;
                entry.OnGUI = OnGUI;
                entry.OnSaveGUI = delegate { SaveSettings(); };
                entry.OnUnload = OnUnload;
                FallingPlugin.Log.LogInfo(PluginInfo.Name + " " + PluginInfo.Version + " loaded through UMM");
                return true;
            }
            catch (Exception ex)
            {
                entry.Logger.Error("AdoFalling8K load failed: " + ex);
                return false;
            }
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool enabled)
        {
            try
            {
                if (!enabled) { Stop(); return true; }
                if (_host != null) return true;
                _host = new GameObject(PluginInfo.Name);
                _host.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_host);
                FallingPlugin runtime = _host.AddComponent<FallingPlugin>();
                runtime.Initialize();
                _host.SetActive(true);
                return true;
            }
            catch (Exception ex)
            {
                Stop();
                entry.Logger.Error("AdoFalling8K toggle failed: " + ex);
                return false;
            }
        }

        private static void Stop()
        {
            if (_host == null) return;
            FallingPlugin runtime = _host.GetComponent<FallingPlugin>();
            if (runtime != null)
            {
                runtime.enabled = false;
                runtime.Shutdown();
            }
            UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            Stop();
            SaveSettings();
            entry.OnToggle = null;
            entry.OnGUI = null;
            entry.OnSaveGUI = null;
            entry.OnUnload = null;
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            FallingConfig cfg = FallingPlugin.Cfg;
            GUILayout.Label("ASDF / JKL;   |   F5: control panel   |   F8: overlay");
            GUILayout.Label("Settings: " + Path.Combine(entry.Path, "settings.json"));
            FallingPlugin runtime = FallingPlugin.Instance;
            if (runtime != null && GUILayout.Button("Open / close control panel")) runtime.TogglePanel();
        }

        internal static void SaveSettings()
        {
            if (_settings == null) return;
            try { _settings.Save(); }
            catch (Exception ex) { FallingPlugin.Log.LogWarning("Cannot save settings: " + ex.Message); }
        }
    }

    /// <summary>Routes existing diagnostics to UMM's log.</summary>
    internal sealed class ModLog
    {
        private readonly UnityModManager.ModEntry.ModLogger _logger;
        public ModLog(UnityModManager.ModEntry.ModLogger logger) { _logger = logger; }
        public void LogInfo(object value) { _logger.Log(Convert.ToString(value)); }
        public void LogWarning(object value) { _logger.Warning(Convert.ToString(value)); }
        public void LogError(object value) { _logger.Error(Convert.ToString(value)); }
    }
}
