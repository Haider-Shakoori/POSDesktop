namespace BusinessOS.POS.Application.Abstractions.Persistence;

public interface ILocalDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
