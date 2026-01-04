using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Transfer;
using Jaya.IO.Platform;
using Jaya.IO.Models;
using Jaya.IO.Mock;

namespace Jaya.IO.Tests
{
    public class TransferEngineTests
    {
        [Fact]
        public async Task TransferEngine_Copies_File_From_Mock_To_Target()
        {
            var mock = new MockFileSystem();
            var sourcePath = "/tmp/mockfile.bin";
            var targetDir = "/tmp/target";
            mock.CreateFile(sourcePath, new byte[] { 1, 2, 3 });

            // Use the mock directly for this unit test to avoid global state interaction
            Jaya.IO.FileSystem.Default = mock;
            var results = await mock.TransferAsync(new[] { sourcePath }, targetDir, TransferMode.Copy, null, CancellationToken.None);

            Assert.Single(results);
            var r = results[0];
            Assert.True(r.Success);
            Assert.Equal(sourcePath, r.SourcePath);
            Assert.Equal(Path.Combine(targetDir, Path.GetFileName(sourcePath)), r.DestinationPath);
        }

        [Fact]
        public async Task TransferEngine_Moves_File_When_ModeIsMove()
        {
            var mock = new MockFileSystem();
            var src = "/tmp/a.txt";
            var destDir = "/tmp/dst";
            mock.CreateFile(src, new byte[] { 9, 9 });

            Jaya.IO.FileSystem.Default = mock;
            var results = await Jaya.IO.FileSystem.Default.TransferAsync(new[] { src }, destDir, TransferMode.Move, null, CancellationToken.None);

            Assert.Single(results);
            Assert.True(results[0].Success);
            Assert.False(await Jaya.IO.FileSystem.Default.ExistsAsync(src));
            Assert.True(await Jaya.IO.FileSystem.Default.ExistsAsync(Path.Combine(destDir, Path.GetFileName(src))));
        }

        [Fact]
        public async Task DeleteBatch_Returns_PerItem_Results()
        {
            var mock = new MockFileSystem();
            var a = "/tmp/x.txt";
            var b = "/tmp/y.txt";
            mock.CreateFile(a, new byte[] { 1 });
            // b does not exist

            Jaya.IO.FileSystem.Default = mock;
            var result = await Jaya.IO.FileSystem.Default.DeleteBatchAsync(new[] { a, b }, Models.DeleteMode.Permanent, null, CancellationToken.None);

            Assert.Equal(2, result.Results.Count);
            Assert.True(result.Results.First(r => r.SourcePath == a).Success);
            Assert.False(result.Results.First(r => r.SourcePath == b).Success);
        }

        [Fact]
        public async Task GetUniqueDestination_Generates_Unique_Name()
        {
            var mock = new MockFileSystem();
            var dir = "/tmp/uniq";
            mock.CreateDirectory(dir);
            mock.CreateFile(Path.Combine(dir, "file.txt"), new byte[] { 1 });

            Jaya.IO.FileSystem.Default = mock;
            var candidate = Jaya.IO.FileSystem.Default.GetUniqueDestination(dir, "file.txt", false);
            Assert.NotEqual(Path.Combine(dir, "file.txt"), candidate);
        }

        [Fact]
        public async Task OpenWrite_And_OpenRead_Work_For_Mock()
        {
            var mock = new MockFileSystem();
            Jaya.IO.FileSystem.Default = mock;
            var path = "/tmp/write.bin";

            using (var ws = await Jaya.IO.FileSystem.Default.OpenWriteAsync(path))
            {
                await ws.WriteAsync(new byte[] { 42, 43 }, 0, 2);
            }

            using (var rs = await Jaya.IO.FileSystem.Default.OpenReadAsync(path))
            {
                var buf = new byte[2];
                var read = await rs.ReadAsync(buf, 0, buf.Length);
                Assert.Equal(2, read);
                Assert.Equal((byte)42, buf[0]);
            }
        }
    }
}
