using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class BusinessDayGuard
{
    public static DateTime LocalBusinessDate(DateTimeOffset value) => value.ToLocalTime().Date;

    public static async Task<BusinessDayEntity> EnsureOpenAsync(
        PosDbContext context,
        DateTime businessDate,
        CancellationToken cancellationToken = default)
    {
        businessDate = businessDate.Date;
        var day = await context.BusinessDays.SingleOrDefaultAsync(
            x => x.BusinessDate == businessDate, cancellationToken);

        if (day is null)
        {
            day = new BusinessDayEntity
            {
                BusinessDate = businessDate,
                Status = "open",
            };
            context.BusinessDays.Add(day);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (day.Status != "open")
            throw new InvalidOperationException(
                "The business day " + businessDate.ToString("yyyy-MM-dd") +
                " is closed. Reopen it before posting new transactions.");

        return day;
    }

    public static Task<BusinessDayEntity> EnsureOpenAsync(
        PosDbContext context,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default) =>
        EnsureOpenAsync(context, LocalBusinessDate(occurredAt), cancellationToken);

    public static async Task<BusinessDayEntity> GetOrCreateAsync(
        PosDbContext context,
        DateTime businessDate,
        CancellationToken cancellationToken = default)
    {
        businessDate = businessDate.Date;
        var day = await context.BusinessDays.SingleOrDefaultAsync(
            x => x.BusinessDate == businessDate, cancellationToken);
        if (day is not null) return day;

        day = new BusinessDayEntity { BusinessDate = businessDate, Status = "open" };
        context.BusinessDays.Add(day);
        await context.SaveChangesAsync(cancellationToken);
        return day;
    }
}
