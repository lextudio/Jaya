using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Jaya.Provider.FileSystem.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemServiceLinux : INativeFileSystemService
    {
        public Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            // Simple Linux implementation using /proc/mounts or lsblk could be added.
            return Task.FromResult<DirectoryModel?>(new DirectoryModel());
        }
    }
}
