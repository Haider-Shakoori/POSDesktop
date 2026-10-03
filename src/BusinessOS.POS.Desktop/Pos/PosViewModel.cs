using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Pos;

public sealed partial class PosViewModel : ObservableObject
{
    private readonly IPosService _pos;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public PosViewModel(IPosService pos, IPermissionAuthorizer authorizer)
    {
        _pos = pos;
        _authorizer = authorizer;

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        AddSelectedCommand = new RelayCommand(AddSelected);
        RemoveLineCommand = new RelayCommand<PosCartLineViewModel>(RemoveLine);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, CanCheckout);
        HoldCommand = new AsyncRelayCommand(HoldAsync, CanHold);
        OpenShiftCommand = new AsyncRelayCommand(OpenShiftAsync);
        ResumeHeldCommand = new AsyncRelayCommand(ResumeHeldAsync, CanResumeHeld);
        ClearCommand = new RelayCommand(ClearCart, () => Cart.Count > 0);
    }

    public ObservableCollection<PosProductSearchItem> SearchResults { get; } = [];
    public ObservableCollection<PosCartLineViewModel> Cart { get; } = [];
    public ObservableCollection<PosPaymentMethod> PaymentMethods { get; } = [];
    public ObservableCollection<PosHeldSaleSummary> HeldSales { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand AddSelectedCommand { get; }
    public IRelayCommand<PosCartLineViewModel> RemoveLineCommand { get; }
    public IAsyncRelayCommand CheckoutCommand { get; }
    public IAsyncRelayCommand HoldCommand { get; }
    public IAsyncRelayCommand OpenShiftCommand { get; }
    public IAsyncRelayCommand ResumeHeldCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public bool CanDiscount => _authorizer.HasPermission("sales.discount");
    public bool CanHoldSales => _authorizer.HasPermission("sales.hold");
    public bool CanOpenShift => _authorizer.HasPermission("shifts.open");

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private PosProductSearchItem? selectedProduct;
    [ObservableProperty] private PosPaymentMethod? selectedPaymentMethod;
    [ObservableProperty] private PosHeldSaleSummary? selectedHeldSale;
    [ObservableProperty] private decimal paymentAmount;
    [ObservableProperty] private decimal saleDiscountAmount;
    [ObservableProperty] private decimal openingCash;
    [ObservableProperty] private bool isShiftOpen;
    [ObservableProperty] private string shiftStatus = "Shift closed";
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private string? lastSaleNumber;
    [ObservableProperty] private bool isBusy;

    public decimal Subtotal => decimal.Round(Cart.Sum(x => x.Subtotal), 2, MidpointRounding.AwayFromZero);
    public decimal LineDiscountTotal => decimal.Round(Cart.Sum(x => x.DiscountAmount), 2, MidpointRounding.AwayFromZero);
    public decimal GrandTotal => Math.Max(0m, decimal.Round(Subtotal - LineDiscountTotal - SaleDiscountAmount, 2, MidpointRounding.AwayFromZero));
    public decimal ChangeAmount => Math.Max(0m, decimal.Round(PaymentAmount - GrandTotal, 2, MidpointRounding.AwayFromZero));
    public string TotalText => "AFN " + GrandTotal.ToString("N2");

    partial void OnSaleDiscountAmountChanged(decimal value) => RaiseTotals();
    partial void OnPaymentAmountChanged(decimal value) => OnPropertyChanged(nameof(ChangeAmount));
    partial void OnSelectedHeldSaleChanged(PosHeldSaleSummary? value) => ResumeHeldCommand.NotifyCanExecuteChanged();

    public async Task InitializeAsync()
    {
        if (_loaded)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var reference = await _pos.GetReferenceDataAsync();
            PaymentMethods.Clear();
            foreach (var method in reference.PaymentMethods)
            {
                PaymentMethods.Add(method);
            }

            SelectedPaymentMethod = PaymentMethods.FirstOrDefault(x => x.IsCash) ?? PaymentMethods.FirstOrDefault();
            ApplyShift(reference.Shift);
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
        {
            SearchResults.Add(item);
        }
    }

    private void AddSelected()
    {
        if (SelectedProduct is not null)
        {
            AddProduct(SelectedProduct);
        }
    }

    private void RemoveLine(PosCartLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        line.TotalsChanged -= OnLineTotalsChanged;
        Cart.Remove(line);
        RaiseTotals();
    }

    private void ClearCart()
    {
        foreach (var line in Cart)
        {
            line.TotalsChanged -= OnLineTotalsChanged;
        }

        Cart.Clear();
        SaleDiscountAmount = 0m;
        RaiseTotals();
        StatusMessage = "Cart cleared.";
    }

    public async Task CheckoutAsync()
    {
        if (!CanCheckout() || SelectedPaymentMethod is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var result = await _pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                Cart.Select(x => new PosCheckoutLineRequest(
                    x.Product.ProductUnitId,
                    x.Quantity,
                    x.DiscountAmount)).ToList(),
                SaleDiscountAmount,
                [new PosPaymentRequest(SelectedPaymentMethod.Code, PaymentAmount)],
                null));

            LastSaleNumber = result.SaleNumber;
            StatusMessage = "Sale " + result.SaleNumber + " completed. Change: AFN " + result.ChangeAmount.ToString("N2");
            ClearCart();
            await SearchCoreAsync();
        });
    }

    public async Task HoldAsync()
    {
        if (!CanHold())
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var held = await _pos.HoldAsync(new PosHoldRequest(
                Guid.NewGuid().ToString(),
                Cart.Select(x => new PosCheckoutLineRequest(
                    x.Product.ProductUnitId,
                    x.Quantity,
                    x.DiscountAmount)).ToList(),
                SaleDiscountAmount));

            StatusMessage = "Sale held as " + held.Number + ".";
            ClearCart();
            await RefreshHeldAsync();
        });
    }

    public async Task ResumeHeldAsync()
    {
        if (SelectedHeldSale is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var detail = await _pos.ResumeHeldSaleAsync(SelectedHeldSale.Id);
            ClearCart();

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
            StatusMessage = detail.Number + " resumed.";
            await RefreshHeldAsync();
            RaiseTotals();
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

    private async Task RefreshHeldAsync()
    {
        HeldSales.Clear();
        if (!CanHoldSales)
        {
            return;
        }

        foreach (var held in await _pos.GetHeldSalesAsync())
        {
            HeldSales.Add(held);
        }
    }

    private void ApplyShift(PosShiftState shift)
    {
        IsShiftOpen = shift.IsOpen;
        ShiftStatus = shift.IsOpen
            ? "Shift open · AFN " + shift.OpeningCash.ToString("N2")
            : "Shift closed";
    }

    private void OnLineTotalsChanged(object? sender, EventArgs e) => RaiseTotals();

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(LineDiscountTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(ChangeAmount));
        OnPropertyChanged(nameof(TotalText));

        if (SelectedPaymentMethod?.IsCash == true)
        {
            PaymentAmount = GrandTotal;
        }

        CheckoutCommand.NotifyCanExecuteChanged();
        HoldCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheckout() =>
        !IsBusy &&
        Cart.Count > 0 &&
        SelectedPaymentMethod is not null &&
        PaymentAmount >= GrandTotal &&
        GrandTotal >= 0m;

    private bool CanHold() => !IsBusy && CanHoldSales && Cart.Count > 0;
    private bool CanResumeHeld() => !IsBusy && SelectedHeldSale is not null;

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

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
    }
}
