//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jaya.Shared.Services
{
    public enum DeleteMode : byte
    {
        Trash,
        Permanent
    }

    public interface IFileDeleteService
    {
        Task<bool> DeleteAsync(AccountModelBase account, IEnumerable<FileSystemObjectModel> items, DeleteMode mode);
    }
}
