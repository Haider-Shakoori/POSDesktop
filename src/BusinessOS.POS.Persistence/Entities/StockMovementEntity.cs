namespace BusinessOS.POS.Persistence.Entities;

public sealed class StockMovementEntity
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long? ActorUserId { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public decimal QuantityBase { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal? UnitCostBase { get; set; }
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
