using BusinessOS.POS.Application.Abstractions.Purchasing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Purchasing;

public sealed partial class PurchaseOrderEditorRow : ObservableObject
{
    public required PurchaseProductOption Product { get; init; }
    [ObservableProperty] private decimal quantity = 1m;
    [ObservableProperty] private decimal unitCost;
    [ObservableProperty] private decimal lineDiscountAmount;
    [ObservableProperty] private string? notes;

    public decimal LineTotal => Math.Max(0m, decimal.Round(Quantity * UnitCost - LineDiscountAmount, 2));

    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
    partial void OnUnitCostChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
    partial void OnLineDiscountAmountChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
}
