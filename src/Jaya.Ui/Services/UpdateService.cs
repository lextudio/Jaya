//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Services;
using Jaya.Ui.Models;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jaya.Ui.Services
{
    public sealed class UpdateService: IService
    {
        const string GITHUB_API = "https://api.github.com/";

        readonly SharedService? _sharedService;
        readonly IPlatformService? _platformService;
        readonly bool _isPortable;

        public UpdateService()
        {
            _sharedService = ServiceLocator.Instance.GetService<SharedService>();
            _platformService = ServiceLocator.Instance.GetService<IPlatformService>();

            _isPortable = true;

            Version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionString = string.Format("{0}.{1}.{2}.{3}", Version.Major, Version.Minor, Version.Build, Version.Revision);
            Bitness = Environment.Is64BitOperatingSystem ? (byte)64 : (byte)32;
        }

        #region properties

        public Version Version { get; }

        public DateTime? Checked => _sharedService?.UpdateConfiguration?.Checked;

        public ReleaseModel? Update => _sharedService?.UpdateConfiguration?.Update;

        public string VersionString { get; }

        public byte Bitness { get; }

        #endregion

        public async Task CheckForUpdate()
        {
            using var http = new HttpClient { BaseAddress = new Uri(GITHUB_API) };
            // GitHub API requires a User-Agent header
            if (!http.DefaultRequestHeaders.Contains("User-Agent"))
                http.DefaultRequestHeaders.Add("User-Agent", "Jaya-App");

            var resp = await http.GetAsync("repos/lextudio/jaya/releases");
            resp.EnsureSuccessStatusCode();

            var stream = await resp.Content.ReadAsStreamAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var releases = await JsonSerializer.DeserializeAsync<ReleaseModel[]>(stream, options).ConfigureAwait(false);

            if (releases != null && releases.Length > 0 && Version.CompareTo(releases[0].Version) < 0 && releases[0].Downloads != null && releases[0].Downloads.Length > 0)
            {
                _sharedService.UpdateConfiguration.Update = releases[0];
            }

            _sharedService.UpdateConfiguration.Checked = DateTime.Now;
            _sharedService.SaveConfigurations();
        }

        public async Task DownloadUpdate()
        {
            if (Update == null)
                return;

            var platform = _platformService != null ? _platformService.GetPlatform() : OSPlatform.Create("unknown");
            var updateFilePrefix = string.Format("{0}{1}", platform.ToString(), _isPortable ? "_portable" : string.Empty);

            Uri? url = null;
            foreach(var download in Update?.Downloads ?? new ReleaseAssetModel[0])
            {
                if (download.Url.Contains(updateFilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    url = new Uri(download.Url, UriKind.Absolute);
                    break;
                }
            }

            if (url == null)
                return;

            using var http = new HttpClient();
            var response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                if (!Directory.Exists(_sharedService.UpdateConfiguration.DownloadDirectory))
                    Directory.CreateDirectory(_sharedService.UpdateConfiguration.DownloadDirectory);

                var updateFilePath = Path.Combine(_sharedService.UpdateConfiguration.DownloadDirectory, Path.GetFileName(url.LocalPath));
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.OpenWrite(updateFilePath);
                await src.CopyToAsync(dst);
            }
        }
    }
}
