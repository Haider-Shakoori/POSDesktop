using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalServer.Runtime;

public sealed class LocalServerRuntimeState
{
    private State? _state;

    public void Initialize(
        LocalServerIdentity identity,
        NetworkConfiguration configuration,
        string certificateSha256) =>
        _state = new State(identity, configuration, certificateSha256);

    public State Require() =>
        _state ?? throw new InvalidOperationException("The LAN server runtime has not been initialized.");

    public sealed record State(
        LocalServerIdentity Identity,
        NetworkConfiguration Configuration,
        string CertificateSha256);
}
