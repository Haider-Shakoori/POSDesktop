namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface IWorkstationContext
{
    long? CashTerminalId { get; }
    string? LanTerminalId { get; }
    bool IsLanRequest { get; }
}
