namespace BusinessOS.POS.Application.Abstractions.Authentication;

public interface IOwnerBootstrapService
{
    Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default);

    Task CreateOwnerAsync(
        string name,
        string username,
        string password,
        string preferredLocale,
        CancellationToken cancellationToken = default);
}
