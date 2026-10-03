using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Purchasing;

public sealed partial class PurchasingViewModel : ObservableObject
{
    private readonly IPurchasingService _purchasing;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public PurchasingViewModel(IPurchasingService purchasing, IPermissionAuthorizer authorizer)
    {
        _purchasing = purchasing;
        _authorizer = authorizer;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewSupplierCommand = new RelayCommand(NewSupplier);
        SaveSupplierCommand = new AsyncRelayCommand(SaveSupplierAsync);

        AddOrderLineCommand = new RelayCommand(AddOrderLine);
        RemoveOrderLineCommand = new RelayCommand<PurchaseOrderEditorRow>(RemoveOrderLine);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync);
        ApproveOrderCommand = new AsyncRelayCommand(ApproveOrderAsync);
        CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync);

        PrepareReceiptFromOrderCommand = new AsyncRelayCommand(PrepareReceiptFromOrderAsync);
        NewDirectReceiptCommand = new RelayCommand(NewDirectReceipt);
        AddReceiptLineCommand = new RelayCommand(AddReceiptLine);
        RemoveReceiptLineCommand = new RelayCommand<GoodsReceiptEditorRow>(RemoveReceiptLine);
        AddExpenseCommand = new RelayCommand(AddExpense);
        RemoveExpenseCommand = new RelayCommand<GoodsReceiptExpenseEditorRow>(RemoveExpense);
        PostReceiptCommand = new AsyncRelayCommand(PostReceiptAsync);

        PostReturnCommand = new AsyncRelayCommand(PostReturnAsync);
        RecordSupplierPaymentCommand = new AsyncRelayCommand(RecordSupplierPaymentAsync);
    }

    public ObservableCollection<SupplierSummary> Suppliers { get; } = [];
    public ObservableCollection<SupplierLedgerRow> SupplierLedger { get; } = [];
    public ObservableCollection<SupplierOpenReceiptRow> SupplierOpenReceipts { get; } = [];
    public ObservableCollection<SupplierPaymentRow> SupplierPayments { get; } = [];

    public ObservableCollection<PurchaseProductOption> Products { get; } = [];
    public ObservableCollection<PurchaseOrderSummary> Orders { get; } = [];
    public ObservableCollection<GoodsReceiptSummary> Receipts { get; } = [];
    public ObservableCollection<PurchasePaymentMethodOption> PaymentMethods { get; } = [];

    public ObservableCollection<PurchaseOrderEditorRow> OrderLines { get; } = [];
    public ObservableCollection<GoodsReceiptEditorRow> ReceiptLines { get; } = [];
    public ObservableCollection<GoodsReceiptExpenseEditorRow> ReceiptExpenses { get; } = [];
    public ObservableCollection<PurchaseReturnEditorRow> ReturnLines { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewSupplierCommand { get; }
    public IAsyncRelayCommand SaveSupplierCommand { get; }
    public IRelayCommand AddOrderLineCommand { get; }
    public IRelayCommand<PurchaseOrderEditorRow> RemoveOrderLineCommand { get; }
    public IAsyncRelayCommand CreateOrderCommand { get; }
    public IAsyncRelayCommand ApproveOrderCommand { get; }
    public IAsyncRelayCommand CancelOrderCommand { get; }
    public IAsyncRelayCommand PrepareReceiptFromOrderCommand { get; }
    public IRelayCommand NewDirectReceiptCommand { get; }
    public IRelayCommand AddReceiptLineCommand { get; }
    public IRelayCommand<GoodsReceiptEditorRow> RemoveReceiptLineCommand { get; }
    public IRelayCommand AddExpenseCommand { get; }
    public IRelayCommand<GoodsReceiptExpenseEditorRow> RemoveExpenseCommand { get; }
    public IAsyncRelayCommand PostReceiptCommand { get; }
    public IAsyncRelayCommand PostReturnCommand { get; }
    public IAsyncRelayCommand RecordSupplierPaymentCommand { get; }

    public bool CanManageSuppliers => _authorizer.HasPermission("suppliers.manage");
    public bool CanCreateOrders => _authorizer.HasPermission("purchases.create");
    public bool CanApproveOrders => _authorizer.HasPermission("purchases.approve");
    public bool CanReceive => _authorizer.HasPermission("purchases.receive");
    public bool CanDirectReceive => _authorizer.HasPermission("purchases.direct_receive");
    public bool CanReturnPurchases => _authorizer.HasPermission("purchases.return");
    public bool CanPaySuppliers => _authorizer.HasPermission("suppliers.pay");

    [ObservableProperty] private SupplierSummary? selectedSupplier;
    [ObservableProperty] private long? editingSupplierId;
    [ObservableProperty] private string supplierName = string.Empty;
    [ObservableProperty] private string? supplierContactPerson;
    [ObservableProperty] private string? supplierPhone;
    [ObservableProperty] private string? supplierAlternatePhone;
    [ObservableProperty] private string? supplierAddress;
    [ObservableProperty] private decimal supplierOpeningBalance;
    [ObservableProperty] private string? supplierNotes;
    [ObservableProperty] private bool supplierIsActive = true;

    [ObservableProperty] private PurchaseProductOption? selectedOrderProduct;
    [ObservableProperty] private decimal orderDiscountAmount;
    [ObservableProperty] private DateTime orderDate = DateTime.Today;
    [ObservableProperty] private DateTime? expectedDate;
    [ObservableProperty] private string? orderSupplierReference;
    [ObservableProperty] private string? orderNotes;
    [ObservableProperty] private PurchaseOrderSummary? selectedOrder;
    [ObservableProperty] private PurchaseOrderDetail? selectedOrderDetail;

    [ObservableProperty] private PurchaseProductOption? selectedReceiptProduct;
    [ObservableProperty] private long? receiptPurchaseOrderId;
    [ObservableProperty] private string receiptMode = "Direct receipt";
    [ObservableProperty] private string? supplierInvoiceReference;
    [ObservableProperty] private decimal receiptDiscountAmount;
    [ObservableProperty] private decimal receiptPaidAmount;
    [ObservableProperty] private PurchasePaymentMethodOption? selectedReceiptPaymentMethod;
    [ObservableProperty] private string? receiptPaymentReference;
    [ObservableProperty] private string? receiptNotes;

    [ObservableProperty] private GoodsReceiptSummary? selectedReceipt;
    [ObservableProperty] private GoodsReceiptDetail? selectedReceiptDetail;
    [ObservableProperty] private string returnReason = string.Empty;

    [ObservableProperty] private decimal supplierPaymentAmount;
    [ObservableProperty] private PurchasePaymentMethodOption? selectedSupplierPaymentMethod;
    [ObservableProperty] private string? supplierPaymentReference;
    [ObservableProperty] private string? supplierPaymentNotes;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";

    public decimal OrderDraftTotal =>
        Math.Max(0m, decimal.Round(OrderLines.Sum(x => x.LineTotal) - OrderDiscountAmount, 2));

    public decimal ReturnDraftTotal =>
        decimal.Round(ReturnLines.Sum(x => x.EstimatedAmount), 2);

    partial void OnSelectedSupplierChanged(SupplierSummary? value)
    {
        if (value is null)
        {
            SupplierLedger.Clear();
            SupplierOpenReceipts.Clear();
            SupplierPayments.Clear();
            return;
        }

        EditingSupplierId = value.Id;
        SupplierName = value.Name;
        SupplierContactPerson = value.ContactPerson;
        SupplierPhone = value.Phone;
        SupplierAlternatePhone = value.AlternatePhone;
        SupplierAddress = value.Address;
        SupplierOpeningBalance = value.OpeningBalance;
        SupplierNotes = value.Notes;
        SupplierIsActive = value.IsActive;
        _ = LoadSupplierDetailAsync();
    }

    partial void OnSelectedOrderChanged(PurchaseOrderSummary? value) => _ = LoadOrderDetailAsync();
    partial void OnSelectedReceiptChanged(GoodsReceiptSummary? value) => _ = LoadReceiptDetailAsync();

    partial void OnOrderDiscountAmountChanged(decimal value) => OnPropertyChanged(nameof(OrderDraftTotal));

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        await ExecuteBusyAsync(async () =>
        {
            await LoadReferenceDataAsync();
            await RefreshCoreAsync();
            _loaded = true;
        });
    }

    private async Task LoadReferenceDataAsync()
    {
        Products.Clear();
        foreach (var product in await _purchasing.GetPurchasableProductsAsync())
            Products.Add(product);

        PaymentMethods.Clear();
        foreach (var method in await _purchasing.GetPaymentMethodsAsync())
            PaymentMethods.Add(method);

        SelectedReceiptPaymentMethod =
            PaymentMethods.FirstOrDefault(x => x.IsCash) ?? PaymentMethods.FirstOrDefault();
        SelectedSupplierPaymentMethod = SelectedReceiptPaymentMethod;
        SelectedOrderProduct = Products.FirstOrDefault();
        SelectedReceiptProduct = Products.FirstOrDefault();
    }

    private async Task RefreshAsync() =>
        await ExecuteBusyAsync(async () =>
        {
            await RefreshCoreAsync();
            StatusMessage = "Purchasing data refreshed.";
        });

    private async Task RefreshCoreAsync()
    {
        var supplierId = SelectedSupplier?.Id;
        var orderId = SelectedOrder?.Id;
        var receiptId = SelectedReceipt?.Id;

        Suppliers.Clear();
        foreach (var row in await _purchasing.GetSuppliersAsync())
            Suppliers.Add(row);

        Orders.Clear();
        foreach (var row in await _purchasing.GetPurchaseOrdersAsync())
            Orders.Add(row);

        Receipts.Clear();
        foreach (var row in await _purchasing.GetGoodsReceiptsAsync())
            Receipts.Add(row);

        SelectedSupplier = supplierId is null
            ? Suppliers.FirstOrDefault()
            : Suppliers.FirstOrDefault(x => x.Id == supplierId.Value) ?? Suppliers.FirstOrDefault();
        SelectedOrder = orderId is null
            ? Orders.FirstOrDefault()
            : Orders.FirstOrDefault(x => x.Id == orderId.Value) ?? Orders.FirstOrDefault();
        SelectedReceipt = receiptId is null
            ? Receipts.FirstOrDefault()
            : Receipts.FirstOrDefault(x => x.Id == receiptId.Value) ?? Receipts.FirstOrDefault();
    }

    private async Task LoadSupplierDetailAsync()
    {
        if (SelectedSupplier is null) return;
        try
        {
            var detail = await _purchasing.GetSupplierAsync(SelectedSupplier.Id);
            SupplierLedger.Clear();
            SupplierOpenReceipts.Clear();
            SupplierPayments.Clear();
            if (detail is null) return;
            foreach (var row in detail.Ledger) SupplierLedger.Add(row);
            foreach (var row in detail.OpenReceipts) SupplierOpenReceipts.Add(row);
            foreach (var row in detail.Payments) SupplierPayments.Add(row);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void NewSupplier()
    {
        SelectedSupplier = null;
        EditingSupplierId = null;
        SupplierName = string.Empty;
        SupplierContactPerson = null;
        SupplierPhone = null;
        SupplierAlternatePhone = null;
        SupplierAddress = null;
        SupplierOpeningBalance = 0m;
        SupplierNotes = null;
        SupplierIsActive = true;
        StatusMessage = "New supplier form ready.";
    }

    private async Task SaveSupplierAsync()
    {
        if (!CanManageSuppliers) return;
        await ExecuteBusyAsync(async () =>
        {
            var saved = await _purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                EditingSupplierId, SupplierName, SupplierContactPerson, SupplierPhone,
                SupplierAlternatePhone, SupplierAddress, SupplierOpeningBalance,
                SupplierNotes, SupplierIsActive));
            await RefreshCoreAsync();
            SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == saved.Id);
            StatusMessage = "Supplier " + saved.Name + " saved.";
        });
    }

    private void AddOrderLine()
    {
        if (SelectedOrderProduct is null) return;
        if (OrderLines.Any(x => x.Product.ProductUnitId == SelectedOrderProduct.ProductUnitId))
        {
            StatusMessage = "That product unit is already on the purchase order.";
            return;
        }

        var row = new PurchaseOrderEditorRow
        {
            Product = SelectedOrderProduct,
            Quantity = 1m,
            UnitCost = SelectedOrderProduct.PurchaseCost,
        };
        row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(OrderDraftTotal));
        OrderLines.Add(row);
        OnPropertyChanged(nameof(OrderDraftTotal));
    }

    private void RemoveOrderLine(PurchaseOrderEditorRow? row)
    {
        if (row is null) return;
        OrderLines.Remove(row);
        OnPropertyChanged(nameof(OrderDraftTotal));
    }

    private async Task CreateOrderAsync()
    {
        if (!CanCreateOrders || SelectedSupplier is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var created = await _purchasing.CreatePurchaseOrderAsync(new PurchaseOrderCreateRequest(
                SelectedSupplier.Id, OrderDate, ExpectedDate, OrderSupplierReference,
                OrderDiscountAmount, OrderNotes,
                OrderLines.Select(x => new PurchaseOrderLineRequest(
                    x.Product.ProductUnitId, x.Quantity, x.UnitCost,
                    x.LineDiscountAmount, x.Notes)).ToList()));

            OrderLines.Clear();
            OrderDiscountAmount = 0m;
            OrderSupplierReference = null;
            OrderNotes = null;
            await RefreshCoreAsync();
            SelectedOrder = Orders.FirstOrDefault(x => x.Id == created.Header.Id);
            StatusMessage = "Purchase order " + created.Header.Number + " created.";
        });
    }

    private async Task LoadOrderDetailAsync()
    {
        if (SelectedOrder is null)
        {
            SelectedOrderDetail = null;
            return;
        }

        try { SelectedOrderDetail = await _purchasing.GetPurchaseOrderAsync(SelectedOrder.Id); }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private async Task ApproveOrderAsync()
    {
        if (!CanApproveOrders || SelectedOrder is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var result = await _purchasing.ApprovePurchaseOrderAsync(SelectedOrder.Id);
            await RefreshCoreAsync();
            SelectedOrder = Orders.FirstOrDefault(x => x.Id == result.Header.Id);
            StatusMessage = result.Header.Number + " approved.";
        });
    }

    private async Task CancelOrderAsync()
    {
        if (!CanApproveOrders || SelectedOrder is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var result = await _purchasing.CancelPurchaseOrderAsync(SelectedOrder.Id);
            await RefreshCoreAsync();
            SelectedOrder = Orders.FirstOrDefault(x => x.Id == result.Header.Id);
            StatusMessage = result.Header.Number + " cancelled.";
        });
    }

    private async Task PrepareReceiptFromOrderAsync()
    {
        if (SelectedOrder is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var order = await _purchasing.GetPurchaseOrderAsync(SelectedOrder.Id)
                ?? throw new InvalidOperationException("Purchase order was not found.");
            if (order.Header.Status is not ("approved" or "partially_received"))
                throw new InvalidOperationException("Select an approved open purchase order.");

            var productMap = Products.ToDictionary(x => x.ProductUnitId);
            ReceiptLines.Clear();
            foreach (var line in order.Items.Where(x => x.RemainingQuantity > 0m))
            {
                if (!productMap.TryGetValue(line.ProductUnitId, out var product)) continue;
                ReceiptLines.Add(new GoodsReceiptEditorRow
                {
                    PurchaseOrderItemId = line.Id,
                    Product = product,
                    MaximumQuantity = line.RemainingQuantity,
                    Quantity = line.RemainingQuantity,
                    UnitCost = line.UnitCost,
                });
            }

            ReceiptPurchaseOrderId = order.Header.Id;
            ReceiptMode = "PO receipt · " + order.Header.Number;
            SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == order.Header.SupplierId);
            SupplierInvoiceReference = null;
            ReceiptDiscountAmount = 0m;
            ReceiptPaidAmount = 0m;
            ReceiptNotes = null;
            StatusMessage = order.Header.Number + " loaded for receiving.";
        });
    }

    private void NewDirectReceipt()
    {
        ReceiptPurchaseOrderId = null;
        ReceiptMode = "Direct receipt";
        ReceiptLines.Clear();
        ReceiptExpenses.Clear();
        SupplierInvoiceReference = null;
        ReceiptDiscountAmount = 0m;
        ReceiptPaidAmount = 0m;
        ReceiptPaymentReference = null;
        ReceiptNotes = null;
        StatusMessage = "Direct goods receipt ready.";
    }

    private void AddReceiptLine()
    {
        if (SelectedReceiptProduct is null) return;
        if (ReceiptPurchaseOrderId is not null)
        {
            StatusMessage = "PO receipts use the purchase-order lines already loaded.";
            return;
        }

        if (ReceiptLines.Any(x => x.Product.ProductUnitId == SelectedReceiptProduct.ProductUnitId))
        {
            StatusMessage = "That product unit is already on the receipt.";
            return;
        }

        ReceiptLines.Add(new GoodsReceiptEditorRow
        {
            Product = SelectedReceiptProduct,
            Quantity = 1m,
            UnitCost = SelectedReceiptProduct.PurchaseCost,
        });
    }

    private void RemoveReceiptLine(GoodsReceiptEditorRow? row)
    {
        if (row is not null) ReceiptLines.Remove(row);
    }

    private void AddExpense() => ReceiptExpenses.Add(new GoodsReceiptExpenseEditorRow { Type = "transport" });

    private void RemoveExpense(GoodsReceiptExpenseEditorRow? row)
    {
        if (row is not null) ReceiptExpenses.Remove(row);
    }

    private async Task PostReceiptAsync()
    {
        if (!CanReceive || SelectedSupplier is null) return;
        if (ReceiptPurchaseOrderId is null && !CanDirectReceive)
        {
            StatusMessage = "You do not have permission to post direct receipts.";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var result = await _purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(),
                SelectedSupplier.Id,
                ReceiptPurchaseOrderId,
                SupplierInvoiceReference,
                DateTimeOffset.UtcNow,
                ReceiptDiscountAmount,
                ReceiptPaidAmount,
                ReceiptPaidAmount > 0m ? SelectedReceiptPaymentMethod?.Code : null,
                ReceiptPaymentReference,
                null,
                ReceiptNotes,
                ReceiptLines.Select(x => new GoodsReceiptLineRequest(
                    x.PurchaseOrderItemId,
                    x.PurchaseOrderItemId is null ? x.Product.ProductUnitId : null,
                    x.Quantity,
                    x.PurchaseOrderItemId is null ? x.UnitCost : null,
                    x.LineDiscountAmount,
                    x.BatchNumber,
                    x.ManufacturedAt,
                    x.ExpiresAt)).ToList(),
                ReceiptExpenses.Select(x => new GoodsReceiptExpenseRequest(
                    x.Type, x.Amount, x.Description)).ToList()));

            NewDirectReceipt();
            await RefreshCoreAsync();
            SelectedReceipt = Receipts.FirstOrDefault(x => x.Id == result.Header.Id);
            StatusMessage = "Goods receipt " + result.Header.Number + " posted.";
        });
    }

    private async Task LoadReceiptDetailAsync()
    {
        ReturnLines.Clear();
        if (SelectedReceipt is null)
        {
            SelectedReceiptDetail = null;
            OnPropertyChanged(nameof(ReturnDraftTotal));
            return;
        }

        try
        {
            SelectedReceiptDetail = await _purchasing.GetGoodsReceiptAsync(SelectedReceipt.Id);
            if (SelectedReceiptDetail is not null)
            {
                foreach (var line in SelectedReceiptDetail.Items.Where(x => x.ReturnableQuantity > 0m))
                {
                    var row = new PurchaseReturnEditorRow { Line = line };
                    row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ReturnDraftTotal));
                    ReturnLines.Add(row);
                }
            }
            OnPropertyChanged(nameof(ReturnDraftTotal));
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private async Task PostReturnAsync()
    {
        if (!CanReturnPurchases || SelectedReceiptDetail is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var result = await _purchasing.PostPurchaseReturnAsync(new PurchaseReturnRequest(
                Guid.NewGuid().ToString(),
                SelectedReceiptDetail.Header.Id,
                ReturnReason,
                ReturnLines.Where(x => x.Quantity > 0m)
                    .Select(x => new PurchaseReturnLineRequest(x.Line.Id, x.Quantity)).ToList()));

            ReturnReason = string.Empty;
            await RefreshCoreAsync();
            SelectedReceipt = Receipts.FirstOrDefault(x => x.Id == result.GoodsReceiptId);
            StatusMessage = result.Number + " posted · AFN " + result.ReturnTotal.ToString("N2") + ".";
        });
    }

    private async Task RecordSupplierPaymentAsync()
    {
        if (!CanPaySuppliers || SelectedSupplier is null || SelectedSupplierPaymentMethod is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var result = await _purchasing.RecordSupplierPaymentAsync(new SupplierPaymentRequest(
                Guid.NewGuid().ToString(),
                SelectedSupplier.Id,
                SupplierPaymentAmount,
                SelectedSupplierPaymentMethod.Code,
                SupplierPaymentReference,
                DateTimeOffset.UtcNow,
                SupplierPaymentNotes));

            SupplierPaymentAmount = 0m;
            SupplierPaymentReference = null;
            SupplierPaymentNotes = null;
            await RefreshCoreAsync();
            SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == result.SupplierId);
            StatusMessage = result.Number + " recorded · balance AFN " + result.SupplierBalanceAfter.ToString("N2") + ".";
        });
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
