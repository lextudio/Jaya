using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Models;

namespace Jaya.IO.Platform.Tests
{
    public class MacPlatformTests
    {
        bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        [Fact]
        public async Task NativeCopy_Works_OnMac()
        {
            if (!IsMac)
                return;

            var fs = Jaya.IO.FileSystem.Default;
            var src = "/tmp/jaya_mac_src.bin";
            var destDir = "/tmp/jaya_mac_dest";
            var dest = Path.Combine(destDir, Path.GetFileName(src));

            // prepare
            await fs.DeleteAsync(src);
            await fs.DeleteAsync(dest);
            await fs.DeleteAsync(destDir);

            // create source file via OpenWrite
            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
            {
                await s.WriteAsync(new byte[] { 7, 8, 9 }, 0, 3);
            }

            // perform copy
            var results = await fs.TransferAsync(new[] { src }, destDir, TransferMode.Copy, null, default);
            Assert.Single(results);
            Assert.True(results[0].Success);

            // verify content
            using var rs = await fs.OpenReadAsync(dest);
            var buf = new byte[3];
            var read = await rs.ReadAsync(buf, 0, buf.Length);
            Assert.Equal(3, read);
            Assert.Equal(new byte[] { 7, 8, 9 }, buf);
        }

        [Fact]
        public async Task MoveToTrash_Removes_File_OnMac()
        {
            if (!IsMac)
                return;

            var fs = Jaya.IO.FileSystem.Default;
            var src = "/tmp/jaya_mac_trash.bin";

            await fs.DeleteAsync(src);
            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
                await s.WriteAsync(new byte[] { 1 }, 0, 1);

            var del = await fs.DeleteAsync(src, DeleteMode.Trash);
            Assert.True(del || !await fs.ExistsAsync(src));
        }
    }
}
