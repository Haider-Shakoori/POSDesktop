using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Sales;

public sealed partial class SalesViewModel : ObservableObject
{
    private readonly ISalesService _sales;
    private readonly ISaleReturnService _returns;
    private readonly IReceiptPrintService _printer;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public SalesViewModel(
        ISalesService sales,
        ISaleReturnService returns,
        IReceiptPrintService printer,
        IPermissionAuthorizer authorizer)
    {
        _sales = sales;
        _returns = returns;
        _printer = printer;
        _authorizer = authorizer;

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PrintReceiptCommand = new AsyncRelayCommand(PrintReceiptAsync, CanPrint);
        PostReturnCommand = new AsyncRelayCommand(PostReturnAsync, CanPostReturn);
        VoidSaleCommand = new AsyncRelayCommand(VoidSaleAsync, CanVoidSale);
    }

    public ObservableCollection<SaleHistoryRow> Sales { get; } = [];
    public ObservableCollection<SalesReturnEditorRow> ReturnLines { get; } = [];
    public ObservableCollection<SaleReturnSummary> ReturnHistory { get; } = [];
    public ObservableCollection<SaleRefundMethod> RefundMethods { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PrintReceiptCommand { get; }
    public IAsyncRelayCommand PostReturnCommand { get; }
    public IAsyncRelayCommand VoidSaleCommand { get; }

    public bool CanViewProfit => _authorizer.HasPermission("reports.profit");
    public bool CanReturn => _authorizer.HasPermission("sales.return");
    public bool CanVoid => _authorizer.HasPermission("sales.void");

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private SaleHistoryRow? selectedSale;
    [ObservableProperty] private SaleDetail? selectedSaleDetail;
    [ObservableProperty] private string returnReason = string.Empty;
    [ObservableProperty] private SaleRefundMethod? selectedRefundMethod;
    [ObservableProperty] private string? refundReference;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";

    public decimal RequestedReturnTotal =>
        decimal.Round(ReturnLines.Sum(x => x.EstimatedReturnAmount), 2, MidpointRounding.AwayFromZero);

    public decimal RequestedReceivableReversal =>
        SelectedSaleDetail is null
            ? 0m
            : Math.Min(RequestedReturnTotal, SelectedSaleDetail.BalanceDue);

    public decimal RequestedRefundDue =>
        decimal.Round(RequestedReturnTotal - RequestedReceivableReversal, 2, MidpointRounding.AwayFromZero);

    public decimal VoidReturnTotal =>
        SelectedSaleDetail is null ? 0m : decimal.Round(SelectedSaleDetail.Items.Sum(x => x.ReturnableAmount), 2);

    public decimal VoidReceivableReversal =>
        SelectedSaleDetail is null ? 0m : Math.Min(VoidReturnTotal, SelectedSaleDetail.BalanceDue);

    public decimal VoidRefundDue =>
        decimal.Round(VoidReturnTotal - VoidReceivableReversal, 2, MidpointRounding.AwayFromZero);

    partial void OnSelectedSaleChanged(SaleHistoryRow? value)
    {
        NotifyCommands();
        _ = LoadSelectedAsync();
    }

    partial void OnReturnReasonChanged(string value) => NotifyCommands();
    partial void OnSelectedRefundMethodChanged(SaleRefundMethod? value) => NotifyCommands();

    public async Task InitializeAsync()
    {
        if (_loaded) return;

        await ExecuteBusyAsync(async () =>
        {
            RefundMethods.Clear();
            if (CanReturn || CanVoid)
            {
                foreach (var method in await _returns.GetRefundMethodsAsync())
                    RefundMethods.Add(method);
                SelectedRefundMethod = RefundMethods.FirstOrDefault(x => x.IsCash) ?? RefundMethods.FirstOrDefault();
            }

            await LoadSalesAsync();
            _loaded = true;
        });
    }

    private async Task SearchAsync() => await ExecuteBusyAsync(LoadSalesAsync);

    private async Task RefreshAsync() =>
        await ExecuteBusyAsync(async () =>
        {
            await LoadSalesAsync();
            StatusMessage = "Sales refreshed.";
        });

    private async Task LoadSalesAsync()
    {
        var selectedId = SelectedSale?.Id;
        var rows = await _sales.GetSalesAsync(SearchText);
        Sales.Clear();
        foreach (var row in rows) Sales.Add(row);
        SelectedSale = Sales.FirstOrDefault(x => x.Id == selectedId) ?? Sales.FirstOrDefault();
        StatusMessage = rows.Count + " sale(s).";
    }

    private async Task LoadSelectedAsync()
    {
        ClearReturnRows();

        if (SelectedSale is null)
        {
            SelectedSaleDetail = null;
            ReturnHistory.Clear();
            NotifyReturnTotals();
            return;
        }

        try
        {
            SelectedSaleDetail = await _sales.GetSaleAsync(SelectedSale.Id);

            if (SelectedSaleDetail is not null)
            {
                foreach (var line in SelectedSaleDetail.Items.Where(x => x.ReturnableQuantity > 0m))
                {
                    var row = new SalesReturnEditorRow { Line = line };
                    row.ReturnChanged += OnReturnLineChanged;
                    ReturnLines.Add(row);
                }
            }

            ReturnHistory.Clear();
            foreach (var row in await _returns.GetReturnsAsync(SelectedSale.Id))
                ReturnHistory.Add(row);

            ReturnReason = string.Empty;
            RefundReference = null;
            NotifyReturnTotals();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }

        NotifyCommands();
    }

    private async Task PrintReceiptAsync()
    {
        if (SelectedSale is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var receipt = await _sales.GetReceiptAsync(SelectedSale.Id)
                ?? throw new InvalidOperationException("Sale receipt data was not found.");
            StatusMessage = _printer.Print(receipt)
                ? "Receipt " + receipt.Sale.Number + " sent to printer."
                : "Receipt printing cancelled.";
        });
    }

