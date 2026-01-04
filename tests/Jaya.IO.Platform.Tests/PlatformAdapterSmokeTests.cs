using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Platform;

namespace Jaya.IO.Platform.Tests
{
    public class PlatformAdapterSmokeTests
    {
        [Fact]
        public async Task EnumerateVolumes_DoesNotThrow()
        {
            var vols = await Jaya.IO.FileSystem.Default.GetVolumesAsync();
            Assert.NotNull(vols);
        }
    }
}
