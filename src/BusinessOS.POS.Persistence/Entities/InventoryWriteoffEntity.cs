namespace BusinessOS.POS.Persistence.Entities;

public sealed class InventoryWriteoffEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string WriteoffType { get; set; } = string.Empty;
    public long PostedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public string? Notes { get; set; }
    public List<InventoryWriteoffItemEntity> Items { get; } = [];
}
