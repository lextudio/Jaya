//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.IO;
using Io = Jaya.IO.Models;
using Jaya.Provider.FileSystem.Models;
using Jaya.Provider.FileSystem.Views;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemService : ProviderServiceBase, IProviderService, IFileDeleteService, IFileTransferService, Jaya.Shared.Services.IFileRenameService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemService>();
        readonly IFileSystem _fileSystem;
        readonly ConcurrentDictionary<string, Task<DirectoryModel?>> _inflight = new();

        public FileSystemService()
        {
            _fileSystem = Jaya.IO.FileSystem.Default;

            Name = "File System";
            ImagePath = "avares://Jaya.Provider.FileSystem/Assets/Images/Computer-32.png";
            Description = "View your local drives, inspect their properties and play with directories & files stored within them.";
            IsRootDrive = true;
            ConfigurationEditorType = typeof(ConfigurationView);
        }

        public override async Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            Log.Debug("FileSystemService.GetDirectoryAsync called: Account={Account}, Path={Path}", account.Name, directory?.Path);
            var accountNonNull = account;
            var model = GetFromCache(accountNonNull, directory);
            if (model != null)
                return model;

            var key = $"{account?.Name ?? "__null"}:{directory?.Path ?? "__root"}";
            var task = _inflight.GetOrAdd(key, _ => FetchAndCacheAsync(accountNonNull, directory));
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }
        }

        async Task<DirectoryModel?> FetchAndCacheAsync(AccountModelBase account, DirectoryModel? directory)
        {
            try
            {
                DirectoryModel? result;
                if (directory == null || string.IsNullOrWhiteSpace(directory.Path))
                {
                    result = await BuildRootAsync().ConfigureAwait(false);
                }
                else
                {
                    var info = await _fileSystem.GetDirectoryAsync(directory.Path, includeFiles: true, includeDirectories: true, CancellationToken.None)
                        .ConfigureAwait(false);
                    result = info == null ? null : MapDirectory(info);
                }

                if (result != null)
                    AddToCache(account, result);

                return result;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to fetch directory for {Path}", directory?.Path ?? "<root>");
                return null;
            }
        }

        async Task<DirectoryModel?> BuildRootAsync()
        {
            try
            {
                var volumes = await _fileSystem.GetVolumesAsync(CancellationToken.None).ConfigureAwait(false);
                var root = new DirectoryModel
                {
                    Directories = new List<DirectoryModel>(),
                    Files = new List<FileModel>()
                };

                foreach (var volume in volumes)
                {
                    var drive = new DirectoryModel(true)
                    {
                        Name = GetVolumeDisplayName(volume),
                        Path = volume.MountPoint,
                        IsExternalDrive = volume.IsRemovable || !volume.IsInternal
                    };
                    root.Directories.Add(drive);
                }

                return root;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Volume enumeration failed");
                return new DirectoryModel
                {
                    Directories = new List<DirectoryModel>(),
                    Files = new List<FileModel>()
                };
            }
        }

        protected override Task<AccountModelBase?> AddAccountAsync(AccountModelBase? account = null)
        {
            throw new NotImplementedException();
        }

        protected override Task<bool> RemoveAccountAsync(AccountModelBase? account)
        {
            throw new NotImplementedException();
        }

        public override Task<IEnumerable<AccountModelBase>> GetAccountsAsync()
        {
            var providers = new List<AccountModelBase>
            {
                new AccountModel()
            };

            return Task.FromResult<IEnumerable<AccountModelBase>>(providers);
        }

        public override Task FormatAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> DeleteAsync(AccountModelBase account, IEnumerable<FileSystemObjectModel> items, DeleteMode mode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var paths = items
                .Select(item => item?.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(comparer)
                .Select(path => path!)
                .ToList();

            if (paths.Count == 0)
                return false;

            try
            {
                var ioMode = mode == DeleteMode.Trash ? Io.DeleteMode.Trash : Io.DeleteMode.Permanent;
                var result = await _fileSystem.DeleteBatchAsync(paths, ioMode, null, CancellationToken.None).ConfigureAwait(false);
                return result.Results.Any(r => r.Success);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Delete failed");
                return false;
            }
        }

        public async Task<IReadOnlyList<FileSystemObjectModel>> TransferAsync(
            AccountModelBase account,
            IEnumerable<FileSystemObjectModel> items,
            DirectoryModel target,
            TransferMode mode,
            IProgress<TransferProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var targetPath = target.Path;
            if (string.IsNullOrWhiteSpace(targetPath))
                return Array.Empty<FileSystemObjectModel>();

            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var sources = items
                .Select(item => item?.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(comparer)
                .Select(path => path!)
                .ToList();

            if (sources.Count == 0)
                return Array.Empty<FileSystemObjectModel>();

            var ioMode = mode == TransferMode.Move ? Io.TransferMode.Move : Io.TransferMode.Copy;
            IProgress<Io.TransferProgressReport>? ioProgress = null;

            if (progress != null)
            {
                ioProgress = new Progress<Io.TransferProgressReport>(report =>
                {
                    var mapped = MapProgress(report, targetPath);
                    if (mapped != null)
                        progress.Report(mapped);
                });
            }

            IReadOnlyList<Io.CopyResult> results;
            try
            {
                results = await _fileSystem.TransferAsync(sources, targetPath, ioMode, ioProgress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Transfer failed to {Target}", targetPath);
                return Array.Empty<FileSystemObjectModel>();
            }

            var createdItems = new List<FileSystemObjectModel>();
            foreach (var result in results)
            {
                if (!result.Success)
                    continue;

                var created = CreateFileSystemObject(result.DestinationPath);
                if (created != null)
                    createdItems.Add(created);
            }

            return createdItems;
        }

        public async Task<FileSystemObjectModel?> RenameAsync(AccountModelBase account, FileSystemObjectModel item, string newName, bool overwrite = false, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentNullException(nameof(newName));

            var parentDir = Path.GetDirectoryName(item.Path) ?? string.Empty;
            var destination = Path.Combine(parentDir, newName);
            Logger.Debug("FileSystemService.RenameAsync: account={Account} source={Source} newName={NewName} destination={Destination} overwrite={Overwrite}", account?.Name, item.Path, newName, destination, overwrite);

            try
            {
                var result = await _fileSystem.RenameAsync(item.Path, destination, overwrite: overwrite, progress: null, cancellationToken).ConfigureAwait(false);
                Logger.Debug("FileSystemService.RenameAsync: IFileSystem.RenameAsync returned Success={Success} DestinationPath={Dest} Error={Error}", result.Success, result.DestinationPath, result.Error);
                if (!result.Success)
                {
                    // If conflict, try to compute unique destination and return null to let UI decide
                    return null;
                }

                if (string.IsNullOrWhiteSpace(result.DestinationPath))
                    return null;

                return CreateFileSystemObject(result.DestinationPath);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Rename failed from {Source} to {Dest}", item.Path, destination);
                return null;
            }
        }

        static TransferProgressReport? MapProgress(Io.TransferProgressReport report, string targetPath)
        {
            var mode = report.Mode == Io.TransferMode.Move ? TransferMode.Move : TransferMode.Copy;
            var stage = MapStage(report.Stage);
            return new TransferProgressReport(
                report.JobId,
                mode,
                stage,
                report.TotalItems,
                report.ProcessedItems,
                report.CurrentName,
                report.CurrentSource,
                targetPath,
                report.Message);
        }

        static TransferProgressStage MapStage(Io.TransferStage stage)
        {
            return stage switch
            {
                Io.TransferStage.Started => TransferProgressStage.Started,
                Io.TransferStage.ItemStarted => TransferProgressStage.ItemStarted,
                Io.TransferStage.ItemCompleted => TransferProgressStage.ItemCompleted,
                Io.TransferStage.Completed => TransferProgressStage.Completed,
                Io.TransferStage.Canceled => TransferProgressStage.Canceled,
                Io.TransferStage.Failed => TransferProgressStage.Failed,
                _ => TransferProgressStage.Failed
            };
        }

        static DirectoryModel MapDirectory(Io.DirectoryInfoModel info)
        {
            var model = CreateDirectoryEntry(info, false);
            model.Directories = info.Directories?.Select(d => CreateDirectoryEntry(d, false)).ToList() ?? new List<DirectoryModel>();
            model.Files = info.Files?.Select(CreateFileEntry).ToList() ?? new List<FileModel>();
            return model;
        }

        static DirectoryModel CreateDirectoryEntry(Io.DirectoryInfoModel info, bool isDrive)
        {
            var model = new DirectoryModel(isDrive)
            {
                Name = string.IsNullOrEmpty(info.Name) ? info.Path : info.Name,
                Path = info.Path,
                Id = info.Id?.Value ?? string.Empty,
                Created = info.Created.LocalDateTime,
                Modified = info.Modified.LocalDateTime,
                Accessed = info.Accessed.LocalDateTime,
                IsHidden = info.Attributes.HasFlag(Io.FileSystemAttributes.Hidden),
                IsSystem = info.Attributes.HasFlag(Io.FileSystemAttributes.System)
            };

            return model;
        }

        static FileModel CreateFileEntry(Io.FileInfoModel info)
        {
            var file = new FileModel();
            var extension = Path.GetExtension(info.Name);
            if (string.IsNullOrEmpty(extension))
            {
                file.Name = info.Name;
                file.Extension = string.Empty;
            }
            else
            {
                var baseName = info.Name.Substring(0, info.Name.Length - extension.Length);
                if (string.IsNullOrEmpty(baseName))
                {
                    file.Name = info.Name;
                    file.Extension = string.Empty;
                }
                else
                {
                    file.Name = baseName;
                    file.Extension = extension.Substring(1);
                }
            }

            file.Path = info.Path;
            file.Id = info.Id?.Value ?? string.Empty;
            file.Size = info.Size;
            file.Created = info.Created.LocalDateTime;
            file.Modified = info.Modified.LocalDateTime;
            file.Accessed = info.Accessed.LocalDateTime;
            file.IsHidden = info.Attributes.HasFlag(Io.FileSystemAttributes.Hidden);
            file.IsSystem = info.Attributes.HasFlag(Io.FileSystemAttributes.System);
            return file;
        }

        static string GetVolumeDisplayName(Io.VolumeModel volume)
        {
            if (!string.IsNullOrWhiteSpace(volume.Name))
                return volume.Name ?? string.Empty;

            var trimmed = volume.MountPoint?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(trimmed))
                return Path.GetFileName(trimmed);

            return volume.MountPoint ?? string.Empty;
        }

        static FileSystemObjectModel? CreateFileSystemObject(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var dir = new DirectoryModel();
                dir.Name = string.IsNullOrEmpty(info.Name) ? info.FullName : info.Name;
                dir.Path = info.FullName;
                dir.Created = info.CreationTime;
                dir.Modified = info.LastWriteTime;
                dir.Accessed = info.LastAccessTime;
                dir.IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden);
                dir.IsSystem = info.Attributes.HasFlag(FileAttributes.System);
                return dir;
            }

            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var file = new FileModel();
                if (string.IsNullOrEmpty(info.Extension))
                {
                    file.Name = info.Name;
                    file.Extension = string.Empty;
                }
                else
                {
                    var baseName = info.Name.Substring(0, info.Name.Length - info.Extension.Length);
                    if (string.IsNullOrEmpty(baseName))
                    {
                        file.Name = info.Name;
                        file.Extension = string.Empty;
                    }
                    else
                    {
                        file.Name = baseName;
                        file.Extension = info.Extension.Substring(1);
                    }
                }
                file.Path = info.FullName;
                file.Size = info.Length;
                file.Created = info.CreationTime;
                file.Modified = info.LastWriteTime;
                file.Accessed = info.LastAccessTime;
                file.IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden);
                file.IsSystem = info.Attributes.HasFlag(FileAttributes.System);
                return file;
            }

            return null;
        }
    }
}
