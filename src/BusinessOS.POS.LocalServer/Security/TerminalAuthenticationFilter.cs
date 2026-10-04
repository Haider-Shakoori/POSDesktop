using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class TerminalAuthenticationFilter(ILocalTerminalService terminals) : IEndpointFilter
{
    public const string TerminalItemKey = "BusinessOS.POS.Terminal";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var terminalId = http.Request.Headers["X-BusinessOS-Terminal-Id"].FirstOrDefault();
        var secret = http.Request.Headers["X-BusinessOS-Terminal-Secret"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(terminalId) || string.IsNullOrWhiteSpace(secret))
            return Results.Unauthorized();

        var terminal = await terminals.AuthenticateTerminalAsync(terminalId, secret, http.RequestAborted);
        if (terminal is null) return Results.Unauthorized();

        await terminals.TouchAsync(terminal.TerminalId, http.RequestAborted);
        http.Items[TerminalItemKey] = terminal;
        return await next(context);
    }
}