    private async Task PostReturnAsync()
    {
        if (SelectedSaleDetail is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var items = ReturnLines
                .Where(x => x.RequestedQuantity > 0m)
                .Select(x => new SaleReturnLineRequest(x.Line.SaleItemId, x.RequestedQuantity))
                .ToList();

            var result = await _returns.ReturnAsync(new SaleReturnRequest(
                Guid.NewGuid().ToString(),
                SelectedSaleDetail.Id,
                ReturnReason,
                items,
                BuildRefunds(RequestedRefundDue)));

            StatusMessage = result.Number + " posted. Return AFN " + result.ReturnTotal.ToString("N2") +
                            " · refund AFN " + result.RefundTotal.ToString("N2") + ".";
            await ReloadAfterReversalAsync(result.SaleId);
        });
    }

    private async Task VoidSaleAsync()
    {
        if (SelectedSaleDetail is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _returns.VoidAsync(new SaleVoidRequest(
                Guid.NewGuid().ToString(),
                SelectedSaleDetail.Id,
                ReturnReason,
                BuildRefunds(VoidRefundDue)));

            StatusMessage = result.Number + " posted. Sale voided.";
            await ReloadAfterReversalAsync(result.SaleId);
        });
    }

    private IReadOnlyList<SaleRefundRequest> BuildRefunds(decimal amount)
    {
        if (amount <= 0m) return [];
        if (SelectedRefundMethod is null)
            throw new InvalidOperationException("Select a refund payment method.");

        return [new SaleRefundRequest(
            SelectedRefundMethod.Code,
            amount,
            RefundReference,
            null)];
    }

    private async Task ReloadAfterReversalAsync(long saleId)
    {
        var rows = await _sales.GetSalesAsync(SearchText);
        Sales.Clear();
        foreach (var row in rows) Sales.Add(row);
        SelectedSale = Sales.FirstOrDefault(x => x.Id == saleId);
        await LoadSelectedAsync();
    }

    private void OnReturnLineChanged(object? sender, EventArgs e)
    {
        NotifyReturnTotals();
        NotifyCommands();
    }

    private void NotifyReturnTotals()
    {
        OnPropertyChanged(nameof(RequestedReturnTotal));
        OnPropertyChanged(nameof(RequestedReceivableReversal));
        OnPropertyChanged(nameof(RequestedRefundDue));
        OnPropertyChanged(nameof(VoidReturnTotal));
        OnPropertyChanged(nameof(VoidReceivableReversal));
        OnPropertyChanged(nameof(VoidRefundDue));
    }

    private void ClearReturnRows()
    {
        foreach (var row in ReturnLines)
            row.ReturnChanged -= OnReturnLineChanged;
        ReturnLines.Clear();
    }

    private bool CanPrint() => !IsBusy && SelectedSale is not null;

    private bool CanPostReturn() =>
        !IsBusy &&
        CanReturn &&
        SelectedSaleDetail is not null &&
        !string.IsNullOrWhiteSpace(ReturnReason) &&
        ReturnLines.Any(x => x.RequestedQuantity > 0m) &&
        (RequestedRefundDue <= 0m || SelectedRefundMethod is not null);

    private bool CanVoidSale() =>
        !IsBusy &&
        CanVoid &&
        SelectedSaleDetail is not null &&
        VoidReturnTotal > 0m &&
        SelectedSaleDetail.Status != "voided" &&
        !string.IsNullOrWhiteSpace(ReturnReason) &&
        (VoidRefundDue <= 0m || SelectedRefundMethod is not null);

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        NotifyCommands();
        try { await action(); }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        PrintReceiptCommand.NotifyCanExecuteChanged();
        PostReturnCommand.NotifyCanExecuteChanged();
        VoidSaleCommand.NotifyCanExecuteChanged();
    }
}
