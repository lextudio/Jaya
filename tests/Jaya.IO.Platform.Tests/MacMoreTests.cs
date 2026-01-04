using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Xunit;
using Jaya.IO.Models;

namespace Jaya.IO.Platform.Tests
{
    public class MacMoreTests
    {
        bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        [Fact]
        public async Task LargeFile_NativeCopy_OnMac()
        {
            if (!IsMac) return;

            var fs = Jaya.IO.FileSystem.Default;
            var src = "/tmp/jaya_mac_big_src.bin";
            var dstDir = "/tmp/jaya_mac_big_dest";
            var dst = Path.Combine(dstDir, Path.GetFileName(src));

            await fs.DeleteAsync(src);
            await fs.DeleteAsync(dst);
            await fs.DeleteAsync(dstDir);

            // create ~1MB content
            var data = new byte[1024 * 1024];
            new Random(42).NextBytes(data);

            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
                await s.WriteAsync(data, 0, data.Length);

            var res = await fs.TransferAsync(new[] { src }, dstDir, TransferMode.Copy, null);
            Assert.Single(res);
            Assert.True(res[0].Success);

            using var rs = await fs.OpenReadAsync(dst);
            var read = new byte[data.Length];
            var got = await rs.ReadAsync(read, 0, read.Length);
            Assert.Equal(data.Length, got);
            Assert.Equal(data[0], read[0]);
        }

        [Fact]
        public async Task Transfer_Reports_Progress_OnMac()
        {
            if (!IsMac) return;

            var fs = Jaya.IO.FileSystem.Default;
            var src = "/tmp/jaya_mac_prog_src.bin";
            var dstDir = "/tmp/jaya_mac_prog_dest";

            await fs.DeleteAsync(src);
            await fs.DeleteAsync(dstDir);

            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
                await s.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);

            var reports = new ConcurrentBag<TransferProgressReport>();
            var progress = new Progress<TransferProgressReport>(r => reports.Add(r));

            var res = await fs.TransferAsync(new[] { src }, dstDir, TransferMode.Copy, progress);
            Assert.Single(res);
            Assert.True(res[0].Success);
            Assert.NotEmpty(reports);
            Assert.Contains(reports, r => r.Stage == TransferStage.Completed || r.Stage == TransferStage.ItemCompleted);
        }

        [Fact]
        public async Task Overwrite_Target_File_OnMac()
        {
            if (!IsMac) return;

            var fs = Jaya.IO.FileSystem.Default;
            var src = "/tmp/jaya_mac_ov_src.bin";
            var dstDir = "/tmp/jaya_mac_ov_dest";
            var dst = Path.Combine(dstDir, Path.GetFileName(src));

            await fs.DeleteAsync(src);
            await fs.DeleteAsync(dst);
            await fs.DeleteAsync(dstDir);

            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
                await s.WriteAsync(new byte[] { 9 }, 0, 1);

            // ensure destination directory exists
            Directory.CreateDirectory(dstDir);
            using (var s = await fs.OpenWriteAsync(dst, FileMode.Create))
                await s.WriteAsync(new byte[] { 1, 2 }, 0, 2);

            var res = await fs.TransferAsync(new[] { src }, dstDir, TransferMode.Copy, null);
            Assert.Single(res);
            Assert.True(res[0].Success);

            var actualPath = res[0].DestinationPath;
            using var rs = await fs.OpenReadAsync(actualPath);
            var buf = new byte[1];
            var r = await rs.ReadAsync(buf, 0, 1);
            Assert.Equal(1, r);
            Assert.Equal((byte)9, buf[0]);
        }

        [Fact]
        public async Task MoveDirectory_ToTrash_OnMac()
        {
            if (!IsMac) return;

            var fs = Jaya.IO.FileSystem.Default;
            var dir = "/tmp/jaya_mac_trashdir";
            var file = Path.Combine(dir, "f.txt");

            await fs.DeleteAsync(file);
            await fs.DeleteAsync(dir);

            // create directory and file
            await fs.DeleteAsync(dir);
            Directory.CreateDirectory(dir);
            using (var s = await fs.OpenWriteAsync(file, FileMode.Create))
                await s.WriteAsync(new byte[] { 7 }, 0, 1);

            var ok = await fs.DeleteAsync(dir, DeleteMode.Trash);
            Assert.True(ok || !await fs.ExistsAsync(dir));
        }
    }
}
