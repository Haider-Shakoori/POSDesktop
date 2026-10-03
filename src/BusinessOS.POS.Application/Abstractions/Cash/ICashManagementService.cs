namespace BusinessOS.POS.Application.Abstractions.Cash;

public interface ICashManagementService
{
    Task<IReadOnlyList<CashTerminalOption>> GetTerminalsAsync(CancellationToken cancellationToken = default);
    Task<CashShiftDetail?> GetCurrentShiftAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashShiftSummary>> GetShiftsAsync(int take = 100, CancellationToken cancellationToken = default);
    Task<CashShiftDetail?> GetShiftAsync(long shiftId, CancellationToken cancellationToken = default);
    Task<CashShiftDetail> OpenShiftAsync(ShiftOpenRequest request, CancellationToken cancellationToken = default);
    Task<CashMovementRow> RecordManualMovementAsync(ManualCashMovementRequest request, CancellationToken cancellationToken = default);
    Task<ShiftClosureResult> CloseShiftAsync(ShiftCloseRequest request, CancellationToken cancellationToken = default);
    Task<CashShiftDetail> ReopenShiftAsync(long shiftId, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashPaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperatingEntryRow>> GetOperatingEntriesAsync(string? entryType = null, int take = 300, CancellationToken cancellationToken = default);
    Task<OperatingEntryRow> RecordOperatingEntryAsync(OperatingEntryRequest request, CancellationToken cancellationToken = default);
}

public sealed record CashTerminalOption(long Id, string Code, string Name, bool IsActive);

public sealed record CashShiftSummary(
    long Id, long TerminalId, string Terminal, long UserId, string UserName,
    DateTime BusinessDate, string Status, decimal OpeningCash, decimal ExpectedCash,
    decimal? ActualCash, decimal? Variance, bool? VarianceWithinTolerance,
    DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt);

public sealed record CashMovementRow(
    long Id, long ShiftId, string MovementType, string Direction, decimal Amount,
    decimal ExpectedCashAfter, string? ReferenceNumber, string? Reason, DateTimeOffset OccurredAt);

public sealed record ShiftClosureRow(
    long Id, int Version, decimal ExpectedCash, decimal ActualCash, decimal Variance,
    decimal Tolerance, bool WithinTolerance, string? VarianceReason, string? ClosingNotes,
    DateTimeOffset ClosedAt);

public sealed record CashShiftDetail(
    CashShiftSummary Shift,
    IReadOnlyList<CashMovementRow> Movements,
    IReadOnlyList<ShiftClosureRow> Closures);

public sealed record ShiftOpenRequest(string IdempotencyKey, long TerminalId, decimal OpeningCash);
public sealed record ManualCashMovementRequest(
    string IdempotencyKey, long ShiftId, string MovementType, decimal Amount, string Reason);
public sealed record ShiftCloseRequest(
    string IdempotencyKey, long ShiftId, decimal ActualCash, string? VarianceReason, string? ClosingNotes);

public sealed record ShiftClosureResult(
    long ClosureId, long ShiftId, int Version, decimal ExpectedCash, decimal ActualCash,
    decimal Variance, decimal Tolerance, bool WithinTolerance, DateTimeOffset ClosedAt);

public sealed record ExpenseCategoryOption(
    long Id, string Code, string EntryType, string Name, bool IsActive, int SortOrder);
public sealed record CashPaymentMethodOption(long Id, string Code, string Name, bool IsCash);
public sealed record OperatingEntryRequest(
    string IdempotencyKey, string EntryType, long ExpenseCategoryId, long PaymentMethodId,
    decimal Amount, string? Reference, string? Description, DateTimeOffset? OccurredAt);
public sealed record OperatingEntryRow(
    long Id, string Number, string EntryType, string Category, string PaymentMethod,
    bool IsCash, decimal Amount, string? Reference, string? Description, DateTimeOffset OccurredAt);
