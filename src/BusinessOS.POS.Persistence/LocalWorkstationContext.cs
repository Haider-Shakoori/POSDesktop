using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.Persistence;

public sealed class LocalWorkstationContext : IWorkstationContext
{
    public long? CashTerminalId => null;
    public string? LanTerminalId => null;
    public bool IsLanRequest => false;
}
