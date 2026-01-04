using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Mock;
using Jaya.IO.Models;

namespace Jaya.IO.Tests
{
    public class ErrorAndStreamTests
    {
        [Fact]
        public async Task Transfer_MissingSource_Returns_Failure()
        {
            var mock = new MockFileSystem();
            Jaya.IO.FileSystem.Default = mock;

            var results = await mock.TransferAsync(new[] { "/does/not/exist.bin" }, "/tmp/out", TransferMode.Copy);
            Assert.Single(results);
            Assert.False(results[0].Success);
        }

        [Fact]
        public async Task OpenWrite_Overwrite_Replaces_Content()
        {
            var mock = new MockFileSystem();
            Jaya.IO.FileSystem.Default = mock;
            var path = "/tmp/overwrite.bin";

            using (var ws = await mock.OpenWriteAsync(path, FileMode.Create))
            {
                await ws.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
            }

            using (var ws = await mock.OpenWriteAsync(path, FileMode.Create))
            {
                await ws.WriteAsync(new byte[] { 9 }, 0, 1);
            }

            using var rs = await mock.OpenReadAsync(path);
            var b = new byte[1];
            var r = await rs.ReadAsync(b, 0, 1);
            Assert.Equal(1, r);
            Assert.Equal((byte)9, b[0]);
        }

        [Fact]
        public async Task DeleteMode_Permanent_Removes_Item()
        {
            var mock = new MockFileSystem();
            var p = "/tmp/toremove.bin";
            mock.CreateFile(p, new byte[] { 1 });
            Jaya.IO.FileSystem.Default = mock;

            var ok = await mock.DeleteAsync(p, Models.DeleteMode.Permanent);
            Assert.True(ok);
            Assert.False(await mock.ExistsAsync(p));
        }
    }
}
