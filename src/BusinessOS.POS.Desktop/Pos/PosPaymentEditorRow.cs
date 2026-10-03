using BusinessOS.POS.Application.Abstractions.Sales;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Pos;

public sealed partial class PosPaymentEditorRow : ObservableObject
{
    public required PosPaymentMethod Method { get; init; }

    [ObservableProperty] private decimal amount;
    [ObservableProperty] private decimal tenderedAmount;
    [ObservableProperty] private string? reference;
    [ObservableProperty] private string? notes;

    public event EventHandler? PaymentChanged;

    public string MethodName => Method.Name;
    public bool IsCash => Method.IsCash;
    public decimal ChangeAmount => IsCash
        ? Math.Max(0m, decimal.Round(TenderedAmount - Amount, 2, MidpointRounding.AwayFromZero))
        : 0m;

    partial void OnAmountChanged(decimal value)
    {
        if (!IsCash)
        {
            TenderedAmount = value;
        }
        else if (TenderedAmount < value)
        {
            TenderedAmount = value;
        }

        OnPropertyChanged(nameof(ChangeAmount));
        PaymentChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnTenderedAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(ChangeAmount));
        PaymentChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnReferenceChanged(string? value) => PaymentChanged?.Invoke(this, EventArgs.Empty);
    partial void OnNotesChanged(string? value) => PaymentChanged?.Invoke(this, EventArgs.Empty);
}
