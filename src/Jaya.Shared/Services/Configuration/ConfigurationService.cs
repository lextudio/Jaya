//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Base;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using System.Buffers;

namespace Jaya.Shared.Services
{
    public sealed class ConfigurationService: IConfigurationService
    {
        static readonly ILogger Logger = Log.ForContext(typeof(ConfigurationService)).ForContext("SourceContext", "Settings");
        readonly string _settingsFilePath;
        readonly object _sync = new object();
        readonly Dictionary<string, JsonElement> _sections = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        int _schemaVersion = 1;
        // No periodic/automatic saves: persistence will occur only when FlushSave() is called

        public ConfigurationService()
        {
            ConfigurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jaya");
            _settingsFilePath = Path.Combine(ConfigurationDirectory, "settings.json");

            // Load settings if present
            try
            {
                LoadAll();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed loading settings.json during startup");
            }
        }

        public string ConfigurationDirectory { get; }

        void LoadAll()
        {
            lock (_sync)
            {
                _sections.Clear();
                if (!File.Exists(_settingsFilePath))
                {
                    Logger.Information("Settings file not found: {Path}", _settingsFilePath);
                    return;
                }

                var json = File.ReadAllText(_settingsFilePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    Logger.Warning("settings.json root is not an object: {Path}", _settingsFilePath);
                    return;
                }

                if (root.TryGetProperty("schemaVersion", out var ver) && ver.ValueKind == JsonValueKind.Number && ver.TryGetInt32(out var v))
                    _schemaVersion = v;

                foreach (var prop in root.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Clone the element so it is independent of the JsonDocument lifetime
                    _sections[prop.Name] = prop.Value.Clone();
                }

                Logger.Information("Loaded settings: Path={Path} Sections={Count}", _settingsFilePath, _sections.Count);
            }
        }

        void SaveAll()
        {
            lock (_sync)
            {
                if (!Directory.Exists(ConfigurationDirectory))
                    Directory.CreateDirectory(ConfigurationDirectory);

                var tempPath = _settingsFilePath + ".tmp";

                // Build an intermediate dictionary for serialization
                var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                root["schemaVersion"] = _schemaVersion;
                foreach (var kv in _sections)
                {
                    // store raw JsonElement by converting to JsonDocument then to object
                    try
                    {
                        // Deserialize element to object to preserve structure
                        var obj = JsonSerializer.Deserialize<object>(kv.Value.GetRawText());
                        root[kv.Key] = obj;
                    }
                    catch
                    {
                        // fallback: store raw text
                        root[kv.Key] = JsonDocument.Parse(kv.Value.GetRawText()).RootElement.Clone();
                    }
                }

                var bytes = JsonSerializer.SerializeToUtf8Bytes(root);
                File.WriteAllBytes(tempPath, bytes);

                // Replace existing file atomically where possible
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        File.Delete(_settingsFilePath);
                    }
                    File.Move(tempPath, _settingsFilePath);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to atomically replace settings file; falling back to overwrite");
                    File.Copy(tempPath, _settingsFilePath, true);
                    File.Delete(tempPath);
                }

                Logger.Information("Saved settings: Path={Path} Bytes={Bytes} Sections={Count}", _settingsFilePath, bytes.Length, _sections.Count);
            }
        }

        public void FlushSave()
        {
            lock (_sync)
            {
                SaveAll();
            }
        }

        public T? Get<T>(string? key = null) where T : ConfigModelBase
        {
            var type = typeof(T);
            if (string.IsNullOrEmpty(key))
                key = GetUsableKey(type);

            lock (_sync)
            {
                if (_sections.TryGetValue(key, out var elem))
                {
                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            IncludeFields = false,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        };

                        var result = elem.Deserialize<T>(options);
                        Logger.Information("Loaded configuration section: Key={Key} Type={Type}", key, type.Name);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed deserializing section {Key} to {Type}", key, type.Name);
                        return default;
                    }
                }
            }

            Logger.Information("Configuration section not found: Key={Key} Type={Type}", key, type.Name);
            return default;
        }

        public T GetOrDefault<T>(string? key = null) where T : ConfigModelBase
        {
            var config = Get<T>(key) ?? ConfigModelBase.Empty<T>();
            return config;
        }

        public void Set<T>(T value, string? key = null)
        {
            var type = typeof(T);
            if (string.IsNullOrEmpty(key))
                key = GetUsableKey(type);

            lock (_sync)
            {
                try
                {
                    var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                    var elem = JsonSerializer.SerializeToElement(value, options);
                    _sections[key] = elem;
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed persisting section {Key} Type={Type}", key, type.Name);
                }
            }
        }

        string GetUsableKey(Type type)
        {
            var name = type.Name;

            var invalidChars = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            foreach (var invalidChar in invalidChars)
                name = name.Replace(invalidChar, '_');

            return name;
        }
    }
}
