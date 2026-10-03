using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Purchasing;

public sealed partial class GoodsReceiptExpenseEditorRow : ObservableObject
{
    [ObservableProperty] private string type = "transport";
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string? description;
}
