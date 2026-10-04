using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Closing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Closing;

public sealed partial class DailyClosingViewModel : ObservableObject
{
    private readonly IBusinessDayClosingService _closing;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public DailyClosingViewModel(
        IBusinessDayClosingService closing,
        IPermissionAuthorizer authorizer)
    {
        _closing = closing;
        _authorizer = authorizer;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CloseDayCommand = new AsyncRelayCommand(CloseDayAsync);
        ReopenDayCommand = new AsyncRelayCommand(ReopenDayAsync);
    }

    public ObservableCollection<BusinessDayShiftRow> Shifts { get; } = [];
    public ObservableCollection<CashBreakdownRow> CashBreakdown { get; } = [];
    public ObservableCollection<BusinessDayClosureRow> Closures { get; } = [];
    public ObservableCollection<BusinessDayRow> BusinessDays { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CloseDayCommand { get; }
    public IAsyncRelayCommand ReopenDayCommand { get; }

    public bool CanCloseDay => _authorizer.HasPermission("business_days.close");
    public bool CanReopenDay => _authorizer.HasPermission("business_days.reopen");

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private BusinessDaySummary? summary;
    [ObservableProperty] private string? closingNotes;
    [ObservableProperty] private string reopenReason = string.Empty;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public string DayStatus => Summary?.Status ?? "open";
    public int ShiftCount => Summary?.ShiftCount ?? 0;
    public int OpenShiftCount => Summary?.OpenShiftCount ?? 0;
    public int SalesCount => Summary?.SalesCount ?? 0;
    public decimal NetSales => Summary?.NetSalesTotal ?? 0m;
    public decimal GrossProfit => Summary?.GrossProfitTotal ?? 0m;
    public decimal NetProfit => Summary?.NetProfitTotal ?? 0m;
    public decimal Purchases => Summary?.PurchasesTotal ?? 0m;
    public decimal OperatingExpenses => Summary?.OperatingExpensesTotal ?? 0m;
    public decimal ExpectedCash => Summary?.ExpectedCashTotal ?? 0m;
    public decimal ActualCash => Summary?.ActualCashTotal ?? 0m;
    public decimal Variance => Summary?.VarianceTotal ?? 0m;
    public decimal LedgerExpectedCash => Summary?.LedgerExpectedCashTotal ?? 0m;
    public bool CashLedgerMatches => ExpectedCash == LedgerExpectedCash;
    public bool CanCloseCurrentDay =>
        CanCloseDay &&
        Summary?.Status == "open" &&
        Summary.OpenShiftCount == 0 &&
        CashLedgerMatches;
    public bool CanReopenCurrentDay => CanReopenDay && Summary?.Status == "closed";

    partial void OnSelectedDateChanged(DateTime value)
    {
        if (_loaded) _ = RefreshAsync();
    }

    partial void OnSummaryChanged(BusinessDaySummary? value) => RaiseSummary();

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            Summary = await _closing.GetSummaryAsync(SelectedDate);

            Shifts.Clear();
            foreach (var shift in Summary.Shifts) Shifts.Add(shift);

            CashBreakdown.Clear();
            foreach (var row in Summary.CashBreakdown) CashBreakdown.Add(row);

            Closures.Clear();
            foreach (var closure in await _closing.GetClosuresAsync(SelectedDate))
                Closures.Add(closure);

            BusinessDays.Clear();
            foreach (var day in await _closing.GetBusinessDaysAsync())
                BusinessDays.Add(day);

            StatusMessage = "Daily reconciliation refreshed.";
        });
    }

    private async Task CloseDayAsync()
    {
        if (!CanCloseDay) return;
        await ExecuteBusyAsync(async () =>
        {
            var closure = await _closing.CloseAsync(new BusinessDayCloseRequest(
                Guid.NewGuid().ToString(), SelectedDate, ClosingNotes));
            ClosingNotes = null;
            await RefreshCoreAsync();
            StatusMessage = closure.Number + " closed and snapshotted.";
        });
    }

    private async Task ReopenDayAsync()
    {
        if (!CanReopenDay) return;
        var reason = ReopenReason.Trim();
        if (reason.Length == 0)
        {
            StatusMessage = "A reason is required to reopen a business day.";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            await _closing.ReopenAsync(SelectedDate, reason);
            ReopenReason = string.Empty;
            await RefreshCoreAsync();
            StatusMessage = "Business day reopened. Prior closure snapshots remain unchanged.";
        });
    }

    private async Task RefreshCoreAsync()
    {
        Summary = await _closing.GetSummaryAsync(SelectedDate);

        Shifts.Clear();
        foreach (var shift in Summary.Shifts) Shifts.Add(shift);

        CashBreakdown.Clear();
        foreach (var row in Summary.CashBreakdown) CashBreakdown.Add(row);

        Closures.Clear();
        foreach (var closure in await _closing.GetClosuresAsync(SelectedDate))
            Closures.Add(closure);

        BusinessDays.Clear();
        foreach (var day in await _closing.GetBusinessDaysAsync())
            BusinessDays.Add(day);
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(DayStatus));
        OnPropertyChanged(nameof(ShiftCount));
        OnPropertyChanged(nameof(OpenShiftCount));
        OnPropertyChanged(nameof(SalesCount));
        OnPropertyChanged(nameof(NetSales));
        OnPropertyChanged(nameof(GrossProfit));
        OnPropertyChanged(nameof(NetProfit));
        OnPropertyChanged(nameof(Purchases));
        OnPropertyChanged(nameof(OperatingExpenses));
        OnPropertyChanged(nameof(ExpectedCash));
        OnPropertyChanged(nameof(ActualCash));
        OnPropertyChanged(nameof(Variance));
        OnPropertyChanged(nameof(LedgerExpectedCash));
        OnPropertyChanged(nameof(CashLedgerMatches));
        OnPropertyChanged(nameof(CanCloseCurrentDay));
        OnPropertyChanged(nameof(CanReopenCurrentDay));
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
