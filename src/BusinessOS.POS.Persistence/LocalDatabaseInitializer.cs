using BusinessOS.POS.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalDatabaseInitializer(IDbContextFactory<PosDbContext> contextFactory)
    : ILocalDatabaseInitializer
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=10000;", cancellationToken);

            await LocalSchemaUpgrade.ApplyAsync(context, cancellationToken);
            await AccessSeed.ApplyAsync(context, cancellationToken);
            await PosReferenceSeed.ApplyAsync(context, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
