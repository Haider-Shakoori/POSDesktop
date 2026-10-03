using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Sales;

public sealed partial class SalesViewModel : ObservableObject
{
    private readonly ISalesService _sales;
    private readonly IReceiptPrintService _printer;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public SalesViewModel(
        ISalesService sales,
        IReceiptPrintService printer,
        IPermissionAuthorizer authorizer)
    {
        _sales = sales;
        _printer = printer;
        _authorizer = authorizer;
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PrintReceiptCommand = new AsyncRelayCommand(PrintReceiptAsync, CanPrint);
    }

    public ObservableCollection<SaleHistoryRow> Sales { get; } = [];
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PrintReceiptCommand { get; }

    public bool CanViewProfit => _authorizer.HasPermission("reports.profit");

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private SaleHistoryRow? selectedSale;
    [ObservableProperty] private SaleDetail? selectedSaleDetail;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";

    partial void OnSelectedSaleChanged(SaleHistoryRow? value)
    {
        PrintReceiptCommand.NotifyCanExecuteChanged();
        _ = LoadSelectedAsync();
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        await ExecuteBusyAsync(async () =>
        {
            await LoadSalesAsync();
            _loaded = true;
        });
    }

    private async Task SearchAsync() =>
        await ExecuteBusyAsync(LoadSalesAsync);

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
        if (SelectedSale is null)
        {
            SelectedSaleDetail = null;
            return;
        }

        try
        {
            SelectedSaleDetail = await _sales.GetSaleAsync(SelectedSale.Id);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
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

    private bool CanPrint() => !IsBusy && SelectedSale is not null;

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        PrintReceiptCommand.NotifyCanExecuteChanged();
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
            PrintReceiptCommand.NotifyCanExecuteChanged();
        }
    }
}
