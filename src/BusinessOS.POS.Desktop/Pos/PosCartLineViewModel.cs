using BusinessOS.POS.Application.Abstractions.Sales;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Pos;

public sealed partial class PosCartLineViewModel : ObservableObject
{
    public PosCartLineViewModel(PosProductSearchItem product)
    {
        Product = product;
        quantity = 1m;
    }

    public PosProductSearchItem Product { get; }

    [ObservableProperty]
    private decimal quantity;

    [ObservableProperty]
    private decimal discountAmount;

    public decimal Subtotal => decimal.Round(Quantity * Product.Price, 2, MidpointRounding.AwayFromZero);
    public decimal NetTotal => Math.Max(0m, decimal.Round(Subtotal - DiscountAmount, 2, MidpointRounding.AwayFromZero));
    public string Description => Product.Name + " · " + Product.Unit;
    public string StockText => Product.AvailableQuantity is null ? "Not tracked" : Product.AvailableQuantity.Value.ToString("0.######");

    public event EventHandler? TotalsChanged;

    partial void OnQuantityChanged(decimal value)
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(NetTotal));
        TotalsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDiscountAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(NetTotal));
        TotalsChanged?.Invoke(this, EventArgs.Empty);
    }
}
