using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalServer.Runtime;

public sealed class LanServerRuntimeState
{
    private LocalServerIdentity? _identity;
    private NetworkConfiguration? _configuration;
    private string? _certificateSha256;

    public void Initialize(
        LocalServerIdentity identity,
        NetworkConfiguration configuration,
        string certificateSha256)
    {
        _identity = identity;
        _configuration = configuration;
        _certificateSha256 = certificateSha256;
    }

    public (LocalServerIdentity Identity, NetworkConfiguration Configuration, string CertificateSha256) Require()
    {
        if (_identity is null || _configuration is null || _certificateSha256 is null)
            throw new InvalidOperationException("The POS LAN server runtime is not initialized.");
        return (_identity, _configuration, _certificateSha256);
    }
}
