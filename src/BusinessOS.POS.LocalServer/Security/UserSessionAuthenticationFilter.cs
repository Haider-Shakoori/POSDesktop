using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalServer.Authentication;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class UserSessionAuthenticationFilter(LanAuthenticationService authentication) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var terminal = http.Items[TerminalAuthenticationFilter.TerminalItemKey] as RegisteredTerminal;
        if (terminal is null) return Results.Unauthorized();

        var header = http.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Results.Unauthorized();

        var token = header["Bearer ".Length..].Trim();
        var user = await authentication.AuthenticateSessionAsync(terminal.TerminalId, token, http.RequestAborted);
        if (user is null) return Results.Unauthorized();

        http.Items[RequestUserSessionService.UserItemKey] = user;
        return await next(context);
    }
}
