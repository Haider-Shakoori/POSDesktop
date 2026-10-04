using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalServer.Runtime;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class TerminalAuthenticationFilter(
    ILocalTerminalService terminals,
    LanRequestContext requestContext)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var terminalId = http.Request.Headers["X-BusinessOS-Terminal-Id"].FirstOrDefault();
        var secret = http.Request.Headers["X-BusinessOS-Terminal-Secret"].FirstOrDefault();

        var terminal = await terminals.AuthenticateTerminalAsync(
            terminalId ?? string.Empty,
            secret ?? string.Empty,
            http.RequestAborted);

        if (terminal is null)
            return Results.Unauthorized();

        requestContext.Terminal = terminal;
        return await next(context);
    }
}
