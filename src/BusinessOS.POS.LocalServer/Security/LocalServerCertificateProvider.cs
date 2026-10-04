using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class LocalServerCertificateProvider(INetworkSecretStore secrets)
{
    private const string SecretKey = "pos-local-server-pfx";

    public async Task<X509Certificate2> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var saved = await secrets.GetAsync(SecretKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            var bytes = Convert.FromBase64String(saved);
            try { return X509CertificateLoader.LoadPkcs12(bytes, null); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=BusinessOS POS Local Server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var pfx = created.Export(X509ContentType.Pkcs12);
        await secrets.SetAsync(SecretKey, Convert.ToBase64String(pfx), cancellationToken);
        var result = X509CertificateLoader.LoadPkcs12(pfx, null);
        CryptographicOperations.ZeroMemory(pfx);
        return result;
    }

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
