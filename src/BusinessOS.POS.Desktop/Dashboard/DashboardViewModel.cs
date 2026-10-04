using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboard;
    private bool _loaded;

    public DashboardViewModel(IDashboardService dashboard)
    {
        _dashboard = dashboard;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public ObservableCollection<DashboardLowStockRow> LowStockProducts { get; } = [];
    public ObservableCollection<DashboardRecentSaleRow> RecentSales { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty] private DashboardSnapshot? snapshot;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public decimal TodayNetSales => Snapshot?.TodayNetSales ?? 0m;
    public int TodayTransactions => Snapshot?.TodayTransactions ?? 0;
    public decimal Receivables => Snapshot?.Receivables ?? 0m;
    public decimal Payables => Snapshot?.Payables ?? 0m;
    public int LowStockCount => Snapshot?.LowStockCount ?? 0;
    public decimal CashOnHand => Snapshot?.OpenShift?.ExpectedCash ?? 0m;
    public string ShiftStatus => Snapshot?.OpenShift is null
        ? "No open cashier shift"
        : Snapshot.OpenShift.Terminal + " · opened " + Snapshot.OpenShift.OpenedAt.ToLocalTime().ToString("g");

    partial void OnSnapshotChanged(DashboardSnapshot? value)
    {
        OnPropertyChanged(nameof(TodayNetSales));
        OnPropertyChanged(nameof(TodayTransactions));
        OnPropertyChanged(nameof(Receivables));
        OnPropertyChanged(nameof(Payables));
        OnPropertyChanged(nameof(LowStockCount));
        OnPropertyChanged(nameof(CashOnHand));
        OnPropertyChanged(nameof(ShiftStatus));
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
            Snapshot = await _dashboard.GetAsync();
            LowStockProducts.Clear();
            foreach (var row in Snapshot.LowStockProducts) LowStockProducts.Add(row);
            RecentSales.Clear();
            foreach (var row in Snapshot.RecentSales) RecentSales.Add(row);
            StatusMessage = "Live dashboard refreshed.";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
