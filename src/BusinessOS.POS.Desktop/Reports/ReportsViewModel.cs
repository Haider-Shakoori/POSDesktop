using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using BusinessOS.POS.Application.Abstractions.Reporting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BusinessOS.POS.Desktop.Reports;

public sealed record ReportFilterOption(long? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IReportingService _reports;
    private bool _loaded;

    public ReportsViewModel(IReportingService reports)
    {
        _reports = reports;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);
    }

    public ObservableCollection<ReportFilterOption> Customers { get; } = [];
    public ObservableCollection<ReportFilterOption> Suppliers { get; } = [];
    public ObservableCollection<ReportFilterOption> Products { get; } = [];
    public ObservableCollection<ReportFilterOption> Categories { get; } = [];
    public ObservableCollection<SalesTrendRow> SalesTrend { get; } = [];
    public ObservableCollection<ProductPerformanceRow> TopProducts { get; } = [];
    public ObservableCollection<ProductPerformanceRow> SlowProducts { get; } = [];
    public ObservableCollection<CategoryProfitRow> CategoryProfit { get; } = [];
    public ObservableCollection<CustomerActivityRow> CustomerActivity { get; } = [];
    public ObservableCollection<SupplierActivityRow> SupplierActivity { get; } = [];
    public ObservableCollection<ExpiryWatchRow> Expiring { get; } = [];
    public ObservableCollection<ClosingHistoryRow> ClosingHistory { get; } = [];
    public ObservableCollection<TimePerformanceRow> PeakHours { get; } = [];
    public ObservableCollection<TimePerformanceRow> Weekdays { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ExportCsvCommand { get; }

    [ObservableProperty] private DateTime fromDate = DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private ReportFilterOption? selectedCustomer;
    [ObservableProperty] private ReportFilterOption? selectedSupplier;
    [ObservableProperty] private ReportFilterOption? selectedProduct;
    [ObservableProperty] private ReportFilterOption? selectedCategory;
    [ObservableProperty] private ReportSummary? summary;
    [ObservableProperty] private InventoryHealth? inventory;
    [ObservableProperty] private bool canViewProfit;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public decimal NetSales => Summary?.NetSales ?? 0m;
    public decimal AverageOrderValue => Summary?.AverageOrderValue ?? 0m;
    public decimal Receivables => Summary?.Receivables ?? 0m;
    public decimal Payables => Summary?.Payables ?? 0m;
    public decimal Purchases => Summary?.Purchases ?? 0m;
    public decimal Returns => Summary?.Returns ?? 0m;
    public decimal GrossProfit => Summary?.GrossProfit ?? 0m;
    public decimal NetProfit => Summary?.NetProfit ?? 0m;
    public decimal InventoryValue => Summary?.InventoryValue ?? 0m;
    public int SalesCount => Summary?.SalesCount ?? 0;
    public int LowStock => Inventory?.LowStock ?? 0;
    public int OutOfStock => Inventory?.OutOfStock ?? 0;
    public int ExpiredBatches => Inventory?.ExpiredBatches ?? 0;
    public int ExpiringBatches => Inventory?.ExpiringBatches ?? 0;

    partial void OnSummaryChanged(ReportSummary? value) => RaiseSummary();
    partial void OnInventoryChanged(InventoryHealth? value) => RaiseSummary();

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadLookupsAsync();
        await RefreshAsync();
    }

    private async Task LoadLookupsAsync()
    {
        var lookups = await _reports.GetLookupsAsync();
        Fill(Customers, lookups.Customers, "All customers");
        Fill(Suppliers, lookups.Suppliers, "All suppliers");
        Fill(Products, lookups.Products, "All products");
        Fill(Categories, lookups.Categories, "All categories");
        SelectedCustomer = Customers.FirstOrDefault();
        SelectedSupplier = Suppliers.FirstOrDefault();
        SelectedProduct = Products.FirstOrDefault();
        SelectedCategory = Categories.FirstOrDefault();
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var snapshot = await _reports.BuildAsync(CurrentFilters());
            Summary = snapshot.Summary;
            Inventory = snapshot.Inventory;
            CanViewProfit = snapshot.CanViewProfit;
            Replace(SalesTrend, snapshot.SalesTrend);
            Replace(TopProducts, snapshot.TopProducts);
            Replace(SlowProducts, snapshot.SlowProducts);
            Replace(CategoryProfit, snapshot.CategoryProfit);
            Replace(CustomerActivity, snapshot.Customers);
            Replace(SupplierActivity, snapshot.Suppliers);
            Replace(Expiring, snapshot.Expiring);
            Replace(ClosingHistory, snapshot.ClosingHistory);
            Replace(PeakHours, snapshot.PeakHours);
            Replace(Weekdays, snapshot.Weekdays);
            StatusMessage = "Reports refreshed for " +
                            snapshot.Filters.From.ToString("yyyy-MM-dd") + " to " +
                            snapshot.Filters.To.ToString("yyyy-MM-dd") + ".";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ExportCsvAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var rows = await _reports.GetSalesExportAsync(CurrentFilters());
            var dialog = new SaveFileDialog
            {
                Title = "Export sales report",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "businessos-pos-sales-" + FromDate.ToString("yyyyMMdd") +
                           "-" + ToDate.ToString("yyyyMMdd") + ".csv",
                AddExtension = true,
                DefaultExt = ".csv",
            };
            if (dialog.ShowDialog() != true) return;

            var csv = new StringBuilder();
            var headers = new List<string>
            {
                "Number","Sold At","Customer","Subtotal","Line Discount","Sale Discount",
                "Net Total","Returned Total"
            };
            if (CanViewProfit)
            {
                headers.Add("COGS");
                headers.Add("Gross Profit");
            }
            headers.Add("Paid Amount");
            headers.Add("Balance Due");
            csv.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

            foreach (var row in rows)
            {
                var values = new List<string>
                {
                    row.Number,
                    row.SoldAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    row.Customer,
                    MoneyText(row.Subtotal),
                    MoneyText(row.LineDiscountTotal),
                    MoneyText(row.SaleDiscountAmount),
                    MoneyText(row.NetTotal),
                    MoneyText(row.ReturnedTotal),
                };
                if (CanViewProfit)
                {
                    values.Add(MoneyText(row.CogsTotal ?? 0m));
                    values.Add(MoneyText(row.GrossProfit ?? 0m));
                }
                values.Add(MoneyText(row.PaidAmount));
                values.Add(MoneyText(row.BalanceDue));
                csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
            }

            await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            StatusMessage = rows.Count + " sales rows exported.";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private ReportFilters CurrentFilters() => new(
        FromDate.Date, ToDate.Date,
        SelectedCustomer?.Id,
        SelectedSupplier?.Id,
        SelectedProduct?.Id,
        SelectedCategory?.Id);

    private static void Fill(
        ObservableCollection<ReportFilterOption> target,
        IReadOnlyList<ReportLookupItem> source,
        string allLabel)
    {
        target.Clear();
        target.Add(new ReportFilterOption(null, allLabel));
        foreach (var row in source)
            target.Add(new ReportFilterOption(row.Id,
                string.IsNullOrWhiteSpace(row.Code) ? row.Name : row.Code + " · " + row.Name));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows) target.Add(row);
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(NetSales));
        OnPropertyChanged(nameof(AverageOrderValue));
        OnPropertyChanged(nameof(Receivables));
        OnPropertyChanged(nameof(Payables));
        OnPropertyChanged(nameof(Purchases));
        OnPropertyChanged(nameof(Returns));
        OnPropertyChanged(nameof(GrossProfit));
        OnPropertyChanged(nameof(NetProfit));
        OnPropertyChanged(nameof(InventoryValue));
        OnPropertyChanged(nameof(SalesCount));
        OnPropertyChanged(nameof(LowStock));
        OnPropertyChanged(nameof(OutOfStock));
        OnPropertyChanged(nameof(ExpiredBatches));
        OnPropertyChanged(nameof(ExpiringBatches));
    }

    private static string MoneyText(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
