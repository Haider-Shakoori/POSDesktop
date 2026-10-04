using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class WindowsNetworkSecretStore(IApplicationPaths paths) : INetworkSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string SecretDirectory => Path.Combine(paths.RootPath, "secrets");
    private string PairingPath => Path.Combine(SecretDirectory, "lan-terminal.bin");
    private string CertificatePasswordPath => Path.Combine(SecretDirectory, "lan-certificate.bin");

    public async Task<TerminalPairingSecret?> LoadTerminalPairingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PairingPath)) return null;
        var json = await ReadProtectedAsync(PairingPath, cancellationToken);
        return JsonSerializer.Deserialize<TerminalPairingSecret>(json, JsonOptions);
    }

    public Task SaveTerminalPairingAsync(
        TerminalPairingSecret secret,
        CancellationToken cancellationToken = default) =>
        WriteProtectedAsync(
            PairingPath,
            JsonSerializer.Serialize(secret, JsonOptions),
            cancellationToken);

    public Task ClearTerminalPairingAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(PairingPath)) File.Delete(PairingPath);
        return Task.CompletedTask;
    }

    public async Task<string?> LoadServerCertificatePasswordAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CertificatePasswordPath)) return null;
        return await ReadProtectedAsync(CertificatePasswordPath, cancellationToken);
    }

    public Task SaveServerCertificatePasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return WriteProtectedAsync(CertificatePasswordPath, password, cancellationToken);
    }

    private async Task<string> ReadProtectedAsync(string path, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LAN secret storage requires Windows DPAPI.");

        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        var clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private async Task WriteProtectedAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LAN secret storage requires Windows DPAPI.");

        paths.EnsureCreated();
        Directory.CreateDirectory(SecretDirectory);
        var clear = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.LocalMachine);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }
}
