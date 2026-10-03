namespace BusinessOS.POS.Persistence.Entities;

public sealed class GoodsReceiptExpenseEntity
{
    public long Id { get; set; }
    public long GoodsReceiptId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}
