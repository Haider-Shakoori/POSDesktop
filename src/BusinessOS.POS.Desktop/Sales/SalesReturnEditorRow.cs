using BusinessOS.POS.Application.Abstractions.Sales;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Sales;

public sealed partial class SalesReturnEditorRow : ObservableObject
{
    public required SaleDetailLine Line { get; init; }

    [ObservableProperty] private decimal requestedQuantity;

    public event EventHandler? ReturnChanged;

    public decimal EstimatedReturnAmount
    {
        get
        {
            if (RequestedQuantity <= 0m) return 0m;
            if (RequestedQuantity >= Line.ReturnableQuantity) return Line.ReturnableAmount;
            if (Line.Quantity <= 0m) return 0m;

            var amount = decimal.Round(
                RequestedQuantity * (Line.NetTotal / Line.Quantity),
                2,
                MidpointRounding.AwayFromZero);
            return Math.Min(amount, Line.ReturnableAmount);
        }
    }

    partial void OnRequestedQuantityChanged(decimal value)
    {
        OnPropertyChanged(nameof(EstimatedReturnAmount));
        ReturnChanged?.Invoke(this, EventArgs.Empty);
    }
}
