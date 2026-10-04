using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IReportingService _reports;
    private bool _loaded;

    public DashboardViewModel(IReportingService reports)
    {
        _reports = reports;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public ObservableCollection<DashboardRecentSale> RecentSales { get; } = [];
    public ObservableCollection<DashboardLowStock> LowStockProducts { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty] private DashboardSnapshot? snapshot;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";

    public decimal TodayNetSales => Snapshot?.TodayNetSales ?? 0m;
    public int TodayTransactions => Snapshot?.TodayTransactions ?? 0;
    public decimal Receivables => Snapshot?.Receivables ?? 0m;
    public decimal Payables => Snapshot?.Payables ?? 0m;
    public int LowStockCount => Snapshot?.LowStockCount ?? 0;
    public decimal CurrentShiftExpectedCash => Snapshot?.CurrentShiftExpectedCash ?? 0m;
    public string CurrentShiftTerminal => Snapshot?.CurrentShiftTerminal ?? "No open shift";
    public bool CanViewSales => Snapshot?.CanViewSales ?? false;
    public bool CanViewCustomers => Snapshot?.CanViewCustomers ?? false;
    public bool CanViewSuppliers => Snapshot?.CanViewSuppliers ?? false;
    public bool CanViewInventory => Snapshot?.CanViewInventory ?? false;

    partial void OnSnapshotChanged(DashboardSnapshot? value)
    {
        OnPropertyChanged(nameof(TodayNetSales));
        OnPropertyChanged(nameof(TodayTransactions));
        OnPropertyChanged(nameof(Receivables));
        OnPropertyChanged(nameof(Payables));
        OnPropertyChanged(nameof(LowStockCount));
        OnPropertyChanged(nameof(CurrentShiftExpectedCash));
        OnPropertyChanged(nameof(CurrentShiftTerminal));
        OnPropertyChanged(nameof(CanViewSales));
        OnPropertyChanged(nameof(CanViewCustomers));
        OnPropertyChanged(nameof(CanViewSuppliers));
        OnPropertyChanged(nameof(CanViewInventory));
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Snapshot = await _reports.GetDashboardAsync();

            RecentSales.Clear();
            foreach (var sale in Snapshot.RecentSales)
                RecentSales.Add(sale);

            LowStockProducts.Clear();
            foreach (var product in Snapshot.LowStockProducts)
                LowStockProducts.Add(product);

            StatusMessage = "Dashboard refreshed.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
