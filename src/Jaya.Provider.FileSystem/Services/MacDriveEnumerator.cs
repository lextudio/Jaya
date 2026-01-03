using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Jaya.Provider.FileSystem.Services
{
    record MacVolume(
        string MountPoint,
        string? VolumeName,
        string? DeviceIdentifier,
        bool? Internal,
        bool? RemovableMedia,
        bool? Ejectable,
        string? BusProtocol);

    static class MacDriveEnumerator
    {
        static readonly ILogger DiskUtilLogger = Log.ForContext("Category", "FileSystem")
                                                   .ForContext("Area", "MacDriveEnumerator");

        public static IReadOnlyList<MacVolume> GetSystemAndExternalVolumes()
        {
            var mountPoints = GetFinderVisibleMountPoints();
            var result = new List<MacVolume>(mountPoints.Count);

            foreach (var mp in mountPoints)
            {
                var plistXml = RunDiskUtil(mp);

                // If we didn't get mount-level info, try to resolve the underlying device node (via df)
                string resolvedDeviceNode = null;
                if (string.IsNullOrWhiteSpace(plistXml))
                {
                    try
                    {
                        resolvedDeviceNode = GetDeviceNodeFromMount(mp);
                        if (!string.IsNullOrEmpty(resolvedDeviceNode))
                        {
                            var devicePath = resolvedDeviceNode.StartsWith("/dev/") ? resolvedDeviceNode : $"/dev/{resolvedDeviceNode}";
                            plistXml = RunDiskUtil(devicePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        DiskUtilLogger.Verbose(ex, "Failed to resolve device node for mount {MountPoint}", mp);
                    }
                }

                if (string.IsNullOrWhiteSpace(plistXml))
                {
                    result.Add(new MacVolume(mp, null, resolvedDeviceNode, null, null, null, null));
                    continue;
                }

                var dict = ParsePlistDict(plistXml);

                var deviceId = GetString(dict, "DeviceIdentifier") ?? GetString(dict, "DeviceNode") ?? resolvedDeviceNode;
                var busProto = GetString(dict, "BusProtocol") ?? GetString(dict, "Protocol");
                var volumeName = GetString(dict, "VolumeName");
                if (string.IsNullOrEmpty(volumeName) && mp != "/")
                    volumeName = Path.GetFileName(mp);

                result.Add(new MacVolume(
                    MountPoint: GetString(dict, "MountPoint") ?? mp,
                    VolumeName: volumeName,
                    DeviceIdentifier: deviceId,
                    Internal: GetBool(dict, "Internal"),
                    RemovableMedia: GetBool(dict, "RemovableMedia") ?? GetBool(dict, "Removable Media"),
                    Ejectable: GetBool(dict, "Ejectable"),
                    BusProtocol: busProto
                ));
            }

            return DeduplicateByMountPoint(result);
        }

        static IReadOnlyList<MacVolume> DeduplicateByMountPoint(List<MacVolume> volumes)
        {
            var seen = new Dictionary<string, MacVolume>(StringComparer.OrdinalIgnoreCase);
            foreach (var volume in volumes)
            {
                var key = volume.MountPoint ?? Guid.NewGuid().ToString();
                if (!seen.ContainsKey(key))
                    seen[key] = volume;
            }

            return seen.Values.ToList();
        }

        static readonly HashSet<string> ReservedFinderVolumeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ".timemachine",
            "com.apple.TimeMachine.localsnapshots"
        };

        static List<string> GetFinderVisibleMountPoints()
        {
            var mps = new List<string> { "/" };

            if (Directory.Exists("/Volumes"))
            {
                foreach (var dir in Directory.EnumerateDirectories("/Volumes"))
                {
                    if (IsReservedFinderVolume(dir))
                        continue;

                    mps.Add(dir);
                }
            }

            return mps.Distinct(StringComparer.Ordinal).ToList();
        }

        static bool IsReservedFinderVolume(string mountPoint)
        {
            if (string.IsNullOrEmpty(mountPoint))
                return false;

            var trimmed = mountPoint.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed))
                return false;

            var name = Path.GetFileName(trimmed);
            return ReservedFinderVolumeNames.Contains(name);
        }

        static string? RunDiskUtil(string mountPoint)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/sbin/diskutil",
                Arguments = $"info -plist \"{mountPoint}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            try
            {
                using var p = Process.Start(psi);
                if (p is null)
                    return null;

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (!string.IsNullOrEmpty(stdout))
                    DiskUtilLogger.Verbose("diskutil info -plist {MountPoint} stdout: {Output}", mountPoint, stdout.Trim());

                if (!string.IsNullOrEmpty(stderr))
                    DiskUtilLogger.Verbose("diskutil info -plist {MountPoint} stderr: {Output}", mountPoint, stderr.Trim());

                if (p.ExitCode != 0)
                {
                    DiskUtilLogger.Warning("diskutil info -plist {MountPoint} exited with {ExitCode}", mountPoint, p.ExitCode);
                    return null;
                }

                return stdout;
            }
            catch (Exception ex)
            {
                DiskUtilLogger.Warning(ex, "diskutil info -plist {MountPoint} failed", mountPoint);
                return null;
            }
        }

        static XElement ParsePlistDict(string plistXml)
            => XDocument.Parse(plistXml).Descendants("dict").First();

        static XElement? FindValue(XElement dict, string key)
        {
            var keyEl = dict.Elements("key").FirstOrDefault(k => k.Value == key);
            return keyEl?.ElementsAfterSelf().OfType<XElement>().FirstOrDefault();
        }

        static string? GetString(XElement dict, string key)
            => FindValue(dict, key)?.Name.LocalName == "string" ? FindValue(dict, key)!.Value : null;

        static bool? GetBool(XElement dict, string key)
        {
            var value = FindValue(dict, key);
            if (value is null)
                return null;

            return value.Name.LocalName switch
            {
                "true" => true,
                "false" => false,
                _ => null
            };
        }

        static string? GetDeviceNodeFromMount(string mountPoint)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/df",
                    Arguments = $"-P \"{mountPoint}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                using var p = Process.Start(psi);
                if (p is null)
                    return null;

                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 || string.IsNullOrEmpty(stdout))
                    return null;

                var lines = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                    return null;

                var parts = lines[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return null;

                var device = parts[0];
                if (device.StartsWith("/dev/"))
                    return device.Substring(5);

                return device;
            }
            catch (Exception ex)
            {
                DiskUtilLogger.Verbose(ex, "GetDeviceNodeFromMount failed for {MountPoint}", mountPoint);
                return null;
            }
        }
    }
}
