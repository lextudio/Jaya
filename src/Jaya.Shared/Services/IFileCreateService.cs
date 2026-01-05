using Jaya.Shared.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.Shared.Services
{
    public interface IFileCreateService
    {
        Task<FileSystemObjectModel?> CreateDirectoryAsync(AccountModelBase account, DirectoryModel parentDirectory, string name, CancellationToken cancellationToken = default);
    }
}
