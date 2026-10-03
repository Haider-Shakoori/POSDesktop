using BusinessOS.POS.Application.Abstractions.Sales;

namespace BusinessOS.POS.Application.Abstractions.Customers;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerSummary>> GetCustomersAsync(
        string? search = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default);
    Task<CustomerDetail?> GetCustomerAsync(long customerId, CancellationToken cancellationToken = default);
    Task<CustomerSummary> SaveCustomerAsync(CustomerSaveRequest request, CancellationToken cancellationToken = default);
    Task<CustomerCollectionResult> CollectAsync(CustomerCollectionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerPaymentMethod>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default);
}

public sealed record CustomerSummary(
    long Id,
    string Name,
    string? Phone,
    string? AlternatePhone,
    string? Address,
    decimal CreditLimit,
    decimal OpeningBalance,
    decimal CurrentBalance,
    bool IsActive);

public sealed record CustomerLedgerRow(
    long Id,
    string EntryType,
    decimal Debit,
    decimal Credit,
    decimal BalanceAfter,
    string? ReferenceNumber,
    DateTimeOffset OccurredAt,
    string? Notes);

public sealed record CustomerOutstandingSale(
    long SaleId,
    string SaleNumber,
    DateTimeOffset SoldAt,
    decimal NetTotal,
    decimal PaidAmount,
    decimal BalanceDue,
    string PaymentStatus);

public sealed record CustomerCollectionRow(
    long Id,
    string Number,
    string PaymentMethod,
    decimal Amount,
    decimal TenderedAmount,
    decimal ChangeAmount,
    string? Reference,
    DateTimeOffset CollectedAt,
    string? Notes);

public sealed record CustomerDetail(
    CustomerSummary Customer,
    IReadOnlyList<CustomerLedgerRow> Ledger,
    IReadOnlyList<CustomerOutstandingSale> OutstandingSales,
    IReadOnlyList<CustomerCollectionRow> Collections);

public sealed record CustomerSaveRequest(
    long? Id,
    string Name,
    string? Phone,
    string? AlternatePhone,
    string? Address,
    decimal CreditLimit,
    decimal OpeningBalance,
    bool IsActive = true);

public sealed record CustomerCollectionRequest(
    string IdempotencyKey,
    long CustomerId,
    string PaymentMethodCode,
    decimal Amount,
    decimal? TenderedAmount,
    string? Reference,
    DateTimeOffset? CollectedAt,
    string? Notes);

public sealed record CustomerPaymentMethod(
    string Code,
    string Name,
    bool IsCash);

public sealed record CustomerCollectionAllocationRow(
    long SaleId,
    string SaleNumber,
    decimal Amount);

public sealed record CustomerCollectionResult(
    long Id,
    string Number,
    long CustomerId,
    decimal Amount,
    decimal TenderedAmount,
    decimal ChangeAmount,
    decimal CustomerBalanceAfter,
    DateTimeOffset CollectedAt,
    IReadOnlyList<CustomerCollectionAllocationRow> Allocations);
