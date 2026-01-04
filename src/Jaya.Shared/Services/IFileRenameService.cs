using System;
using System.Threading;
using System.Threading.Tasks;
using Jaya.Shared.Models;
using System.Collections.Generic;

namespace Jaya.Shared.Services
{
    public interface IFileRenameService
    {
        Task<FileSystemObjectModel?> RenameAsync(AccountModelBase account, FileSystemObjectModel item, string newName, bool overwrite = false, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default);
    }
}
