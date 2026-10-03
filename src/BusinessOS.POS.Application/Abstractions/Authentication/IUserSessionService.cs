using BusinessOS.POS.Domain.Authentication;

namespace BusinessOS.POS.Application.Abstractions.Authentication;

public interface IUserSessionService
{
    UserSessionSnapshot? Current { get; }

    Task<UserSessionSnapshot> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task UpdatePreferredLocaleAsync(
        string locale,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
