using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Jaya.IO.Mock;
using Jaya.IO.Models;

namespace Jaya.IO.Tests
{
    public class ConcurrencyTests
    {
        [Fact]
        public async Task Multiple_Transfers_CanRun_Concurrently()
        {
            var mock = new MockFileSystem();
            for (int i = 0; i < 10; i++)
            {
                mock.CreateFile($"/tmp/file{i}.bin", new byte[] { (byte)i });
            }

            Jaya.IO.FileSystem.Default = mock;

            var tasks = new List<Task<IReadOnlyList<CopyResult>>>();
            for (int t = 0; t < 5; t++)
            {
                var sources = Enumerable.Range(0, 10).Select(i => $"/tmp/file{i}.bin");
                tasks.Add(Jaya.IO.FileSystem.Default.TransferAsync(sources, $"/tmp/out{t}", TransferMode.Copy));
            }

            await Task.WhenAll(tasks);

            foreach (var task in tasks)
                Assert.Equal(10, task.Result.Count);
        }
    }
}
