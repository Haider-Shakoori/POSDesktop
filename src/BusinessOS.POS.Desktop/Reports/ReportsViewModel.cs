using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Reports;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IReportingService _reports;
    private bool _loaded;

    public ReportsViewModel(IReportingService reports)
    {
        _reports = reports;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    public ObservableCollection<ReportLookupOption> Customers { get; } = [];
    public ObservableCollection<ReportLookupOption> Suppliers { get; } = [];
    public ObservableCollection<ReportLookupOption> Products { get; } = [];
    public ObservableCollection<ReportLookupOption> Categories { get; } = [];
    public ObservableCollection<SalesTrendRow> SalesTrend { get; } = [];
    public ObservableCollection<ProductPerformanceRow> TopProducts { get; } = [];
    public ObservableCollection<ProductPerformanceRow> SlowProducts { get; } = [];
    public ObservableCollection<CategoryProfitRow> CategoryProfit { get; } = [];
    public ObservableCollection<BalanceReportRow> CustomerBalances { get; } = [];
    public ObservableCollection<BalanceReportRow> SupplierBalances { get; } = [];
    public ObservableCollection<InventoryReportRow> Inventory { get; } = [];
    public ObservableCollection<ExpiryReportRow> Expiring { get; } = [];
    public ObservableCollection<ClosingHistoryRow> ClosingHistory { get; } = [];
    public ObservableCollection<PeakHourRow> PeakHours { get; } = [];
    public ObservableCollection<WeekdayPerformanceRow> Weekdays { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ClearFiltersCommand { get; }

    [ObservableProperty] private DateTime fromDate = DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private ReportLookupOption? selectedCustomer;
    [ObservableProperty] private ReportLookupOption? selectedSupplier;
    [ObservableProperty] private ReportLookupOption? selectedProduct;
    [ObservableProperty] private ReportLookupOption? selectedCategory;
    [ObservableProperty] private ReportResult? report;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";

    public int SalesCount => Report?.Summary.SalesCount ?? 0;
    public decimal NetSales => Report?.Summary.NetSales ?? 0m;
    public decimal Aov => Report?.Summary.Aov ?? 0m;
    public decimal GrossProfit => Report?.Summary.GrossProfit ?? 0m;
    public decimal NetProfit => Report?.Summary.NetProfit ?? 0m;
    public decimal InventoryValue => Report?.Summary.InventoryValue ?? 0m;
    public decimal Receivables => Report?.Summary.Receivables ?? 0m;
    public decimal Payables => Report?.Summary.Payables ?? 0m;
    public bool CanViewProfit => Report?.CanViewProfit ?? false;

    partial void OnReportChanged(ReportResult? value)
    {
        OnPropertyChanged(nameof(SalesCount));
        OnPropertyChanged(nameof(NetSales));
        OnPropertyChanged(nameof(Aov));
        OnPropertyChanged(nameof(GrossProfit));
        OnPropertyChanged(nameof(NetProfit));
        OnPropertyChanged(nameof(InventoryValue));
        OnPropertyChanged(nameof(Receivables));
        OnPropertyChanged(nameof(Payables));
        OnPropertyChanged(nameof(CanViewProfit));
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var lookup = await _reports.GetLookupsAsync();
            Fill(Customers, lookup.Customers);
            Fill(Suppliers, lookup.Suppliers);
            Fill(Products, lookup.Products);
            Fill(Categories, lookup.Categories);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _reports.BuildAsync(new ReportFilters(
                FromDate, ToDate, SelectedCustomer?.Id, SelectedSupplier?.Id,
                SelectedProduct?.Id, SelectedCategory?.Id));
            Report = result;

            Fill(SalesTrend, result.SalesTrend);
            Fill(TopProducts, result.TopProducts);
            Fill(SlowProducts, result.SlowProducts);
            Fill(CategoryProfit, result.CategoryProfit);
            Fill(CustomerBalances, result.Customers);
            Fill(SupplierBalances, result.Suppliers);
            Fill(Inventory, result.Inventory);
            Fill(Expiring, result.Expiring);
            Fill(ClosingHistory, result.ClosingHistory);
            Fill(PeakHours, result.PeakHours);
            Fill(Weekdays, result.Weekdays);
            StatusMessage = "Reports refreshed.";
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

    private void ClearFilters()
    {
        FromDate = DateTime.Today.AddDays(-29);
        ToDate = DateTime.Today;
        SelectedCustomer = null;
        SelectedSupplier = null;
        SelectedProduct = null;
        SelectedCategory = null;
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}
