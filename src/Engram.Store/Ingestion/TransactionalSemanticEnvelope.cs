using System;
using System.Collections.Generic;

namespace Engram.Store.Ingestion;

public enum TransactionStatus
{
    Started,
    Committed,
    Aborted,
    RolledBack
}

public class TransactionalSemanticEnvelope
{
    public Guid TransactionId { get; set; }
    public TransactionStatus Status { get; set; }
    public List<TransactionOperation> Operations { get; set; } = new();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class TransactionOperation
{
    public string FilePath { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string? PreviousContent { get; set; } // Null if creating a new file
    public string NewContent { get; set; } = string.Empty;
}
