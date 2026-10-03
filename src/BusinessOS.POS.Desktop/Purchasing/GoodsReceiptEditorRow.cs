using BusinessOS.POS.Application.Abstractions.Purchasing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Purchasing;

public sealed partial class GoodsReceiptEditorRow : ObservableObject
{
    public long? PurchaseOrderItemId { get; init; }
    public required PurchaseProductOption Product { get; init; }
    public decimal? MaximumQuantity { get; init; }

    [ObservableProperty] private decimal quantity = 1m;
    [ObservableProperty] private decimal unitCost;
    [ObservableProperty] private decimal lineDiscountAmount;
    [ObservableProperty] private string? batchNumber;
    [ObservableProperty] private DateTime? manufacturedAt;
    [ObservableProperty] private DateTime? expiresAt;

    public string Description => Product.Sku + " · " + Product.Name + " · " + Product.Unit;
}
