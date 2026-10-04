using BusinessOS.POS.Application.Abstractions.Authentication;

namespace BusinessOS.POS.LocalClient;

public sealed class LanClientPermissionAuthorizer(IUserSessionService sessions)
    : IPermissionAuthorizer
{
    public bool HasPermission(string permission) =>
        sessions.Current?.HasPermission(permission) == true;

    public void Demand(string permission)
    {
        if (!HasPermission(permission))
            throw new PermissionDeniedException(permission);
    }
}
