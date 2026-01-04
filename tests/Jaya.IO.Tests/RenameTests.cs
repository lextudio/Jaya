#nullable enable
using Jaya.IO;
using Jaya.IO.Platform;
using Jaya.IO.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jaya.IO.Tests
{
    public class RenameTests : IDisposable
    {
        string _tempDir;
        public RenameTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "jaya_rename_tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            // Ensure default platform is reset
            Platform.PlatformFactory.Set(new ManagedTestTrashPlatform());
            FileSystem.Default = new PlatformFileSystem();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public async Task SameVolume_File_Rename_Succeeds()
        {
            var src = Path.Combine(_tempDir, "file1.txt");
            var dst = Path.Combine(_tempDir, "file2.txt");
            await File.WriteAllTextAsync(src, "hello");

            var result = await FileSystem.Default.RenameAsync(src, dst, overwrite: false, progress: null, cancellationToken: CancellationToken.None);
            
            Assert.True(result.Success);
            Assert.True(File.Exists(dst));
            Assert.False(File.Exists(src));
        }

        [Fact]
        public async Task Overwrite_File_Rename_When_Exists()
        {
            var src = Path.Combine(_tempDir, "file1.txt");
            var dst = Path.Combine(_tempDir, "file2.txt");
            await File.WriteAllTextAsync(src, "hello");
            await File.WriteAllTextAsync(dst, "existing");

            var resultFail = await FileSystem.Default.RenameAsync(src, dst, overwrite: false, progress: null, cancellationToken: CancellationToken.None);
            Assert.False(resultFail.Success);

            var resultOk = await FileSystem.Default.RenameAsync(src, dst, overwrite: true, progress: null, cancellationToken: CancellationToken.None);
            Assert.True(resultOk.Success);
            Assert.True(File.Exists(dst));
            Assert.False(File.Exists(src));
            var content = await File.ReadAllTextAsync(dst);
            Assert.Equal("hello", content);
        }

        [Fact]
        public async Task CrossVolume_Fallback_Uses_TransferEngine()
        {
            // Inject a platform that refuses native rename so TransferEngine fallback is used
            Platform.PlatformFactory.Set(new RefusingRenamePlatform());
            FileSystem.Default = new PlatformFileSystem();

            var src = Path.Combine(_tempDir, "file1.txt");
            var dstDir = Path.Combine(_tempDir, "sub");
            Directory.CreateDirectory(dstDir);
            var dst = Path.Combine(dstDir, "file2.txt");
            await File.WriteAllTextAsync(src, new string('x', 1024));

            var result = await FileSystem.Default.RenameAsync(src, dst, overwrite: false, progress: null, cancellationToken: CancellationToken.None);
            Assert.True(result.Success);
            Assert.True(File.Exists(dst));
            Assert.False(File.Exists(src));
        }

        class RefusingRenamePlatform : IPlatformFileSystem
        {
            public Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<VolumeModel>());
            public Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult((false, (string?)null));
            public Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default) => Task.FromResult(false);
            public Task<bool> TryNativeRenameAsync(string source, string dest, bool overwrite, CancellationToken cancellationToken = default) => Task.FromResult(false);
        }
    }
}
