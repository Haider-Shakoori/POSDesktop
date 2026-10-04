using System.Net.Http.Headers;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalServer.Runtime;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class UserSessionAuthenticationFilter(
    ILanSessionStore sessions,
    LanRequestContext requestContext)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var terminal = requestContext.Terminal;
        if (terminal is null) return Results.Unauthorized();

        var authorization = http.Request.Headers.Authorization.FirstOrDefault();
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
            return Results.Unauthorized();

        var principal = await sessions.AuthenticateAsync(
            terminal.TerminalId,
            header.Parameter,
            http.RequestAborted);
        if (principal is null) return Results.Unauthorized();

        requestContext.Principal = principal;
        requestContext.AccessToken = header.Parameter;
        return await next(context);
    }
}
