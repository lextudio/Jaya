#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Mock;
using Jaya.IO.Models;

namespace Jaya.IO.Tests
{
    public class CancellationTests
    {
        class SlowWrapper : Jaya.IO.IFileSystem
        {
            readonly MockFileSystem _inner;
            public SlowWrapper(MockFileSystem inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

            public Task<DirectoryInfoModel?> GetDirectoryAsync(string path, bool includeFiles = true, bool includeDirectories = true, CancellationToken cancellationToken = default)
                => _inner.GetDirectoryAsync(path, includeFiles, includeDirectories, cancellationToken);

            public Task<FileInfoModel?> GetFileAsync(string path, CancellationToken cancellationToken = default)
                => _inner.GetFileAsync(path, cancellationToken);

            public Task<IReadOnlyList<VolumeModel>> GetVolumesAsync(CancellationToken cancellationToken = default)
                => _inner.GetVolumesAsync(cancellationToken);

            public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
                => _inner.ExistsAsync(path, cancellationToken);

            public string GetUniqueDestination(string targetDirectory, string name, bool isDirectory)
                => _inner.GetUniqueDestination(targetDirectory, name, isDirectory);

            public Task<bool> DeleteAsync(string path, Models.DeleteMode mode = Models.DeleteMode.Permanent, CancellationToken cancellationToken = default)
                => _inner.DeleteAsync(path, mode, cancellationToken);

            public Task<DeleteResult> DeleteBatchAsync(IEnumerable<string> paths, Models.DeleteMode mode = Models.DeleteMode.Permanent, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
                => _inner.DeleteBatchAsync(paths, mode, progress, cancellationToken);

            public async Task<IReadOnlyList<CopyResult>> TransferAsync(IEnumerable<string> sources, string targetDirectory, TransferMode mode, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
            {
                var results = new List<CopyResult>();
                foreach (var s in sources)
                {
                    try
                    {
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new CopyResult { SourcePath = s, DestinationPath = Path.Combine(targetDirectory, Path.GetFileName(s)), Success = false });
                        break;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        results.Add(new CopyResult { SourcePath = s, DestinationPath = Path.Combine(targetDirectory, Path.GetFileName(s)), Success = false });
                        break;
                    }

                    var r = await _inner.TransferAsync(new[] { s }, targetDirectory, mode, progress, cancellationToken).ConfigureAwait(false);
                    results.AddRange(r);
                }

                return results;
            }

            public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
                => _inner.OpenReadAsync(path, cancellationToken);

            public Task<Stream> OpenWriteAsync(string path, FileMode mode = FileMode.Create, CancellationToken cancellationToken = default)
                => _inner.OpenWriteAsync(path, mode, cancellationToken);

            public Task<FileAccessRights> GetAccessRightsAsync(string path, CancellationToken cancellationToken = default)
                => _inner.GetAccessRightsAsync(path, cancellationToken);
        }

        [Fact]
        public async Task Transfer_CanBeCancelled()
        {
            var mock = new MockFileSystem();
            mock.CreateFile("/tmp/one.bin", new byte[] { 1 });
            mock.CreateFile("/tmp/two.bin", new byte[] { 2 });

            var slow = new SlowWrapper(mock);
            Jaya.IO.FileSystem.Default = slow;

            using var cts = new CancellationTokenSource();
            var task = Jaya.IO.FileSystem.Default.TransferAsync(new[] { "/tmp/one.bin", "/tmp/two.bin" }, "/tmp/out", TransferMode.Copy, null, cts.Token);

            // Cancel shortly after starting to interrupt the slow transfer
            await Task.Delay(100);
            cts.Cancel();

            var results = await task;
            Assert.True(results.Count >= 1);
            Assert.Contains(results, r => !r.Success);
        }
    }
}
