using Jaya.IO.Models;
using Jaya.IO.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Transfer;

internal class TransferEngine
{
    readonly IPlatformFileSystem _platform;

    public TransferEngine(IPlatformFileSystem platform)
    {
        _platform = platform;
    }

    public async Task<bool> DeleteAsync(string path, DeleteMode mode, CancellationToken cancellationToken = default)
    {
        var result = await TryDeleteAsync(path, mode, cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<DeleteResult> DeleteBatchAsync(IEnumerable<string> paths, DeleteMode mode, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var results = new List<CopyResult>();
        var jobId = Guid.NewGuid();
        var processed = 0;
        var anyFailed = false;

        progress?.Report(new TransferProgressReport
        {
            JobId = jobId,
            Mode = TransferMode.Delete,
            Stage = TransferStage.Started,
            TotalItems = list.Count,
            ProcessedItems = processed
        });

        foreach (var p in list)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new TransferProgressReport
                {
                    JobId = jobId,
                    Mode = TransferMode.Delete,
                    Stage = TransferStage.Canceled,
                    TotalItems = list.Count,
                    ProcessedItems = processed,
                    Percent = GetPercent(processed, list.Count)
                });
                break;
            }

            progress?.Report(new TransferProgressReport
            {
                JobId = jobId,
                Mode = TransferMode.Delete,
                Stage = TransferStage.ItemStarted,
                TotalItems = list.Count,
                ProcessedItems = processed,
                CurrentName = Path.GetFileName(p),
                CurrentSource = p
            });

            var deleteResult = await TryDeleteAsync(p, mode, cancellationToken).ConfigureAwait(false);
            results.Add(new CopyResult
            {
                SourcePath = p,
                DestinationPath = p,
                Success = deleteResult.Success,
                Skipped = !deleteResult.Success && deleteResult.Error is FileNotFoundException,
                Error = deleteResult.Error
            });

            processed++;
            if (!deleteResult.Success)
                anyFailed = true;

            progress?.Report(new TransferProgressReport
            {
                JobId = jobId,
                Mode = TransferMode.Delete,
                Stage = TransferStage.ItemCompleted,
                TotalItems = list.Count,
                ProcessedItems = processed,
                CurrentName = Path.GetFileName(p),
                CurrentSource = p,
                Message = deleteResult.Error?.Message,
                Percent = GetPercent(processed, list.Count),
                CurrentItemSucceeded = deleteResult.Success
            });
        }

        var finalStage = cancellationToken.IsCancellationRequested
            ? TransferStage.Canceled
            : anyFailed ? TransferStage.Failed : TransferStage.Completed;

