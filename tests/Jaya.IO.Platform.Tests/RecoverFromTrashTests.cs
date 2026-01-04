using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Jaya.IO.Platform.Tests
{
    public class RecoverFromTrashTests
    {
        [Fact]
        public async Task RecoverFromManagedTrash_Works()
        {
            var trashRoot = Jaya.IO.FileSystem.EnableManagedTestTrash();

            var fs = Jaya.IO.FileSystem.Default;
            var src = Path.Combine(Path.GetTempPath(), "jaya_recover_test.bin");
            if (File.Exists(src)) File.Delete(src);

            using (var s = await fs.OpenWriteAsync(src, FileMode.Create))
                await s.WriteAsync(new byte[] { 11, 12, 13 }, 0, 3);

            var ok = await fs.DeleteAsync(src, Jaya.IO.Models.DeleteMode.Trash);
            Assert.True(ok);

            var files = Directory.GetFiles(trashRoot);
            Assert.NotEmpty(files);
            var trashPath = files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("jaya_recover_test", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(trashPath));

            var recovered = Path.Combine(Path.GetTempPath(), "jaya_recover_test_recovered.bin");
            if (File.Exists(recovered)) File.Delete(recovered);

            var recoveredOk = await Jaya.IO.FileSystem.RecoverFromTrashAsync(trashPath, recovered);
            Assert.True(recoveredOk);
            Assert.True(File.Exists(recovered));

            using var rs = File.OpenRead(recovered);
            Assert.Equal(3, rs.Length);

            File.Delete(recovered);
        }
    }
}
