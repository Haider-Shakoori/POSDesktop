using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Inventory;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Inventory;

public sealed partial class InventoryViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public InventoryViewModel(IInventoryService inventory, IPermissionAuthorizer authorizer)
    {
        _inventory = inventory;
        _authorizer = authorizer;

        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
        RecordOpeningStockCommand = new AsyncRelayCommand(RecordOpeningStockAsync);
        AdjustStockCommand = new AsyncRelayCommand(AdjustStockAsync);
        AddCountLineCommand = new RelayCommand(AddCountLine);
        RemoveCountLineCommand = new RelayCommand<StockCountEditorRow>(RemoveCountLine);
        CreateCountCommand = new AsyncRelayCommand(CreateCountAsync);
        ApproveCountCommand = new AsyncRelayCommand(ApproveCountAsync);
        AddWriteoffLineCommand = new RelayCommand(AddWriteoffLine);
        RemoveWriteoffLineCommand = new RelayCommand<WriteoffEditorRow>(RemoveWriteoffLine);
        PostWriteoffCommand = new AsyncRelayCommand(PostWriteoffAsync);
    }

    public ObservableCollection<InventoryStockRow> StockRows { get; } = [];
    public ObservableCollection<InventoryProductOption> Products { get; } = [];
    public ObservableCollection<InventoryTargetOption> Targets { get; } = [];
    public ObservableCollection<InventoryMovementRow> Movements { get; } = [];
    public ObservableCollection<StockCountSummary> StockCounts { get; } = [];
    public ObservableCollection<InventoryWriteoffResult> Writeoffs { get; } = [];
    public ObservableCollection<StockCountEditorRow> CountLines { get; } = [];
    public ObservableCollection<WriteoffEditorRow> WriteoffLines { get; } = [];

    public string[] AdjustmentDirections { get; } = ["Increase", "Decrease"];
    public string[] WriteoffTypes { get; } = ["damage", "expiry"];

    public bool CanOpeningStock => _authorizer.HasPermission("inventory.opening_stock");
    public bool CanAdjust => _authorizer.HasPermission("inventory.adjust");
    public bool CanCount => _authorizer.HasPermission("inventory.count");
    public bool CanApproveCount => _authorizer.HasPermission("inventory.count.approve");
    public bool CanWriteoff => _authorizer.HasPermission("inventory.writeoff");

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RecordOpeningStockCommand { get; }
    public IAsyncRelayCommand AdjustStockCommand { get; }
    public IRelayCommand AddCountLineCommand { get; }
    public IRelayCommand<StockCountEditorRow> RemoveCountLineCommand { get; }
    public IAsyncRelayCommand CreateCountCommand { get; }
    public IAsyncRelayCommand ApproveCountCommand { get; }
    public IRelayCommand AddWriteoffLineCommand { get; }
    public IRelayCommand<WriteoffEditorRow> RemoveWriteoffLineCommand { get; }
    public IAsyncRelayCommand PostWriteoffCommand { get; }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private string stockSearch = string.Empty;
    [ObservableProperty] private bool lowStockOnly;

    [ObservableProperty] private InventoryProductOption? selectedOpeningProduct;
    [ObservableProperty] private InventoryUnitOption? selectedOpeningUnit;
    [ObservableProperty] private decimal openingQuantity;
    [ObservableProperty] private decimal? openingUnitCost;
    [ObservableProperty] private string? openingBatchNumber;
    [ObservableProperty] private DateTime? openingManufacturedAt;
    [ObservableProperty] private DateTime? openingExpiresAt;
    [ObservableProperty] private string? openingNotes;

    [ObservableProperty] private InventoryTargetOption? selectedAdjustmentTarget;
    [ObservableProperty] private string selectedAdjustmentDirection = "Increase";
    [ObservableProperty] private decimal adjustmentQuantity;
    [ObservableProperty] private string adjustmentReason = string.Empty;
    [ObservableProperty] private string? adjustmentNotes;

    [ObservableProperty] private InventoryTargetOption? selectedCountTarget;
    [ObservableProperty] private decimal newCountPhysicalQuantity;
    [ObservableProperty] private string? countNotes;
    [ObservableProperty] private StockCountSummary? selectedStockCount;

    [ObservableProperty] private InventoryTargetOption? selectedWriteoffTarget;
    [ObservableProperty] private decimal newWriteoffQuantity;
    [ObservableProperty] private string selectedWriteoffType = "damage";
    [ObservableProperty] private string writeoffReason = string.Empty;
    [ObservableProperty] private string? writeoffNotes;

    partial void OnSelectedOpeningProductChanged(InventoryProductOption? value)
    {
        SelectedOpeningUnit = value?.Units.FirstOrDefault();
        OpeningBatchNumber = null;
        OpeningManufacturedAt = null;
        OpeningExpiresAt = null;
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        await ExecuteBusyAsync(async () =>
        {
            await ReloadReferenceDataAsync();
            await ReloadDataAsync();
            _loaded = true;
        });
    }

    private async Task RefreshAllAsync() =>
        await ExecuteBusyAsync(async () =>
        {
            await ReloadReferenceDataAsync();
            await ReloadDataAsync();
            StatusMessage = "Inventory refreshed.";
        });

    private async Task ReloadReferenceDataAsync()
    {
        var openingProductId = SelectedOpeningProduct?.ProductId;
        var adjustmentKey = SelectedAdjustmentTarget?.Key;
        var countKey = SelectedCountTarget?.Key;
        var writeoffKey = SelectedWriteoffTarget?.Key;

        var data = await _inventory.GetReferenceDataAsync();
        Products.Clear();
        foreach (var product in data.Products) Products.Add(product);
        Targets.Clear();
        foreach (var target in data.Targets) Targets.Add(target);

        SelectedOpeningProduct = Products.FirstOrDefault(x => x.ProductId == openingProductId) ?? Products.FirstOrDefault();
        SelectedAdjustmentTarget = Targets.FirstOrDefault(x => x.Key == adjustmentKey) ?? Targets.FirstOrDefault();
        SelectedCountTarget = Targets.FirstOrDefault(x => x.Key == countKey) ?? Targets.FirstOrDefault();
        SelectedWriteoffTarget = Targets.FirstOrDefault(x => x.Key == writeoffKey) ?? Targets.FirstOrDefault();
    }

    private async Task ReloadDataAsync()
    {
        StockRows.Clear();
        foreach (var row in await _inventory.GetStockAsync(StockSearch, LowStockOnly))
            StockRows.Add(row);

        Movements.Clear();
        foreach (var row in await _inventory.GetMovementsAsync())
            Movements.Add(row);

        StockCounts.Clear();
        foreach (var row in await _inventory.GetStockCountsAsync())
            StockCounts.Add(row);

        Writeoffs.Clear();
        foreach (var row in await _inventory.GetWriteoffsAsync())
            Writeoffs.Add(row);
    }

    private async Task RecordOpeningStockAsync()
    {
        if (!CanOpeningStock || SelectedOpeningProduct is null || SelectedOpeningUnit is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var movement = await _inventory.RecordOpeningStockAsync(new OpeningStockRequest(
                Guid.NewGuid().ToString(),
                SelectedOpeningProduct.ProductId,
                SelectedOpeningUnit.UnitId,
                OpeningQuantity,
                OpeningUnitCost,
                OpeningBatchNumber,
                OpeningManufacturedAt,
                OpeningExpiresAt,
                OpeningNotes));

            StatusMessage = "Opening stock recorded for " + movement.Sku + ".";
            OpeningQuantity = 0m;
            OpeningUnitCost = null;
            OpeningBatchNumber = null;
            OpeningManufacturedAt = null;
            OpeningExpiresAt = null;
            OpeningNotes = null;
            await ReloadReferenceDataAsync();
            await ReloadDataAsync();
        });
    }

    private async Task AdjustStockAsync()
    {
        if (!CanAdjust || SelectedAdjustmentTarget is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var movement = await _inventory.AdjustStockAsync(new StockAdjustmentRequest(
                Guid.NewGuid().ToString(),
                SelectedAdjustmentTarget.Key,
                AdjustmentQuantity,
                SelectedAdjustmentDirection == "Increase",
                AdjustmentReason,
                AdjustmentNotes));

            StatusMessage = "Stock adjustment posted: " + movement.MovementType + ".";
            AdjustmentQuantity = 0m;
            AdjustmentReason = string.Empty;
            AdjustmentNotes = null;
            await ReloadReferenceDataAsync();
            await ReloadDataAsync();
        });
    }

    private void AddCountLine()
    {
        if (SelectedCountTarget is null)
        {
            StatusMessage = "Select a stock-count target.";
            return;
        }
        if (CountLines.Any(x => x.Target.Key == SelectedCountTarget.Key))
        {
            StatusMessage = "That target is already in this count.";
            return;
        }

        CountLines.Add(new StockCountEditorRow
        {
            Target = SelectedCountTarget,
            PhysicalQuantityBase = NewCountPhysicalQuantity,
        });
        NewCountPhysicalQuantity = 0m;
    }

    private void RemoveCountLine(StockCountEditorRow? row)
    {
        if (row is not null) CountLines.Remove(row);
    }

    private async Task CreateCountAsync()
    {
        if (!CanCount) return;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _inventory.CreateStockCountAsync(new CreateStockCountRequest(
                Guid.NewGuid().ToString(),
                CountLines.Select(x => new StockCountLineRequest(
                    x.Target.Key,
                    x.PhysicalQuantityBase)).ToList(),
                CountNotes));

            StatusMessage = "Stock count " + result.Number + " created.";
            CountLines.Clear();
            CountNotes = null;
            await ReloadDataAsync();
            SelectedStockCount = StockCounts.FirstOrDefault(x => x.Id == result.Id);
        });
    }

    private async Task ApproveCountAsync()
    {
        if (!CanApproveCount || SelectedStockCount is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _inventory.ApproveStockCountAsync(
                SelectedStockCount.Id,
                Guid.NewGuid().ToString());

            StatusMessage = "Stock count " + result.Number + " approved.";
            await ReloadReferenceDataAsync();
            await ReloadDataAsync();
            SelectedStockCount = StockCounts.FirstOrDefault(x => x.Id == result.Id);
        });
    }

    private void AddWriteoffLine()
    {
        if (SelectedWriteoffTarget is null)
        {
            StatusMessage = "Select a write-off target.";
            return;
        }
        if (WriteoffLines.Any(x => x.Target.Key == SelectedWriteoffTarget.Key))
        {
            StatusMessage = "That target is already in this write-off.";
            return;
        }

        WriteoffLines.Add(new WriteoffEditorRow
        {
            Target = SelectedWriteoffTarget,
            QuantityBase = NewWriteoffQuantity,
        });
        NewWriteoffQuantity = 0m;
    }

    private void RemoveWriteoffLine(WriteoffEditorRow? row)
    {
        if (row is not null) WriteoffLines.Remove(row);
    }

    private async Task PostWriteoffAsync()
    {
        if (!CanWriteoff) return;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _inventory.PostWriteoffAsync(new InventoryWriteoffRequest(
                Guid.NewGuid().ToString(),
                SelectedWriteoffType,
                WriteoffReason,
                WriteoffLines.Select(x => new InventoryWriteoffLineRequest(
                    x.Target.Key,
                    x.QuantityBase)).ToList(),
                WriteoffNotes));

            StatusMessage = result.Number + " posted. Cost: AFN " + result.TotalCost.ToString("N2");
            WriteoffLines.Clear();
            WriteoffReason = string.Empty;
            WriteoffNotes = null;
            await ReloadReferenceDataAsync();
            await ReloadDataAsync();
        });
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
