using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AdoFalling
{
    internal interface ISetting
    {
        JToken Serialize();
    }

    /// <summary>A mutable option shared by the renderer and the F5 control panel.</summary>
    internal sealed class Setting<T> : ISetting
    {
        public T Value { get; set; }
        public Setting(T value) { Value = value; }

        public JToken Serialize()
        {
            if (typeof(T).IsEnum) return new JValue(Value.ToString());
            if (typeof(T) == typeof(Color))
            {
                Color color = (Color)(object)Value;
                return new JObject(new JProperty("r", color.r), new JProperty("g", color.g),
                    new JProperty("b", color.b), new JProperty("a", color.a));
            }
            return Value == null ? JValue.CreateNull() : JToken.FromObject(Value);
        }
    }

    /// <summary>Sectioned JSON settings in the UMM mod directory, with atomic saves.</summary>
    internal sealed class SettingsFile
    {
        private readonly string _path;
        private readonly Action<string> _warn;
        private readonly Dictionary<string, Dictionary<string, ISetting>> _entries
            = new Dictionary<string, Dictionary<string, ISetting>>();
        private readonly JObject _data;
        private bool _malformed;

        public SettingsFile(string path, Action<string> warn)
        {
            _path = path;
            _warn = warn;
            _data = new JObject();
            if (!File.Exists(path)) return;
            try { _data = JObject.Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (JsonException ex)
            {
                _malformed = true;
                Warn("Cannot parse settings; defaults will be used and the original backed up: " + ex.Message);
            }
        }

        public Setting<T> Bind<T>(string section, string key, T fallback)
        {
            Dictionary<string, ISetting> entries;
            if (!_entries.TryGetValue(section, out entries))
            {
                entries = new Dictionary<string, ISetting>();
                _entries.Add(section, entries);
            }
            if (entries.ContainsKey(key)) throw new ArgumentException("Duplicate setting: " + section + "/" + key);
            T value = fallback;
            JObject group = _data[section] as JObject;
            JToken token = group != null ? group[key] : null;
            if (token != null && token.Type != JTokenType.Null)
            {
                try
                {
                    if (typeof(T).IsEnum)
                    {
                        object parsed = Enum.Parse(typeof(T), token.ToString(), true);
                        if (!Enum.IsDefined(typeof(T), parsed)) throw new FormatException("Unknown enum value");
                        value = (T)parsed;
                    }
                    else if (typeof(T) == typeof(Color))
                    {
                        JObject rgba = token as JObject;
                        if (rgba == null) throw new FormatException("Expected RGBA object");
                        value = (T)(object)new Color(ColorComponent(rgba, "r"), ColorComponent(rgba, "g"),
                            ColorComponent(rgba, "b"), ColorComponent(rgba, "a"));
                    }
                    else value = token.ToObject<T>();
                    if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
                    {
                        double number = Convert.ToDouble(value);
                        if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Non-finite number");
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is ArgumentException) && !(ex is FormatException) && !(ex is JsonException)
                        && !(ex is InvalidCastException) && !(ex is OverflowException)) throw;
                    value = fallback;
                    Warn("Invalid setting " + section + "/" + key + "; using default: " + ex.Message);
                }
            }
            var entry = new Setting<T>(value);
            entries.Add(key, entry);
            return entry;
        }

        public void Save()
        {
            foreach (var section in _entries)
            {
                JObject group = _data[section.Key] as JObject;
                if (group == null) { group = new JObject(); _data[section.Key] = group; }
                foreach (var entry in section.Value) group[entry.Key] = entry.Value.Serialize();
            }
            string parent = Path.GetDirectoryName(Path.GetFullPath(_path));
            Directory.CreateDirectory(parent);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, _data.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (_malformed && File.Exists(_path))
                File.Copy(_path, _path + ".invalid.bak", true);
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
            _malformed = false;
        }

        private static float ColorComponent(JObject rgba, string name)
        {
            JToken token = rgba[name];
            if (token == null || token.Type == JTokenType.Null) throw new FormatException("Missing color component " + name);
            float number = token.Value<float>();
            if (float.IsNaN(number) || float.IsInfinity(number)) throw new FormatException("Non-finite color component");
            return number;
        }

        private void Warn(string text) { if (_warn != null) _warn(text); }
    }
}
