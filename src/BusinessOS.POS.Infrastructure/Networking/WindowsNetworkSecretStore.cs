using System.Security.Cryptography;
using System.Text;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class WindowsNetworkSecretStore(IApplicationPaths paths) : INetworkSecretStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = SecretPath(key);
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var clear = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        paths.EnsureCreated();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var clear = Encoding.UTF8.GetBytes(value);
            try
            {
                var protectedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
                try { await File.WriteAllBytesAsync(SecretPath(key), protectedBytes, cancellationToken); }
                finally { CryptographicOperations.ZeroMemory(protectedBytes); }
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        finally { _gate.Release(); }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = SecretPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string SecretPath(string key)
    {
        var safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(paths.RootPath, "network-secret-" + safe + ".bin");
    }
}
