using System;

namespace Jaya.IO.Models;

public enum TransferMode { Copy, Move, Delete }
public enum TransferStage { Started, ItemStarted, ItemCompleted, Completed, Canceled, Failed }

public sealed class TransferProgressReport
{
    public Guid JobId { get; init; }
    public TransferMode Mode { get; init; }
    public TransferStage Stage { get; init; }
    public int TotalItems { get; init; }
    public int ProcessedItems { get; init; }
    public string? CurrentName { get; init; }
    public string? CurrentSource { get; init; }
    public string? CurrentDestination { get; init; }
    public string? Message { get; init; }
    public double? Percent { get; init; }
    public bool? CurrentItemSucceeded { get; init; }
}

public sealed class CopyResult
{
    public bool Success { get; init; }
    public Exception? Error { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public bool Skipped { get; init; }
}

public enum DeleteMode { Permanent, Trash }
public sealed class DeleteResult
{
    public IReadOnlyList<CopyResult> Results { get; init; } = Array.Empty<CopyResult>();
}
