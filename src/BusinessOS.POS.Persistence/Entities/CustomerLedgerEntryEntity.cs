namespace BusinessOS.POS.Persistence.Entities;

public sealed class CustomerLedgerEntryEntity
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long? ActorUserId { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Notes { get; set; }
}
