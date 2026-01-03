using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Jaya.Provider.FileSystem.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemServiceWindows : INativeFileSystemService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemServiceWindows>();
        public Task<DirectoryModel> GetDirectoryAsync(AccountModelBase account, DirectoryModel directory = null)
        {
            // Simple Windows implementation using DriveInfo for now.
            return Task.FromResult(new DirectoryModel());
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
    }
}
