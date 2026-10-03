namespace BusinessOS.POS.Persistence.Entities;

public sealed class OperatingEntryEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long ExpenseCategoryId { get; set; }
    public long PaymentMethodId { get; set; }
    public long RecordedByUserId { get; set; }
    public string EntryType { get; set; } = "expense";
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
