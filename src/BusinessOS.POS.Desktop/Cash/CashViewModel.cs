using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Cash;

public sealed partial class CashViewModel : ObservableObject
{
    private readonly ICashManagementService _cash;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public CashViewModel(ICashManagementService cash, IPermissionAuthorizer authorizer)
    {
        _cash = cash;
        _authorizer = authorizer;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenShiftCommand = new AsyncRelayCommand(OpenShiftAsync);
        RecordManualMovementCommand = new AsyncRelayCommand(RecordManualMovementAsync);
        CloseShiftCommand = new AsyncRelayCommand(CloseShiftAsync);
        ReopenShiftCommand = new AsyncRelayCommand(ReopenShiftAsync);
    }

    public ObservableCollection<CashTerminalOption> Terminals { get; } = [];
    public ObservableCollection<CashShiftSummary> Shifts { get; } = [];
    public ObservableCollection<CashMovementRow> Movements { get; } = [];
    public ObservableCollection<ShiftClosureRow> Closures { get; } = [];
    public string[] ManualMovementTypes { get; } = ["cash_deposit", "cash_withdrawal", "drawer_to_safe"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand OpenShiftCommand { get; }
    public IAsyncRelayCommand RecordManualMovementCommand { get; }
    public IAsyncRelayCommand CloseShiftCommand { get; }
    public IAsyncRelayCommand ReopenShiftCommand { get; }

    public bool CanOpen => _authorizer.HasPermission("shifts.open");
    public bool CanManageCash => _authorizer.HasPermission("cash.manage");
    public bool CanClose => _authorizer.HasPermission("shifts.close");
    public bool CanReopen => _authorizer.HasPermission("shifts.reopen");

    [ObservableProperty] private CashTerminalOption? selectedTerminal;
    [ObservableProperty] private CashShiftSummary? selectedShift;
    [ObservableProperty] private CashShiftDetail? currentShift;
    [ObservableProperty] private decimal openingCash;
    [ObservableProperty] private string selectedManualMovementType = "cash_deposit";
    [ObservableProperty] private decimal manualAmount;
    [ObservableProperty] private string manualReason = string.Empty;
    [ObservableProperty] private decimal actualCash;
    [ObservableProperty] private string varianceReason = string.Empty;
    [ObservableProperty] private string closingNotes = string.Empty;
    [ObservableProperty] private string reopenReason = string.Empty;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public decimal CurrentExpectedCash => CurrentShift?.Shift.ExpectedCash ?? 0m;
    public decimal CurrentOpeningCash => CurrentShift?.Shift.OpeningCash ?? 0m;
    public decimal CurrentInflows => CurrentShift?.Movements.Where(x => x.Direction == "inflow").Sum(x => x.Amount) ?? 0m;
    public decimal CurrentOutflows => CurrentShift?.Movements.Where(x => x.Direction == "outflow").Sum(x => x.Amount) ?? 0m;
    public bool HasOpenShift => CurrentShift?.Shift.Status == "open";

    partial void OnSelectedShiftChanged(CashShiftSummary? value) => _ = LoadSelectedShiftAsync();

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        await ExecuteBusyAsync(async () =>
        {
            Terminals.Clear();
            foreach (var terminal in await _cash.GetTerminalsAsync())
                Terminals.Add(terminal);
            SelectedTerminal = Terminals.FirstOrDefault(x => x.IsActive);
            await RefreshCoreAsync();
            _loaded = true;
        });
    }

    private async Task RefreshAsync() =>
        await ExecuteBusyAsync(async () =>
        {
            await RefreshCoreAsync();
            StatusMessage = "Cash drawer refreshed.";
        });

    private async Task RefreshCoreAsync()
    {
        var selectedId = SelectedShift?.Id;
        var current = await _cash.GetCurrentShiftAsync();
        CurrentShift = current;
        RaiseSummary();

        Shifts.Clear();
        foreach (var shift in await _cash.GetShiftsAsync())
            Shifts.Add(shift);

        SelectedShift = selectedId is null
            ? Shifts.FirstOrDefault()
            : Shifts.FirstOrDefault(x => x.Id == selectedId.Value) ?? Shifts.FirstOrDefault();

        if (current is not null)
        {
            var currentRow = Shifts.FirstOrDefault(x => x.Id == current.Shift.Id);
            if (currentRow is not null) SelectedShift = currentRow;
        }
    }

    private async Task LoadSelectedShiftAsync()
    {
        Movements.Clear();
        Closures.Clear();
        if (SelectedShift is null) return;
        try
        {
            var detail = await _cash.GetShiftAsync(SelectedShift.Id);
            if (detail is null) return;
            foreach (var movement in detail.Movements) Movements.Add(movement);
            foreach (var closure in detail.Closures) Closures.Add(closure);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private async Task OpenShiftAsync()
    {
        if (!CanOpen || SelectedTerminal is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var opened = await _cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), SelectedTerminal.Id, OpeningCash));
            CurrentShift = opened;
            OpeningCash = 0m;
            await RefreshCoreAsync();
            StatusMessage = "Shift opened on " + opened.Shift.Terminal + ".";
        });
    }

    private async Task RecordManualMovementAsync()
    {
        if (!CanManageCash || CurrentShift is null) return;
        await ExecuteBusyAsync(async () =>
        {
            await _cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                Guid.NewGuid().ToString(), CurrentShift.Shift.Id,
                SelectedManualMovementType, ManualAmount, ManualReason));
            ManualAmount = 0m;
            ManualReason = string.Empty;
            await RefreshCoreAsync();
            StatusMessage = "Manual cash movement recorded.";
        });
    }

    private async Task CloseShiftAsync()
    {
        if (!CanClose || CurrentShift is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var result = await _cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), CurrentShift.Shift.Id,
                ActualCash, VarianceReason, ClosingNotes));
            ActualCash = 0m;
            VarianceReason = string.Empty;
            ClosingNotes = string.Empty;
            await RefreshCoreAsync();
            StatusMessage = "Shift closed · variance AFN " + result.Variance.ToString("N2") + ".";
        });
    }

    private async Task ReopenShiftAsync()
    {
        if (!CanReopen || SelectedShift is null) return;
        await ExecuteBusyAsync(async () =>
        {
            await _cash.ReopenShiftAsync(SelectedShift.Id, ReopenReason);
            ReopenReason = string.Empty;
            await RefreshCoreAsync();
            StatusMessage = "Shift reopened.";
        });
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(CurrentExpectedCash));
        OnPropertyChanged(nameof(CurrentOpeningCash));
        OnPropertyChanged(nameof(CurrentInflows));
        OnPropertyChanged(nameof(CurrentOutflows));
        OnPropertyChanged(nameof(HasOpenShift));
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
