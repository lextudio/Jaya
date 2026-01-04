//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.Shared.Services
{
    public enum TransferMode : byte
    {
        Copy,
        Move
    }

    public enum TransferProgressStage : byte
    {
        Started,
        ItemStarted,
        ItemCompleted,
        Completed,
        Canceled,
        Failed
    }

    public sealed class TransferProgressReport
    {
        public TransferProgressReport(Guid jobId, TransferMode mode, TransferProgressStage stage, int totalItems, int processedItems, string? currentItemName, string? currentItemPath, string? targetPath, string? message = null)
        {
            JobId = jobId;
            Mode = mode;
            Stage = stage;
            TotalItems = totalItems;
            ProcessedItems = processedItems;
            CurrentItemName = currentItemName;
            CurrentItemPath = currentItemPath;
            TargetPath = targetPath;
            Message = message;
        }

        public Guid JobId { get; }

        public TransferMode Mode { get; }

        public TransferProgressStage Stage { get; }

        public int TotalItems { get; }

        public int ProcessedItems { get; }

        public string? CurrentItemName { get; }

        public string? CurrentItemPath { get; }

        public string? TargetPath { get; }

        public string? Message { get; }
    }

    public interface IFileTransferService
    {
        Task<IReadOnlyList<FileSystemObjectModel>> TransferAsync(
            AccountModelBase account,
            IEnumerable<FileSystemObjectModel> items,
            DirectoryModel target,
            TransferMode mode,
            IProgress<TransferProgressReport>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
