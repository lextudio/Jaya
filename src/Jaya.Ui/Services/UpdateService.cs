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
    public sealed class UpdateService : IService
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

            Version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            var v = Version;
            VersionString = string.Format("{0}.{1}.{2}.{3}", v.Major, v.Minor, v.Build, v.Revision);
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

            if (releases != null && releases.Length > 0 && releases[0] != null)
            {
                var first = releases[0];
                if (first != null && Version.CompareTo(first.Version) < 0 && first.Downloads != null && first.Downloads.Length > 0 && _sharedService?.UpdateConfiguration != null)
                {
                    _sharedService.UpdateConfiguration.Update = first;
                }
            }

            if (_sharedService != null && _sharedService.UpdateConfiguration != null)
            {
                _sharedService.UpdateConfiguration.Checked = DateTime.Now;
            }
            // Update the checked timestamp once and persist below.
            if (releases != null && releases.Length > 0 && releases[0] != null)
            {
                var latest = releases[0];
                if (latest != null && latest.Downloads != null && latest.Downloads.Length > 0 && Version.CompareTo(latest.Version) < 0)
                {
                    if (_sharedService?.UpdateConfiguration != null)
                        _sharedService.UpdateConfiguration.Update = latest;
                }
            }

            if (_sharedService != null && _sharedService.UpdateConfiguration != null)
            {
                _sharedService.UpdateConfiguration.Checked = DateTime.Now;
            }
        }

        public async Task DownloadUpdate()
        {
            if (Update == null)
                return;

            var platform = _platformService != null ? _platformService.GetPlatform() : OSPlatform.Create("unknown");
            var updateFilePrefix = string.Format("{0}{1}", platform.ToString(), _isPortable ? "_portable" : string.Empty);

            Uri? url = null;
            foreach (var download in Update?.Downloads ?? Array.Empty<ReleaseAssetModel>())
            {
                if (download?.Url == null)
                    continue;

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
            if (response.IsSuccessStatusCode && _sharedService != null && _sharedService.UpdateConfiguration != null)
            {
                var downloadDir = _sharedService.UpdateConfiguration.DownloadDirectory ?? Path.GetTempPath();
                if (!Directory.Exists(downloadDir))
                    Directory.CreateDirectory(downloadDir);

                var updateFilePath = Path.Combine(downloadDir, Path.GetFileName(url.LocalPath));
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.OpenWrite(updateFilePath);
                await src.CopyToAsync(dst);
            }
        }
    }
}
