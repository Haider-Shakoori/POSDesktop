using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class WindowsNetworkSecretStore(IApplicationPaths paths) : INetworkSecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BusinessOS.POS.Network.v1");
    private readonly SemaphoreSlim _gate = new(1,1);

    public async Task<TerminalPairingSecret?> LoadTerminalPairingAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken))?.TerminalPairing;

    public Task SaveTerminalPairingAsync(TerminalPairingSecret secret, CancellationToken cancellationToken = default) =>
        MutateAsync(x => x with { TerminalPairing = secret }, cancellationToken);

    public Task ClearTerminalPairingAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(x => x with { TerminalPairing = null }, cancellationToken);

    public async Task<string?> LoadServerCertificatePasswordAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken))?.ServerCertificatePassword;

    public Task SaveServerCertificatePasswordAsync(string password, CancellationToken cancellationToken = default) =>
        MutateAsync(x => x with { ServerCertificatePassword = password }, cancellationToken);

    private async Task<SecretState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.NetworkSecretsPath)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(paths.NetworkSecretsPath, cancellationToken);
        var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        try { return JsonSerializer.Deserialize<SecretState>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private async Task MutateAsync(Func<SecretState,SecretState> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = mutate(await LoadAsync(cancellationToken) ?? new SecretState());
            paths.EnsureCreated();
            var plain = JsonSerializer.SerializeToUtf8Bytes(state);
            var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.LocalMachine);
            try { await File.WriteAllBytesAsync(paths.NetworkSecretsPath, protectedBytes, cancellationToken); }
            finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(protectedBytes); }
        }
        finally { _gate.Release(); }
    }

    private sealed record SecretState(TerminalPairingSecret? TerminalPairing = null, string? ServerCertificatePassword = null);
}
