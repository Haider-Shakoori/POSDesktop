using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Desktop.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Pos;

public sealed partial class PosViewModel : ObservableObject
{
    private readonly IPosService _pos;
    private readonly ISalesService _sales;
    private readonly ICustomerService _customers;
    private readonly IReceiptPrintService _printer;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;
    private bool _updatingPayments;

    public PosViewModel(
        IPosService pos,
        ISalesService sales,
        ICustomerService customers,
        IReceiptPrintService printer,
        IPermissionAuthorizer authorizer)
    {
        _pos = pos;
        _sales = sales;
        _customers = customers;
        _printer = printer;
        _authorizer = authorizer;

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        AddSelectedCommand = new RelayCommand(AddSelected);
        RemoveLineCommand = new RelayCommand<PosCartLineViewModel>(RemoveLine);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, CanCheckout);
        HoldCommand = new AsyncRelayCommand(HoldAsync, CanHold);
        OpenShiftCommand = new AsyncRelayCommand(OpenShiftAsync);
        ResumeHeldCommand = new AsyncRelayCommand(ResumeHeldAsync, CanResumeHeld);
        ReleaseHeldCommand = new AsyncRelayCommand(ReleaseHeldAsync, CanReleaseHeld);
        ClearCommand = new RelayCommand(() => ResetCart(true), () => Cart.Count > 0);
        AddPaymentCommand = new RelayCommand(AddPayment, CanAddPayment);
        RemovePaymentCommand = new RelayCommand<PosPaymentEditorRow>(RemovePayment);
        PrintLastReceiptCommand = new AsyncRelayCommand(PrintLastReceiptAsync, CanPrintLastReceipt);
        QuickCreateCustomerCommand = new AsyncRelayCommand(QuickCreateCustomerAsync, CanQuickCreate);
        RefreshCustomersCommand = new AsyncRelayCommand(RefreshCustomersAsync);
    }

    public ObservableCollection<PosProductSearchItem> SearchResults { get; } = [];
    public ObservableCollection<PosCartLineViewModel> Cart { get; } = [];
    public ObservableCollection<PosPaymentMethod> PaymentMethods { get; } = [];
    public ObservableCollection<PosPaymentEditorRow> Payments { get; } = [];
    public ObservableCollection<PosHeldSaleSummary> HeldSales { get; } = [];
    public ObservableCollection<PosCustomerOption> Customers { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand AddSelectedCommand { get; }
    public IRelayCommand<PosCartLineViewModel> RemoveLineCommand { get; }
    public IAsyncRelayCommand CheckoutCommand { get; }
    public IAsyncRelayCommand HoldCommand { get; }
    public IAsyncRelayCommand OpenShiftCommand { get; }
    public IAsyncRelayCommand ResumeHeldCommand { get; }
    public IAsyncRelayCommand ReleaseHeldCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand AddPaymentCommand { get; }
    public IRelayCommand<PosPaymentEditorRow> RemovePaymentCommand { get; }
    public IAsyncRelayCommand PrintLastReceiptCommand { get; }
    public IAsyncRelayCommand QuickCreateCustomerCommand { get; }
    public IAsyncRelayCommand RefreshCustomersCommand { get; }

    public bool CanDiscount => _authorizer.HasPermission("sales.discount");
    public bool CanHoldSales => _authorizer.HasPermission("sales.hold");
    public bool CanOpenShift => _authorizer.HasPermission("shifts.open");
    public bool CanCredit => _authorizer.HasPermission("sales.credit");
    public bool CanQuickCreateCustomer =>
        _authorizer.HasPermission("customers.quick_create") ||
        _authorizer.HasPermission("customers.manage");

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private PosProductSearchItem? selectedProduct;
    [ObservableProperty] private PosPaymentMethod? selectedPaymentMethodToAdd;
    [ObservableProperty] private PosHeldSaleSummary? selectedHeldSale;
    [ObservableProperty] private PosCustomerOption? selectedCustomer;
    [ObservableProperty] private string quickCustomerName = string.Empty;
    [ObservableProperty] private string? quickCustomerPhone;
    [ObservableProperty] private decimal quickCustomerCreditLimit;
    [ObservableProperty] private decimal saleDiscountAmount;
    [ObservableProperty] private string? saleNotes;
    [ObservableProperty] private decimal openingCash;
    [ObservableProperty] private bool isShiftOpen;
    [ObservableProperty] private string shiftStatus = "Shift closed";
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private long? lastSaleId;
    [ObservableProperty] private string? lastSaleNumber;
    [ObservableProperty] private bool isBusy;

    public decimal Subtotal => Money(Cart.Sum(x => x.Subtotal));
    public decimal LineDiscountTotal => Money(Cart.Sum(x => x.DiscountAmount));
    public decimal GrandTotal => Math.Max(0m, Money(Subtotal - LineDiscountTotal - SaleDiscountAmount));
    public decimal AppliedPaymentTotal => Money(Payments.Sum(x => x.Amount));
    public decimal ChangeAmount => Money(Payments.Sum(x => x.ChangeAmount));
    public decimal RemainingPaymentAmount => Money(Math.Max(0m, GrandTotal - AppliedPaymentTotal));
    public string TotalText => "AFN " + GrandTotal.ToString("N2");
    public string PaymentSummaryText =>
        AppliedPaymentTotal > GrandTotal
            ? "Applied payments exceed total"
            : RemainingPaymentAmount > 0m
                ? SelectedCustomer is not null && CanCredit
                    ? "Customer credit: AFN " + RemainingPaymentAmount.ToString("N2")
                    : "Remaining: AFN " + RemainingPaymentAmount.ToString("N2") + " · select customer for credit"
                : "Paid in full";

    partial void OnSaleDiscountAmountChanged(decimal value) => RaiseTotals();
    partial void OnSelectedHeldSaleChanged(PosHeldSaleSummary? value)
    {
        ResumeHeldCommand.NotifyCanExecuteChanged();
        ReleaseHeldCommand.NotifyCanExecuteChanged();
    }
    partial void OnLastSaleIdChanged(long? value) => PrintLastReceiptCommand.NotifyCanExecuteChanged();
    partial void OnSelectedPaymentMethodToAddChanged(PosPaymentMethod? value) => AddPaymentCommand.NotifyCanExecuteChanged();
    partial void OnSelectedCustomerChanged(PosCustomerOption? value) => RaisePaymentTotals();
    partial void OnQuickCustomerNameChanged(string value) => QuickCreateCustomerCommand.NotifyCanExecuteChanged();

    public async Task InitializeAsync()
    {
        if (_loaded) return;

        await ExecuteBusyAsync(async () =>
        {
            var reference = await _pos.GetReferenceDataAsync();
            PaymentMethods.Clear();
            foreach (var method in reference.PaymentMethods)
                PaymentMethods.Add(method);

            Customers.Clear();
            foreach (var customer in reference.Customers)
                Customers.Add(customer);

            SelectedPaymentMethodToAdd =
                PaymentMethods.FirstOrDefault(x => x.IsCash) ??
                PaymentMethods.FirstOrDefault();

            ApplyShift(reference.Shift);
            EnsureDefaultPayment();
            await SearchCoreAsync();
            await RefreshHeldAsync();
            _loaded = true;
        });
    }

    public async Task SearchAndMaybeAddExactAsync()
    {
        await SearchAsync();
        if (SearchResults.Count == 1 &&
            !string.IsNullOrWhiteSpace(SearchResults[0].MatchedBarcode))
        {
            AddProduct(SearchResults[0]);
            SearchText = string.Empty;
            await SearchAsync();
        }
    }

    public void AddProduct(PosProductSearchItem product)
    {
        var existing = Cart.FirstOrDefault(x => x.Product.ProductUnitId == product.ProductUnitId);
        if (existing is not null)
        {
            existing.Quantity += 1m;
            return;
        }

        var line = new PosCartLineViewModel(product);
        line.TotalsChanged += OnLineTotalsChanged;
        Cart.Add(line);
        RaiseTotals();
        StatusMessage = product.Name + " added.";
    }

    private async Task SearchAsync() => await ExecuteBusyAsync(SearchCoreAsync);

    private async Task SearchCoreAsync()
    {
        var items = await _pos.SearchProductsAsync(SearchText);
        SearchResults.Clear();
        foreach (var item in items)
            SearchResults.Add(item);
    }

    private void AddSelected()
    {
        if (SelectedProduct is not null)
            AddProduct(SelectedProduct);
    }

    private void RemoveLine(PosCartLineViewModel? line)
    {
        if (line is null) return;
        line.TotalsChanged -= OnLineTotalsChanged;
        Cart.Remove(line);
        RaiseTotals();
    }

    private void ResetCart(bool showMessage)
    {
        foreach (var line in Cart)
            line.TotalsChanged -= OnLineTotalsChanged;

        Cart.Clear();
        SaleDiscountAmount = 0m;
        SaleNotes = null;
        SelectedCustomer = null;
        ResetPayments();
        RaiseTotals();
        if (showMessage) StatusMessage = "Cart cleared.";
    }

    public async Task CheckoutAsync()
    {
        if (!CanCheckout()) return;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                Cart.Select(x => new PosCheckoutLineRequest(
                    x.Product.ProductUnitId,
                    x.Quantity,
                    x.DiscountAmount)).ToList(),
                SaleDiscountAmount,
                Payments.Select(x => new PosPaymentRequest(
                    x.Method.Code,
                    x.Amount,
                    x.TenderedAmount,
                    x.Reference,
                    x.Notes)).ToList(),
                SaleNotes,
                SelectedCustomer?.Id));

            LastSaleId = result.SaleId;
            LastSaleNumber = result.SaleNumber;
            ResetCart(false);
            StatusMessage = "Sale " + result.SaleNumber +
                            " completed · " + result.PaymentStatus +
                            (result.BalanceDue > 0m ? " · balance AFN " + result.BalanceDue.ToString("N2") : string.Empty) +
                            " · change AFN " + result.ChangeAmount.ToString("N2") +
                            ". Receipt is ready to print.";
            await RefreshCustomersCoreAsync();
            await SearchCoreAsync();
        });
    }

    public async Task HoldAsync()
    {
        if (!CanHold()) return;

        await ExecuteBusyAsync(async () =>
        {
            var held = await _pos.HoldAsync(new PosHoldRequest(
                Guid.NewGuid().ToString(),
                Cart.Select(x => new PosCheckoutLineRequest(
                    x.Product.ProductUnitId,
                    x.Quantity,
                    x.DiscountAmount)).ToList(),
                SaleDiscountAmount,
                SaleNotes,
                SelectedCustomer?.Id));

            ResetCart(false);
            StatusMessage = "Sale held as " + held.Number + ".";
            await RefreshHeldAsync();
        });
    }

    public async Task ResumeHeldAsync()
    {
        if (SelectedHeldSale is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var detail = await _pos.ResumeHeldSaleAsync(SelectedHeldSale.Id);
            ResetCart(false);

            foreach (var held in detail.Lines)
            {
                var product = new PosProductSearchItem(
                    held.ProductUnitId,
                    0,
                    held.Name,
                    held.Sku,
                    held.Unit,
                    held.DecimalPlaces,
                    held.ConversionFactor,
                    held.UnitPriceSnapshot,
                    held.MinimumPrice,
                    held.AvailableQuantity,
                    held.TrackStock,
                    null);

                var line = new PosCartLineViewModel(product)
                {
                    Quantity = held.Quantity,
                    DiscountAmount = held.LineDiscountAmount,
                };
                line.TotalsChanged += OnLineTotalsChanged;
                Cart.Add(line);
            }

            SaleDiscountAmount = detail.SaleDiscountAmount;
            SaleNotes = detail.Notes;
            SelectedCustomer = detail.CustomerId is null
                ? null
                : Customers.FirstOrDefault(x => x.Id == detail.CustomerId.Value);
            StatusMessage = detail.Number + " resumed.";
            await RefreshHeldAsync();
            RaiseTotals();
        });
    }

    public async Task ReleaseHeldAsync()
    {
        if (SelectedHeldSale is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var number = SelectedHeldSale.Number;
            await _pos.ReleaseHeldSaleAsync(SelectedHeldSale.Id);
            StatusMessage = number + " released.";
            await RefreshHeldAsync();
        });
    }

    public async Task OpenShiftAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var shift = await _pos.OpenShiftAsync(OpeningCash);
            ApplyShift(shift);
            StatusMessage = "Cashier shift opened.";
        });
    }

    private async Task PrintLastReceiptAsync()
    {
        if (LastSaleId is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var receipt = await _sales.GetReceiptAsync(LastSaleId.Value)
                ?? throw new InvalidOperationException("The completed sale could not be found.");

            StatusMessage = _printer.Print(receipt)
                ? "Receipt " + receipt.Sale.Number + " sent to printer."
                : "Receipt printing cancelled.";
        });
    }

    private void AddPayment()
    {
        if (!CanAddPayment() || SelectedPaymentMethodToAdd is null) return;

        var remaining = RemainingPaymentAmount;
        var row = new PosPaymentEditorRow
        {
            Method = SelectedPaymentMethodToAdd,
            Amount = remaining,
            TenderedAmount = remaining,
        };
        AttachPayment(row);
        Payments.Add(row);
        RaisePaymentTotals();
    }

    private void RemovePayment(PosPaymentEditorRow? row)
    {
        if (row is null) return;
        row.PaymentChanged -= OnPaymentChanged;
        Payments.Remove(row);
        RaisePaymentTotals();
    }

    private void ResetPayments()
    {
        foreach (var row in Payments)
            row.PaymentChanged -= OnPaymentChanged;
        Payments.Clear();
        EnsureDefaultPayment();
    }

    private void EnsureDefaultPayment()
    {
        if (Payments.Count > 0 || PaymentMethods.Count == 0) return;
        var method =
            PaymentMethods.FirstOrDefault(x => x.IsCash) ??
            PaymentMethods.First();

        var row = new PosPaymentEditorRow
        {
            Method = method,
            Amount = GrandTotal,
            TenderedAmount = GrandTotal,
        };
        AttachPayment(row);
        Payments.Add(row);
        SelectedPaymentMethodToAdd = method;
        RaisePaymentTotals();
    }

    private void AttachPayment(PosPaymentEditorRow row) =>
        row.PaymentChanged += OnPaymentChanged;

    private void OnPaymentChanged(object? sender, EventArgs e) => RaisePaymentTotals();

    private async Task RefreshCustomersAsync() =>
        await ExecuteBusyAsync(RefreshCustomersCoreAsync);

    private async Task RefreshCustomersCoreAsync()
    {
        var selectedId = SelectedCustomer?.Id;
        var rows = await _customers.GetCustomersAsync(activeOnly: true);
        Customers.Clear();
        foreach (var customer in rows)
        {
            Customers.Add(new PosCustomerOption(
                customer.Id, customer.Name, customer.Phone,
                customer.CreditLimit, customer.CurrentBalance));
        }

        SelectedCustomer = selectedId is null
            ? null
            : Customers.FirstOrDefault(x => x.Id == selectedId.Value);
    }

    private async Task QuickCreateCustomerAsync()
    {
        if (!CanQuickCreate()) return;

        await ExecuteBusyAsync(async () =>
        {
            var saved = await _customers.SaveCustomerAsync(new CustomerSaveRequest(
                null,
                QuickCustomerName,
                QuickCustomerPhone,
                null,
                null,
                QuickCustomerCreditLimit,
                0m,
                true));

            await RefreshCustomersCoreAsync();
            SelectedCustomer = Customers.FirstOrDefault(x => x.Id == saved.Id);
            QuickCustomerName = string.Empty;
            QuickCustomerPhone = null;
            QuickCustomerCreditLimit = 0m;
            StatusMessage = "Customer " + saved.Name + " created and selected.";
        });
    }

    private async Task RefreshHeldAsync()
    {
        HeldSales.Clear();
        if (!CanHoldSales) return;

        foreach (var held in await _pos.GetHeldSalesAsync())
            HeldSales.Add(held);

        SelectedHeldSale = HeldSales.FirstOrDefault();
    }

    private void ApplyShift(PosShiftState shift)
    {
        IsShiftOpen = shift.IsOpen;
        ShiftStatus = shift.IsOpen
            ? "Shift open · AFN " + shift.OpeningCash.ToString("N2")
            : "Shift closed";
        CheckoutCommand.NotifyCanExecuteChanged();
    }

    private void OnLineTotalsChanged(object? sender, EventArgs e) => RaiseTotals();

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(LineDiscountTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(TotalText));

        if (!_updatingPayments && Payments.Count == 1)
        {
            _updatingPayments = true;
            try
            {
                var row = Payments[0];
                row.Amount = GrandTotal;
                if (!row.IsCash || row.TenderedAmount < GrandTotal)
                    row.TenderedAmount = GrandTotal;
            }
            finally
            {
                _updatingPayments = false;
            }
        }

        RaisePaymentTotals();
        HoldCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private void RaisePaymentTotals()
    {
        OnPropertyChanged(nameof(AppliedPaymentTotal));
        OnPropertyChanged(nameof(ChangeAmount));
        OnPropertyChanged(nameof(RemainingPaymentAmount));
        OnPropertyChanged(nameof(PaymentSummaryText));
        CheckoutCommand.NotifyCanExecuteChanged();
        AddPaymentCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheckout()
    {
        if (IsBusy || Cart.Count == 0 || GrandTotal < 0m || Payments.Count > 8)
            return false;

        if (AppliedPaymentTotal > GrandTotal)
            return false;

        if (RemainingPaymentAmount > 0m && (SelectedCustomer is null || !CanCredit))
            return false;

        foreach (var payment in Payments)
        {
            if (payment.Amount <= 0m || payment.TenderedAmount < payment.Amount)
                return false;
            if (!payment.IsCash && payment.TenderedAmount != payment.Amount)
                return false;
        }

        return !Payments.Any(x => x.IsCash) || IsShiftOpen;
    }

    private bool CanHold() => !IsBusy && CanHoldSales && Cart.Count > 0;
    private bool CanResumeHeld() => !IsBusy && SelectedHeldSale is not null;
    private bool CanReleaseHeld() => !IsBusy && SelectedHeldSale is not null;
    private bool CanAddPayment() => !IsBusy && Payments.Count < 8 && SelectedPaymentMethodToAdd is not null;
    private bool CanPrintLastReceipt() => !IsBusy && LastSaleId is not null;
    private bool CanQuickCreate() => !IsBusy && CanQuickCreateCustomer && !string.IsNullOrWhiteSpace(QuickCustomerName);

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;

        IsBusy = true;
        NotifyCommands();
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
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        CheckoutCommand.NotifyCanExecuteChanged();
        HoldCommand.NotifyCanExecuteChanged();
        ResumeHeldCommand.NotifyCanExecuteChanged();
        ReleaseHeldCommand.NotifyCanExecuteChanged();
        AddPaymentCommand.NotifyCanExecuteChanged();
        PrintLastReceiptCommand.NotifyCanExecuteChanged();
        QuickCreateCustomerCommand.NotifyCanExecuteChanged();
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
