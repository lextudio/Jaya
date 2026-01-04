using Jaya.Shared.Models;
using Jaya.Shared.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jaya.Provider.FileSystem.Services
{
    // Internal interface used only by the FileSystemService facade to interact with
    // platform-specific implementations without exposing ProviderServiceBase surface.
    internal interface INativeFileSystemService
    {
        Task<DirectoryModel?> GetDirectoryAsync(Jaya.Shared.Models.AccountModelBase account, DirectoryModel? directory = null);

        Task<bool> DeleteAsync(IEnumerable<FileSystemObjectModel> items, DeleteMode mode);
    }
}
