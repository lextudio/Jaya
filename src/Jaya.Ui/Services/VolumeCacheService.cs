using Jaya.IO;
using Jaya.IO.Models;
using Jaya.Shared.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.Ui.Services
{
    public sealed class VolumeCacheService : IService, IDisposable
    {
        static readonly ILogger Logger = Log.ForContext(typeof(VolumeCacheService)).ForContext("SourceContext", "FileSystem");
        readonly IFileSystem _fileSystem = FileSystem.Default;
        readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
        Timer? _refreshTimer;

        IReadOnlyList<VolumeModel> _volumes = Array.Empty<VolumeModel>();
        DateTime _lastRefreshUtc = DateTime.MinValue;

        public event EventHandler<VolumeCacheChangedEventArgs>? VolumesChanged;

        public IReadOnlyList<VolumeModel> CurrentVolumes => _volumes;

        public DateTime LastRefreshUtc => _lastRefreshUtc;

        public void WarmUp()
        {
            Logger.Debug("Volume cache warmup requested.");
            _ = RefreshAsync();

            if (_refreshTimer == null)
            {
                Logger.Debug("Volume cache refresh timer starting. Interval={Interval}", _refreshInterval);
                _refreshTimer = new Timer(_ => _ = RefreshAsync(), null, _refreshInterval, _refreshInterval);
            }
        }

        public void EnsureFresh(TimeSpan maxAge)
        {
            if (_volumes.Count == 0 || (DateTime.UtcNow - _lastRefreshUtc) > maxAge)
            {
                Logger.Debug("Volume cache stale or empty. Refreshing. Count={Count} AgeSeconds={AgeSeconds}",
                    _volumes.Count,
                    (DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
                _ = RefreshAsync();
            }
        }

        public async Task<IReadOnlyList<VolumeModel>> GetVolumesSnapshotAsync(bool allowStale = true)
        {
            if (allowStale && _volumes.Count > 0)
            {
                Logger.Debug("Volume cache snapshot returned (stale allowed). Count={Count}", _volumes.Count);
                return _volumes;
            }

            Logger.Debug("Volume cache snapshot requested (stale not allowed or empty). Refreshing.");
            await RefreshAsync().ConfigureAwait(false);
            return _volumes;
        }

        public async Task RefreshAsync()
        {
            if (!await _refreshLock.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                Logger.Debug("Volume cache refresh started.");
                var previous = _volumes;
                var volumes = await _fileSystem.GetVolumesAsync(CancellationToken.None).ConfigureAwait(false);
                _volumes = volumes ?? Array.Empty<VolumeModel>();
                _lastRefreshUtc = DateTime.UtcNow;
                Logger.Debug("Volume cache refreshed. Count={Count}", _volumes.Count);

                if (!AreVolumesEquivalent(previous, _volumes))
                {
                    Logger.Debug("Volume cache changed. Count={Count}", _volumes.Count);
                    VolumesChanged?.Invoke(this, new VolumeCacheChangedEventArgs(_volumes));
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Volume cache refresh failed");
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public void Dispose()
        {
            _refreshTimer?.Dispose();
            _refreshLock.Dispose();
        }

        static bool AreVolumesEquivalent(IReadOnlyList<VolumeModel> left, IReadOnlyList<VolumeModel> right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left.Count != right.Count)
                return false;

            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var set = new HashSet<string>(comparer);
            for (int i = 0; i < left.Count; i++)
                set.Add(BuildVolumeKey(left[i]));

            for (int i = 0; i < right.Count; i++)
            {
                if (!set.Remove(BuildVolumeKey(right[i])))
                    return false;
            }

            return set.Count == 0;
        }

        static string BuildVolumeKey(VolumeModel volume)
        {
            var mount = NormalizeMountPoint(volume.MountPoint);
            return string.Concat(
                mount,
                "|",
                volume.Name ?? string.Empty,
                "|",
                volume.DeviceId ?? string.Empty,
                "|",
                volume.IsRemovable ? "1" : "0",
                "|",
                volume.IsInternal ? "1" : "0");
        }

        static string NormalizeMountPoint(string mountPoint)
        {
            if (string.IsNullOrWhiteSpace(mountPoint))
                return string.Empty;

            var trimmed = mountPoint.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed))
                return Path.GetPathRoot(mountPoint) ?? mountPoint;

            return trimmed;
        }
    }

    public sealed class VolumeCacheChangedEventArgs : EventArgs
    {
        public VolumeCacheChangedEventArgs(IReadOnlyList<VolumeModel> volumes)
        {
            Volumes = volumes;
        }

        public IReadOnlyList<VolumeModel> Volumes { get; }
    }
}
