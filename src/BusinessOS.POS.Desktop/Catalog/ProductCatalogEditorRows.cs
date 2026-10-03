using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Catalog;

public sealed partial class ProductUnitEditorRow : ObservableObject
{
    public long UnitId { get; init; }
    public string UnitName { get; init; } = string.Empty;
    public string UnitCode { get; init; } = string.Empty;
    [ObservableProperty] private decimal conversionFactor = 1m;
    [ObservableProperty] private bool canPurchase;
    [ObservableProperty] private bool canSell = true;
    [ObservableProperty] private decimal? sellingPrice;
    [ObservableProperty] private decimal? minimumSellingPrice;
    [ObservableProperty] private decimal? wholesalePrice;
}

public sealed partial class BarcodeEditorRow : ObservableObject
{
    public long UnitId { get; init; }
    public string UnitName { get; init; } = string.Empty;
    [ObservableProperty] private string barcode = string.Empty;
    [ObservableProperty] private bool isPrimary;
}

public sealed record BarcodeUnitOption(long UnitId, string Name);
