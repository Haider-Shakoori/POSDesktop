namespace BusinessOS.POS.Persistence.Entities;

public sealed class SaleReturnItemEntity
{
    public long Id { get; set; }
    public long SaleReturnId { get; set; }
    public long SaleItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityBase { get; set; }
    public decimal ReturnAmount { get; set; }
    public decimal CogsAmount { get; set; }
}
