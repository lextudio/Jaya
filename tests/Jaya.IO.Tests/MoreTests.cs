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
    public class MoreTests
    {
        [Fact]
        public async Task GetDirectory_Returns_Files_And_Directories()
        {
            var mock = new MockFileSystem();
            mock.CreateDirectory("/tmp/parent");
            mock.CreateDirectory("/tmp/parent/subdir");
            mock.CreateFile("/tmp/parent/file1.txt", new byte[] { 1 });

            Jaya.IO.FileSystem.Default = mock;
            var dir = await mock.GetDirectoryAsync("/tmp/parent");

            Assert.NotNull(dir);
            Assert.Contains(dir.Files, f => f.Name == "file1.txt");
            Assert.Contains(dir.Directories, d => d.Name == "subdir");
        }

        [Fact]
        public async Task GetAccessRights_Returns_Flags()
        {
            var mock = new MockFileSystem();
            mock.CreateFile("/tmp/rw.bin", new byte[] { 0 });

            Jaya.IO.FileSystem.Default = mock;
            var rights = await mock.GetAccessRightsAsync("/tmp/rw.bin");

            Assert.True((rights & FileAccessRights.Read) == FileAccessRights.Read);
            Assert.True((rights & FileAccessRights.Write) == FileAccessRights.Write);
        }

        [Fact]
        public async Task DeleteBatch_Reports_Progress_PerItem()
        {
            var mock = new MockFileSystem();
            mock.CreateFile("/tmp/a1.txt", new byte[] { 1 });
            mock.CreateFile("/tmp/a2.txt", new byte[] { 2 });

            var reports = new List<TransferProgressReport>();
            var progress = new Progress<TransferProgressReport>(r => reports.Add(r));

            Jaya.IO.FileSystem.Default = mock;
            var res = await mock.DeleteBatchAsync(new[] { "/tmp/a1.txt", "/tmp/a2.txt" }, Models.DeleteMode.Permanent, progress);

            Assert.Equal(2, res.Results.Count);
            Assert.True(reports.Count >= 1);
            Assert.True(res.Results.All(r => r.Success));
        }

        [Fact]
        public async Task OpenWrite_Append_Appends_Content()
        {
            var mock = new MockFileSystem();
            var path = "/tmp/append.bin";
            mock.CreateFile(path, new byte[] { 10 });

            Jaya.IO.FileSystem.Default = mock;

            using (var s = await mock.OpenWriteAsync(path, FileMode.Append))
            {
                await s.WriteAsync(new byte[] { 20 }, 0, 1);
            }

            using var rs = await mock.OpenReadAsync(path);
            var buf = new byte[2];
            var read = await rs.ReadAsync(buf, 0, 2);
            Assert.Equal(2, read);
            Assert.Equal((byte)10, buf[0]);
            Assert.Equal((byte)20, buf[1]);
        }

        [Fact]
        public async Task GetUniqueDestination_Works_Under_Many_Collisions()
        {
            var mock = new MockFileSystem();
            var dir = "/tmp/collide";
            mock.CreateDirectory(dir);

            // create many files with the same base name pattern
            for (int i = 0; i < 20; i++)
            {
                var name = i == 0 ? "file.txt" : $"file ({i}).txt";
                mock.CreateFile(Path.Combine(dir, name), new byte[] { (byte)i });
            }

            Jaya.IO.FileSystem.Default = mock;
            var candidate = mock.GetUniqueDestination(dir, "file.txt", false);
            Assert.DoesNotContain(candidate, mock.GetDirectoryAsync(dir).GetAwaiter().GetResult().Files.Select(f => f.Path));
            Assert.StartsWith(Path.Combine(dir, "file"), candidate);
        }
    }
}
