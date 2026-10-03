using BusinessOS.POS.Application.Abstractions.Purchasing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Purchasing;

public sealed partial class PurchaseReturnEditorRow : ObservableObject
{
    public required GoodsReceiptLine Line { get; init; }
    [ObservableProperty] private decimal quantity;

    public decimal EstimatedAmount =>
        Quantity <= 0m || Line.ReturnableQuantity <= 0m
            ? 0m
            : Quantity >= Line.ReturnableQuantity
                ? Line.ReturnableAmount
                : decimal.Round(Quantity * (Line.LandedTotal / Line.Quantity), 2);

    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(EstimatedAmount));
}
