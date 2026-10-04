using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.LocalServer.Runtime;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class RequestPermissionAuthorizer(LanRequestContext requestContext)
    : IPermissionAuthorizer
{
    public bool HasPermission(string permission)
    {
        var principal = requestContext.Principal;
        if (principal is null || !principal.User.HasPermission(permission))
            return false;

        return principal.TerminalPermissions.Count == 0 ||
               principal.TerminalPermissions.Contains(permission);
    }

    public void Demand(string permission)
    {
        if (!HasPermission(permission))
            throw new PermissionDeniedException(permission);
    }
}
