using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jaya.IO
{
    public static class VolumeFilter
    {
        public static IReadOnlyList<Models.VolumeModel> ApplyFilters(IReadOnlyList<Models.VolumeModel> volumes)
        {
            if (volumes == null || volumes.Count == 0)
                return Array.Empty<Models.VolumeModel>();

            var result = new List<Models.VolumeModel>(volumes.Count);
            var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (var vol in volumes)
            {
                if (vol == null)
                    continue;

                var mount = NormalizeMountPoint(vol.MountPoint);
                if (string.IsNullOrWhiteSpace(mount))
                    continue;

                if (!seen.Add(mount))
                    continue;

                var lower = mount.ToLowerInvariant();

                if (!OperatingSystem.IsWindows())
                {
                    if (lower.StartsWith("/proc") || lower.StartsWith("/sys") || lower.StartsWith("/run") || lower.StartsWith("/dev") || lower.StartsWith("/var") || lower.StartsWith("/private") || lower.StartsWith("/system/volumes"))
                        continue;

                    if (lower.Contains("/snap/") || lower.Contains("/containers/") || lower.Contains("/core") || lower.Contains("/gvfs") || lower.Contains("/library/developer/coresimulator"))
                        continue;
                }

                var flags = vol.Flags;
                if (flags != null && flags.Count > 0)
                {
                    bool hasFlag(string name) => flags.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
                    if (hasFlag("simulator") || hasFlag("snapshot") || hasFlag("disk-image") || hasFlag("synthesized") || hasFlag("no-device-id"))
                        continue;

                    if (flags.Any(f => f != null && f.StartsWith("system-", StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                if (!vol.IsInternal && !vol.IsRemovable && string.IsNullOrWhiteSpace(vol.Name))
                    continue;

                result.Add(vol);
            }

            return result;
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
}
