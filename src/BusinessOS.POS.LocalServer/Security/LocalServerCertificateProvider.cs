using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class LocalServerCertificateProvider(
    IApplicationPaths paths,
    INetworkSecretStore secretStore)
{
    private string CertificatePath => Path.Combine(paths.RootPath, "secrets", "lan-server.pfx");

    public async Task<X509Certificate2> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        Directory.CreateDirectory(Path.GetDirectoryName(CertificatePath)!);

        var password = await secretStore.LoadServerCertificatePasswordAsync(cancellationToken);
        if (File.Exists(CertificatePath) && !string.IsNullOrWhiteSpace(password))
        {
            var existing = new X509Certificate2(
                await File.ReadAllBytesAsync(CertificatePath, cancellationToken),
                password,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
            if (existing.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30))
                return existing;
            existing.Dispose();
        }

        password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await secretStore.SaveServerCertificatePasswordAsync(password, cancellationToken);

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=BusinessOS POS Local Server",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);

        foreach (var address in Dns.GetHostAddresses(Environment.MachineName)
                     .Where(x => x.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                              or System.Net.Sockets.AddressFamily.InterNetworkV6))
        {
            san.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(san.Build());

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        var pfx = created.Export(X509ContentType.Pfx, password);
        await File.WriteAllBytesAsync(CertificatePath, pfx, cancellationToken);
        return new X509Certificate2(
            pfx,
            password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }

    public static string Sha256Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
}
