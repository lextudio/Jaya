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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Serilog;
using System.IO.Pipelines;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemService : ProviderServiceBase, IProviderService, IFileDeleteService, IFileTransferService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemService>();
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
            if (result != null)
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

        public Task<bool> DeleteAsync(AccountModelBase account, IEnumerable<FileSystemObjectModel> items, DeleteMode mode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            return _impl.DeleteAsync(items, mode);
        }

        public Task<IReadOnlyList<FileSystemObjectModel>> TransferAsync(
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
                return Task.FromResult<IReadOnlyList<FileSystemObjectModel>>(Array.Empty<FileSystemObjectModel>());

            return Task.Run(() =>
            {
                Directory.CreateDirectory(targetPath);
                var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                var createdItems = new List<FileSystemObjectModel>();
                var backend = GetCopyBackend();
                var jobId = Guid.NewGuid();

                var candidates = new List<(string SourcePath, string Name, bool IsDirectory)>();
                foreach (var item in items)
                {
                    var sourcePath = item?.Path;
                    if (string.IsNullOrWhiteSpace(sourcePath))
                        continue;

                    var trimmedSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var name = Path.GetFileName(trimmedSource);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var isDirectory = Directory.Exists(trimmedSource);
                    if (!isDirectory && !File.Exists(trimmedSource))
                        continue;

                    candidates.Add((trimmedSource, name, isDirectory));
                }

                progress?.Report(new TransferProgressReport(
                    jobId,
                    mode,
                    TransferProgressStage.Started,
                    candidates.Count,
                    0,
                    null,
                    null,
                    targetPath));

                var processed = 0;
                foreach (var candidate in candidates)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        progress?.Report(new TransferProgressReport(
                            jobId,
                            mode,
                            TransferProgressStage.Canceled,
                            candidates.Count,
                            processed,
                            null,
                            null,
                            targetPath));
                        break;
                    }

                    var trimmedSource = candidate.SourcePath;
                    var name = candidate.Name;
                    var isDirectory = candidate.IsDirectory;
                    var sourceDirectory = Path.GetDirectoryName(trimmedSource);
                    if (mode == TransferMode.Move &&
                        !string.IsNullOrWhiteSpace(sourceDirectory) &&
                        comparer.Equals(sourceDirectory, targetPath))
                    {
                        processed++;
                        progress?.Report(new TransferProgressReport(
                            jobId,
                            mode,
                            TransferProgressStage.ItemCompleted,
                            candidates.Count,
                            processed,
                            name,
                            trimmedSource,
                            targetPath,
                            "Skipped: source and target are the same"));
                        continue;
                    }

                    var destinationPath = GetUniqueDestination(targetPath, name, isDirectory);
                    if (comparer.Equals(trimmedSource, destinationPath))
                    {
                        processed++;
                        progress?.Report(new TransferProgressReport(
                            jobId,
                            mode,
                            TransferProgressStage.ItemCompleted,
                            candidates.Count,
                            processed,
                            name,
                            trimmedSource,
                            targetPath,
                            "Skipped: source and destination are the same"));
                        continue;
                    }

                    progress?.Report(new TransferProgressReport(
                        jobId,
                        mode,
                        TransferProgressStage.ItemStarted,
                        candidates.Count,
                        processed,
                        name,
                        trimmedSource,
                        targetPath));

                    var transferred = mode == TransferMode.Copy
                        ? TryCopyItem(trimmedSource, destinationPath, isDirectory, backend, cancellationToken)
                        : TryMoveItem(trimmedSource, destinationPath, isDirectory, backend, cancellationToken);

                    if (transferred)
                    {
                        var created = CreateFileSystemObject(destinationPath);
                        if (created != null)
                            createdItems.Add(created);
                    }

                    processed++;
                    var message = transferred ? null : "Failed to transfer item";
                    progress?.Report(new TransferProgressReport(
                        jobId,
                        mode,
                        TransferProgressStage.ItemCompleted,
                        candidates.Count,
                        processed,
                        name,
                        trimmedSource,
                        targetPath,
                        message));
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    progress?.Report(new TransferProgressReport(
                        jobId,
                        mode,
                        TransferProgressStage.Completed,
                        candidates.Count,
                        processed,
                        null,
                        null,
                        targetPath));
                }

                return (IReadOnlyList<FileSystemObjectModel>)createdItems;
            }, cancellationToken);
        }

        static string GetUniqueDestination(string targetDirectory, string name, bool isDirectory)
        {
            var baseName = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
            var extension = isDirectory ? string.Empty : Path.GetExtension(name);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = name;

            var candidate = Path.Combine(targetDirectory, $"{baseName}{extension}");
            var counter = 1;

            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                var suffix = counter == 1 ? " copy" : $" copy {counter}";
                candidate = Path.Combine(targetDirectory, $"{baseName}{suffix}{extension}");
                counter++;
            }

            return candidate;
        }

        enum CopyBackend : byte
        {
            Managed,
            Stream,
            WindowsNative,
            MacNative
        }

        static CopyBackend GetCopyBackend()
        {
            if (OperatingSystem.IsWindows())
                return CopyBackend.WindowsNative;
            if (OperatingSystem.IsMacOS())
                return CopyBackend.MacNative;
            if (OperatingSystem.IsLinux())
                return CopyBackend.Stream;
            return CopyBackend.Managed;
        }

        static bool TryCopyItem(string sourcePath, string destinationPath, bool isDirectory, CopyBackend backend, CancellationToken cancellationToken)
        {
            try
            {
                if (isDirectory)
                {
                    CopyDirectory(sourcePath, destinationPath, backend, cancellationToken);
                }
                else
                {
                    switch (backend)
                    {
                        case CopyBackend.WindowsNative:
                            return CopyFileWindowsNative(sourcePath, destinationPath, cancellationToken);
                        case CopyBackend.MacNative:
                            return CopyFileMacNative(sourcePath, destinationPath, cancellationToken);
                        case CopyBackend.Stream:
                            CopyFileStream(sourcePath, destinationPath, cancellationToken);
                            break;
                        default:
                            File.Copy(sourcePath, destinationPath);
                            break;
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                TryDeletePartial(destinationPath);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Copy failed from {Source} to {Destination}", sourcePath, destinationPath);
                TryDeletePartial(destinationPath);
                return false;
            }
        }

        static bool TryMoveItem(string sourcePath, string destinationPath, bool isDirectory, CopyBackend backend, CancellationToken cancellationToken)
        {
            try
            {
                if (isDirectory)
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                else
                {
                    if (backend == CopyBackend.WindowsNative)
                    {
                        if (MoveFileWindowsNative(sourcePath, destinationPath, cancellationToken))
                            return true;
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(cancellationToken);
                        throw new IOException("MoveFileWithProgress failed.");
                    }

                    File.Move(sourcePath, destinationPath);
                    return true;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException ex)
            {
                Logger.Debug(ex, "Move failed, attempting copy+delete from {Source} to {Destination}", sourcePath, destinationPath);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Move failed from {Source} to {Destination}", sourcePath, destinationPath);
                return false;
            }

            if (!TryCopyItem(sourcePath, destinationPath, isDirectory, backend, cancellationToken))
                return false;

            return TryDeleteItem(sourcePath, isDirectory);
        }

        static bool TryDeleteItem(string sourcePath, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                    Directory.Delete(sourcePath, true);
                else
                    File.Delete(sourcePath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to delete source after move: {Source}", sourcePath);
                return false;
            }
        }

        static void CopyDirectory(string sourcePath, string destinationPath, CopyBackend backend, CancellationToken cancellationToken)
        {
            var dir = new DirectoryInfo(sourcePath);
            if (!dir.Exists)
                return;

            Directory.CreateDirectory(destinationPath);

            foreach (var file in dir.GetFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetFilePath = Path.Combine(destinationPath, file.Name);
                switch (backend)
                {
                    case CopyBackend.WindowsNative:
                        if (!CopyFileWindowsNative(file.FullName, targetFilePath, cancellationToken))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                throw new OperationCanceledException(cancellationToken);
                            throw new IOException("CopyFileEx failed.");
                        }
                        break;
                    case CopyBackend.MacNative:
                        if (!CopyFileMacNative(file.FullName, targetFilePath, cancellationToken))
                            throw new IOException("copyfile failed.");
                        break;
                    case CopyBackend.Stream:
                        CopyFileStream(file.FullName, targetFilePath, cancellationToken);
                        break;
                    default:
                        file.CopyTo(targetFilePath);
                        break;
                }
            }

            foreach (var subdir in dir.GetDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetSubDir = Path.Combine(destinationPath, subdir.Name);
                CopyDirectory(subdir.FullName, targetSubDir, backend, cancellationToken);
            }
        }

        static void CopyFileStream(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            const int bufferSize = 1024 * 1024;
            using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan);
            var buffer = new byte[bufferSize];
            int read;
            while ((read = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destinationStream.Write(buffer, 0, read);
            }
        }

        static bool CopyFileWindowsNative(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var cancel = false;
            var state = new TransferCancelState(cancellationToken);
            var handle = GCHandle.Alloc(state);
            try
            {
                var result = CopyFileEx(
                    sourcePath,
                    destinationPath,
                    s_copyProgressRoutine,
                    GCHandle.ToIntPtr(handle),
                    ref cancel,
                    CopyFileFlags.COPY_FILE_RESTARTABLE);

                if (!result && !cancellationToken.IsCancellationRequested)
                {
                    var error = Marshal.GetLastWin32Error();
                    Logger.Warning("CopyFileEx failed from {Source} to {Destination}. Error={Error}", sourcePath, destinationPath, error);
                }

                if (!result)
                    TryDeletePartial(destinationPath);

                return result;
            }
            finally
            {
                handle.Free();
            }
        }

        static bool MoveFileWindowsNative(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var state = new TransferCancelState(cancellationToken);
            var handle = GCHandle.Alloc(state);
            try
            {
                var result = MoveFileWithProgress(
                    sourcePath,
                    destinationPath,
                    s_copyProgressRoutine,
                    GCHandle.ToIntPtr(handle),
                    MoveFileFlags.MOVEFILE_COPY_ALLOWED | MoveFileFlags.MOVEFILE_WRITE_THROUGH);

                if (!result && !cancellationToken.IsCancellationRequested)
                {
                    var error = Marshal.GetLastWin32Error();
                    Logger.Warning("MoveFileWithProgress failed from {Source} to {Destination}. Error={Error}", sourcePath, destinationPath, error);
                }

                return result;
            }
            finally
            {
                handle.Free();
            }
        }

        static bool CopyFileMacNative(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var flags = CopyFileFlagsMac.COPYFILE_ALL;
            var result = copyfile(sourcePath, destinationPath, IntPtr.Zero, flags);
            if (result != 0 && !cancellationToken.IsCancellationRequested)
            {
                var error = Marshal.GetLastWin32Error();
                Logger.Warning("copyfile failed from {Source} to {Destination}. Error={Error}", sourcePath, destinationPath, error);
            }

            if (result != 0)
                TryDeletePartial(destinationPath);

            return result == 0;
        }

        sealed class TransferCancelState
        {
            public TransferCancelState(CancellationToken token)
            {
                Token = token;
            }

            public CancellationToken Token { get; }
        }

        delegate CopyProgressResult CopyProgressRoutine(
            long totalFileSize,
            long totalBytesTransferred,
            long streamSize,
            long streamBytesTransferred,
            uint dwStreamNumber,
            CopyProgressCallbackReason dwCallbackReason,
            IntPtr hSourceFile,
            IntPtr hDestinationFile,
            IntPtr lpData);

        static readonly CopyProgressRoutine s_copyProgressRoutine = CopyProgressCallback;

        static CopyProgressResult CopyProgressCallback(
            long totalFileSize,
            long totalBytesTransferred,
            long streamSize,
            long streamBytesTransferred,
            uint dwStreamNumber,
            CopyProgressCallbackReason dwCallbackReason,
            IntPtr hSourceFile,
            IntPtr hDestinationFile,
            IntPtr lpData)
        {
            if (lpData != IntPtr.Zero)
            {
                var handle = GCHandle.FromIntPtr(lpData);
                if (handle.Target is TransferCancelState state && state.Token.IsCancellationRequested)
                    return CopyProgressResult.PROGRESS_CANCEL;
            }

            return CopyProgressResult.PROGRESS_CONTINUE;
        }

        enum CopyProgressResult : uint
        {
            PROGRESS_CONTINUE = 0,
            PROGRESS_CANCEL = 1,
            PROGRESS_STOP = 2,
            PROGRESS_QUIET = 3
        }

        enum CopyProgressCallbackReason : uint
        {
            CALLBACK_CHUNK_FINISHED = 0,
            CALLBACK_STREAM_SWITCH = 1
        }

        [Flags]
        enum CopyFileFlags : uint
        {
            COPY_FILE_RESTARTABLE = 0x00000002
        }

        [Flags]
        enum MoveFileFlags : uint
        {
            MOVEFILE_COPY_ALLOWED = 0x00000002,
            MOVEFILE_WRITE_THROUGH = 0x00000008
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CopyFileEx(
            string lpExistingFileName,
            string lpNewFileName,
            CopyProgressRoutine lpProgressRoutine,
            IntPtr lpData,
            ref bool pbCancel,
            CopyFileFlags dwCopyFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileWithProgress(
            string lpExistingFileName,
            string lpNewFileName,
            CopyProgressRoutine lpProgressRoutine,
            IntPtr lpData,
            MoveFileFlags dwFlags);

        [Flags]
        enum CopyFileFlagsMac : uint
        {
            COPYFILE_DATA = 0x00000001,
            COPYFILE_SECURITY = 0x00000002,
            COPYFILE_XATTR = 0x00000004,
            COPYFILE_STAT = 0x00000008,
            COPYFILE_ACL = 0x00000010,
            COPYFILE_RECURSIVE = 0x00000080,
            COPYFILE_ALL = COPYFILE_DATA | COPYFILE_SECURITY | COPYFILE_XATTR | COPYFILE_STAT | COPYFILE_ACL
        }

        [DllImport("libSystem.dylib", EntryPoint = "copyfile", SetLastError = true)]
        static extern int copyfile(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string to,
            IntPtr state,
            CopyFileFlagsMac flags);

        static void TryDeletePartial(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
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
