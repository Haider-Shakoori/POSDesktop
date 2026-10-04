using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalServer.Runtime;

public sealed class RequestWorkstationContext(LanRequestContext requestContext) : IWorkstationContext
{
    public long? CashTerminalId => requestContext.Terminal?.CashTerminalId;
    public string? LanTerminalId => requestContext.Terminal?.TerminalId;
    public bool IsLanRequest => requestContext.Terminal is not null;
}
