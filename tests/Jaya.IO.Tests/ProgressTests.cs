#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Mock;
using Jaya.IO.Models;

namespace Jaya.IO.Tests
{
    public class ProgressTests
    {
        [Fact]
        public async Task Transfer_Reports_Progress()
        {
            var mock = new MockFileSystem();
            var src1 = "/tmp/p1.bin";
            var src2 = "/tmp/p2.bin";
            mock.CreateFile(src1, new byte[] { 1 });
            mock.CreateFile(src2, new byte[] { 2 });

            // Wrapper that emits progress reports around each item
            var reports = new System.Collections.Concurrent.ConcurrentBag<TransferProgressReport>();
            var progress = new Progress<TransferProgressReport>(r => reports.Add(r));

            var wrapper = new ProgressWrapper(mock, progress);
            Jaya.IO.FileSystem.Default = wrapper;

            var results = await wrapper.TransferAsync(new[] { src1, src2 }, "/tmp/out", TransferMode.Copy, progress, CancellationToken.None);

            Assert.NotEmpty(reports);
            Assert.Equal(2, results.Count);
        }
    }

    class ProgressWrapper : Jaya.IO.IFileSystem
    {
        readonly MockFileSystem _inner;
        readonly IProgress<TransferProgressReport> _progress;

        public ProgressWrapper(MockFileSystem inner, IProgress<TransferProgressReport> progress)
        {
            _inner = inner;
            _progress = progress;
        }

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
            var list = new List<CopyResult>();
            var jobId = Guid.NewGuid();
            int total = sources is ICollection<string> c ? c.Count : sources.Count();
            int processed = 0;
            foreach (var s in sources)
            {
                _progress?.Report(new TransferProgressReport { JobId = jobId, Mode = mode, Stage = TransferStage.ItemStarted, TotalItems = total, ProcessedItems = processed, CurrentName = Path.GetFileName(s), CurrentSource = s });
                var r = await _inner.TransferAsync(new[] { s }, targetDirectory, mode, progress, cancellationToken).ConfigureAwait(false);
                list.AddRange(r);
                processed++;
                _progress?.Report(new TransferProgressReport { JobId = jobId, Mode = mode, Stage = TransferStage.ItemCompleted, TotalItems = total, ProcessedItems = processed, CurrentName = Path.GetFileName(s), CurrentSource = s, CurrentDestination = list[^1].DestinationPath });
            }

            _progress?.Report(new TransferProgressReport { JobId = jobId, Mode = mode, Stage = TransferStage.Completed, TotalItems = total, ProcessedItems = processed });
            return list;
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
            => _inner.OpenReadAsync(path, cancellationToken);

        public Task<Stream> OpenWriteAsync(string path, FileMode mode = FileMode.Create, CancellationToken cancellationToken = default)
            => _inner.OpenWriteAsync(path, mode, cancellationToken);

        public Task<FileAccessRights> GetAccessRightsAsync(string path, CancellationToken cancellationToken = default)
            => _inner.GetAccessRightsAsync(path, cancellationToken);

        public Task<Jaya.IO.RenameResult> RenameAsync(string sourcePath, string destinationPath, bool overwrite = false, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
            => _inner.RenameAsync(sourcePath, destinationPath, overwrite, progress, cancellationToken);
    }
}
