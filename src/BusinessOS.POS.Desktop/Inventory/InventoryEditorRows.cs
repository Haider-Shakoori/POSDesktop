using BusinessOS.POS.Application.Abstractions.Inventory;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Inventory;

public sealed partial class StockCountEditorRow : ObservableObject
{
    public InventoryTargetOption Target { get; init; } = null!;
    [ObservableProperty] private decimal physicalQuantityBase;
}

public sealed partial class WriteoffEditorRow : ObservableObject
{
    public InventoryTargetOption Target { get; init; } = null!;
    [ObservableProperty] private decimal quantityBase;
}
