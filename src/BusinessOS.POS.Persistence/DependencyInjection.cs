using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.POS.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSPosPersistence(this IServiceCollection services)
    {
        services.AddPooledDbContextFactory<PosDbContext>((serviceProvider, options) =>
        {
            var paths = serviceProvider.GetRequiredService<IApplicationPaths>();
            paths.EnsureCreated();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                ForeignKeys = true,
                DefaultTimeout = 10,
            }.ToString();

            options.UseSqlite(connectionString);
        }, poolSize: 8);

        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<LoginAttemptThrottle>();
        services.AddSingleton<ILocalDatabaseInitializer, LocalDatabaseInitializer>();
        services.AddSingleton<IOwnerBootstrapService, OwnerBootstrapService>();
        services.AddSingleton<IUserSessionService, LocalUserSessionService>();
        services.AddSingleton<IPermissionAuthorizer, PermissionAuthorizer>();
        services.AddSingleton<IPosService, LocalPosService>();

        return services;
    }
}
