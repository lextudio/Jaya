using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Jaya.Provider.FileSystem.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemServiceWindows : INativeFileSystemService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemServiceWindows>();
        public Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            // Simple Windows implementation using DriveInfo for now.
            return Task.FromResult<DirectoryModel?>(new DirectoryModel());
        }

        public Task<bool> DeleteAsync(IEnumerable<FileSystemObjectModel> items, DeleteMode mode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            return Task.Run(() =>
            {
                var anyDeleted = false;
                foreach (var item in items)
                {
                    var path = item?.Path;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var deleted = mode == DeleteMode.Trash ? TryMoveToRecycleBin(path) : TryDelete(path);
                    if (deleted)
                        anyDeleted = true;
                }

                return anyDeleted;
            });
        }

        internal static void AddGetDrives(DirectoryModel model)
        {
            try
            {
                foreach (var driveInfo in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!driveInfo.IsReady)
                            continue;

                        var drive = new DirectoryModel(true);
                        drive.Name = driveInfo.Name;
                        drive.Path = driveInfo.RootDirectory.FullName;
                        drive.Size = driveInfo.TotalSize;
                        drive.IsExternalDrive = false;
                        Logger.Debug("Windows drive {Name}@{Path} IsExternalDrive={IsExternalDrive}",
                            drive.Name,
                            drive.Path,
                            drive.IsExternalDrive);
                        model.Directories.Add(drive);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        static bool TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    return true;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to delete {Path}", path);
            }

            return false;
        }

        static bool TryMoveToRecycleBin(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var from = path + '\0' + '\0';
                var operation = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = from,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
                };

                var result = SHFileOperation(ref operation);
                if (result == 0 && !operation.fAnyOperationsAborted)
                    return true;

                Logger.Warning("Recycle bin move failed for {Path} (Result={Result}, Aborted={Aborted})",
                    path,
                    result,
                    operation.fAnyOperationsAborted);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Recycle bin move failed for {Path}", path);
            }

            return false;
        }

        const uint FO_DELETE = 0x0003;
        const ushort FOF_ALLOWUNDO = 0x0040;
        const ushort FOF_NOCONFIRMATION = 0x0010;
        const ushort FOF_NOERRORUI = 0x0400;
        const ushort FOF_SILENT = 0x0004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
