//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Base;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jaya.Shared.Services
{
    public sealed class ConfigurationService: IConfigurationService
    {
        readonly string _configurationFilePathFormat;

        public ConfigurationService()
        {
            ConfigurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jaya");
            _configurationFilePathFormat = Path.Combine(ConfigurationDirectory, "config_{0}.json");
        }

        ~ConfigurationService()
        {

        }

        public string ConfigurationDirectory { get; }

        public T? Get<T>(string? key = null) where T : ConfigModelBase
        {
            var type = typeof(T);

            if (string.IsNullOrEmpty(key))
                key = GetUsableKey(type);

            var fileInfo = new FileInfo(string.Format(_configurationFilePathFormat, key));
            if (fileInfo.Exists)
            {
                var json = File.ReadAllText(fileInfo.FullName);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    IncludeFields = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                return JsonSerializer.Deserialize(json, type, options) as T;
            }

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

            // create configuration directory if missing
            var fileInfo = new FileInfo(string.Format(_configurationFilePathFormat, key));
            if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
            {
                if (!string.IsNullOrEmpty(fileInfo.DirectoryName))
                    Directory.CreateDirectory(fileInfo.DirectoryName);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(value, type, options);
            File.WriteAllText(fileInfo.FullName, json);
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
