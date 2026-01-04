//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Provider.FileSystem.Models;
using Jaya.Provider.FileSystem.Views;
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Serilog;
using System.IO.Pipelines;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemService : ProviderServiceBase, IProviderService
    {
        readonly INativeFileSystemService _impl;
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<DirectoryModel?>> _inflight = new();

        public FileSystemService()
        {
            // Detect platform and instantiate the appropriate implementation
            if (OperatingSystem.IsMacOS())
                _impl = new FileSystemServiceMac();
            else if (OperatingSystem.IsWindows())
                _impl = new FileSystemServiceWindows();
            else
                _impl = new FileSystemServiceLinux();

            Name = "File System";
            ImagePath = "avares://Jaya.Provider.FileSystem/Assets/Images/Computer-32.png";
            Description = "View your local drives, inspect their properties and play with directories & files stored within them.";
            IsRootDrive = true;
            ConfigurationEditorType = typeof(ConfigurationView);
        }

        public override async Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            Log.Debug("FileSystemService.GetDirectoryAsync called: Account={Account}, Path={Path}", account?.Name, directory?.Path);
            var model = GetFromCache(account, directory);
            if (model != null)
                return model;

            var key = $"{account?.Name ?? "__null"}:{directory?.Path ?? "__root"}";

            // If there's already an in-flight request for the same key, return it
            var task = _inflight.GetOrAdd(key, _ => FetchAndCacheAsync(account, directory, key));
            try
            {
                return await task;
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }
        }

        async Task<DirectoryModel?> FetchAndCacheAsync(AccountModelBase account, DirectoryModel? directory, string key)
        {
            Log.Debug("Fetching directory for key={Key} on thread {Thread}", key, Environment.CurrentManagedThreadId);
            var result = await _impl.GetDirectoryAsync(account, directory);
            AddToCache(account, result);
            return result;
        }

        protected override Task<AccountModelBase> AddAccountAsync(AccountModelBase account = null)
        {
            throw new NotImplementedException();
        }

        protected override Task<bool> RemoveAccountAsync(AccountModelBase account)
        {
            throw new NotImplementedException();
        }

        public override async Task<IEnumerable<AccountModelBase>> GetAccountsAsync()
        {
            var providers = new List<AccountModelBase>
            {
                new AccountModel()
            };

            return await Task.Run(() => providers);
        }

        public override Task FormatAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            throw new NotImplementedException();
        }
    }
}
