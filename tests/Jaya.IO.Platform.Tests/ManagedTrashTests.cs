using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Jaya.IO.Platform.Tests
{
    public class ManagedTrashTests
    {
        bool IsMac => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);

        [Fact]
        public async Task Can_Move_To_Managed_Trash_And_Recover()
        {
            // Enable managed test trash for deterministic behavior
            var trashRoot = Jaya.IO.FileSystem.EnableManagedTestTrash();

            var fs = Jaya.IO.FileSystem.Default;
            var src = Path.Combine(Path.GetTempPath(), "jaya_managed_trash_test.bin");
            if (File.Exists(src)) File.Delete(src);

            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
                await s.WriteAsync(new byte[] { 4, 5, 6 }, 0, 3);

            // Delete to trash using library API
            var ok = await fs.DeleteAsync(src, Jaya.IO.Models.DeleteMode.Trash);
            Assert.True(ok);

            // Find file in trashRoot
            var files = Directory.GetFiles(trashRoot);
            Assert.NotEmpty(files);
            var trashPath = files.First();

            // Recover by moving it back
            var recovered = Path.Combine(Path.GetTempPath(), "jaya_managed_trash_test_recovered.bin");
            if (File.Exists(recovered)) File.Delete(recovered);
            File.Move(trashPath, recovered);

            Assert.True(File.Exists(recovered));
            using var rs = File.OpenRead(recovered);
            Assert.Equal(3, rs.Length);

            // cleanup
            File.Delete(recovered);
        }
    }
}