        progress?.Report(new TransferProgressReport
        {
            JobId = jobId,
            Mode = TransferMode.Delete,
            Stage = finalStage,
            TotalItems = list.Count,
            ProcessedItems = processed,
            Percent = GetPercent(processed, list.Count)
        });
        return new DeleteResult { Results = results };
    }

    public async Task<IReadOnlyList<CopyResult>> TransferAsync(IEnumerable<string> sources, string targetDirectory, TransferMode mode, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        if (mode == TransferMode.Delete)
            throw new ArgumentOutOfRangeException(nameof(mode), "Use DeleteAsync/DeleteBatchAsync for delete operations.");

        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new ArgumentException("Target directory is required.", nameof(targetDirectory));

        var list = sources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var results = new List<CopyResult>();
        var jobId = Guid.NewGuid();
        var processed = 0;
        var anyFailed = false;

        progress?.Report(new TransferProgressReport
        {
            JobId = jobId,
            Mode = mode,
            Stage = TransferStage.Started,
            TotalItems = list.Count,
            ProcessedItems = processed
        });

        try
        {
            Directory.CreateDirectory(targetDirectory);
        }
        catch (Exception ex)
        {
            foreach (var rawSource in list)
            {
                results.Add(new CopyResult
                {
                    SourcePath = rawSource,
                    DestinationPath = string.Empty,
                    Success = false,
                    Error = ex
                });
            }

            progress?.Report(new TransferProgressReport
            {
                JobId = jobId,
                Mode = mode,
                Stage = TransferStage.Failed,
                TotalItems = list.Count,
                ProcessedItems = processed,
                Message = ex.Message
            });

            return results;
        }

        foreach (var rawSource in list)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new TransferProgressReport
                {
                    JobId = jobId,
                    Mode = mode,
                    Stage = TransferStage.Canceled,
                    TotalItems = list.Count,
                    ProcessedItems = processed,
                    Percent = GetPercent(processed, list.Count)
                });
                break;
            }

            var source = Path.TrimEndingDirectorySeparator(rawSource);
            var isDir = Directory.Exists(source);
            var isFile = File.Exists(source);

            if (!isDir && !isFile)
            {
                var missingError = new FileNotFoundException("Source not found.", source);
                results.Add(new CopyResult
                {
                    SourcePath = source,
                    DestinationPath = string.Empty,
                    Success = false,
                    Skipped = true,
                    Error = missingError
                });
                processed++;
                anyFailed = true;
                progress?.Report(new TransferProgressReport
                {
                    JobId = jobId,
                    Mode = mode,
                    Stage = TransferStage.ItemCompleted,
                    TotalItems = list.Count,
                    ProcessedItems = processed,
                    CurrentName = Path.GetFileName(source),
                    CurrentSource = source,
                    Message = missingError.Message,
                    Percent = GetPercent(processed, list.Count),
                    CurrentItemSucceeded = false
                });
                continue;
            }

            var name = isDir ? new DirectoryInfo(source).Name : Path.GetFileName(source);
            if (string.IsNullOrWhiteSpace(name))
                name = source;

            var dest = GenerateUniqueDestination(Path.Combine(targetDirectory, name), isDir);

            progress?.Report(new TransferProgressReport
            {
                JobId = jobId,
                Mode = mode,
                Stage = TransferStage.ItemStarted,
                TotalItems = list.Count,
                ProcessedItems = processed,
                CurrentName = name,
                CurrentSource = source,
                CurrentDestination = dest
            });

            CopyResult result = mode == TransferMode.Move
                ? await MoveItemAsync(source, dest, isDir, progress, cancellationToken).ConfigureAwait(false)
                : await CopyItemAsync(source, dest, isDir, progress, cancellationToken).ConfigureAwait(false);

            results.Add(result);
            processed++;
            if (!result.Success)
                anyFailed = true;

            progress?.Report(new TransferProgressReport
            {
                JobId = jobId,
                Mode = mode,
                Stage = TransferStage.ItemCompleted,
                TotalItems = list.Count,
                ProcessedItems = processed,
                CurrentName = name,
                CurrentSource = source,
                CurrentDestination = dest,
                Message = result.Error?.Message,
                Percent = GetPercent(processed, list.Count),
                CurrentItemSucceeded = result.Success
            });
        }

        var completionStage = cancellationToken.IsCancellationRequested
            ? TransferStage.Canceled
            : anyFailed ? TransferStage.Failed : TransferStage.Completed;

        progress?.Report(new TransferProgressReport
        {
            JobId = jobId,
            Mode = mode,
            Stage = completionStage,
            TotalItems = list.Count,
            ProcessedItems = processed,
            Percent = GetPercent(processed, list.Count)
        });
        return results;
    }

    async Task<CopyResult> CopyItemAsync(string source, string dest, bool isDir, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken)
    {
        try
        {
            var nativeOk = await _platform.TryNativeCopyAsync(source, dest, progress, cancellationToken).ConfigureAwait(false);
            if (!nativeOk)
            {
                if (isDir)
                    CopyDirectory(source, dest, cancellationToken);
                else
                    File.Copy(source, dest);
            }

            return new CopyResult { SourcePath = source, DestinationPath = dest, Success = true };
        }
        catch (Exception ex)
        {
            return new CopyResult { SourcePath = source, DestinationPath = dest, Success = false, Error = ex };
        }
    }

    async Task<CopyResult> MoveItemAsync(string source, string dest, bool isDir, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken)
    {
        try
        {
            if (isDir)
                Directory.Move(source, dest);
            else
                File.Move(source, dest);

            return new CopyResult { SourcePath = source, DestinationPath = dest, Success = true };
        }
        catch (IOException)
        {
            var copyResult = await CopyItemAsync(source, dest, isDir, progress, cancellationToken).ConfigureAwait(false);
            if (!copyResult.Success)
                return copyResult;

            var deleteResult = TryDeletePermanent(source);
            if (!deleteResult.Success)
                return new CopyResult { SourcePath = source, DestinationPath = dest, Success = false, Error = deleteResult.Error };

            return new CopyResult { SourcePath = source, DestinationPath = dest, Success = true };
        }
        catch (Exception ex)
        {
            return new CopyResult { SourcePath = source, DestinationPath = dest, Success = false, Error = ex };
        }
    }

    async Task<(bool Success, Exception? Error)> TryDeleteAsync(string path, DeleteMode mode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, new ArgumentException("Path is required.", nameof(path)));

        cancellationToken.ThrowIfCancellationRequested();

        var exists = File.Exists(path) || Directory.Exists(path);
        if (!exists)
            return (false, new FileNotFoundException("Path not found.", path));

        if (mode == DeleteMode.Trash)
        {
            try
            {
                var (moved, trashPath) = await _platform.MoveToTrashAsync(path, cancellationToken).ConfigureAwait(false);
                if (moved)
                    return (true, null);
                return (false, new IOException("Move to trash failed."));
            }
            catch (Exception ex)
            {
                return (false, ex);
            }
        }

        return TryDeletePermanent(path);
    }

    static (bool Success, Exception? Error) TryDeletePermanent(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
            else
                return (false, new FileNotFoundException("Path not found.", path));

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    static string GenerateUniqueDestination(string candidate, bool isDir)
    {
        var dir = Path.GetDirectoryName(candidate) ?? string.Empty;
        var name = Path.GetFileName(candidate);
        var baseName = isDir ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDir ? string.Empty : Path.GetExtension(name);
        var result = Path.Combine(dir, baseName + extension);
        var counter = 1;
        while (File.Exists(result) || Directory.Exists(result))
        {
            var suffix = counter == 1 ? " copy" : $" copy {counter}";
            result = Path.Combine(dir, baseName + suffix + extension);
            counter++;
        }

        return result;
    }

    static void CopyDirectory(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var dir = new DirectoryInfo(sourcePath);
        if (!dir.Exists) return;
        Directory.CreateDirectory(destinationPath);
        foreach (var file in dir.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFilePath = Path.Combine(destinationPath, file.Name);
            File.Copy(file.FullName, targetFilePath);
        }

        foreach (var subdir in dir.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetSubDir = Path.Combine(destinationPath, subdir.Name);
            CopyDirectory(subdir.FullName, targetSubDir, cancellationToken);
        }
    }

    static double? GetPercent(int processed, int total)
    {
        if (total <= 0)
            return null;
        return processed * 100d / total;
    }
}
