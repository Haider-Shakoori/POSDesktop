namespace BusinessOS.POS.Persistence.Entities;

public sealed class StockCountEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ApprovalIdempotencyKey { get; set; }
    public long CountedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public string Status { get; set; } = "draft";
    public DateTimeOffset CountedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public List<StockCountItemEntity> Items { get; } = [];
}
