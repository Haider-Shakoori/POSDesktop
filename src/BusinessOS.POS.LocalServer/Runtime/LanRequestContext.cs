using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalServer.Runtime;

public sealed class LanRequestContext
{
    public RegisteredLanTerminal? Terminal { get; set; }
    public LanSessionPrincipal? Principal { get; set; }
    public string? AccessToken { get; set; }
}
