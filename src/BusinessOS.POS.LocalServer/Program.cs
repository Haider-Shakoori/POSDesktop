using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Infrastructure;
using BusinessOS.POS.Infrastructure.Networking;

var paths = new ApplicationPaths(); paths.EnsureCreated();
var configurations = new NetworkConfigurationStore(paths);
var config = await configurations.LoadAsync();
if (config.Mode != DeploymentMode.Server) throw new InvalidOperationException("Local Server can run only when this computer is configured as Main POS Server.");

var secrets = new WindowsNetworkSecretStore(paths);
using var certificate = await CertificateProvider.GetOrCreateAsync(paths, secrets);
var fingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(x => x.ServiceName = "BusinessOS POS Local Server");
builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(config.ServerPort, x => x.UseHttps(certificate)));
builder.Services.AddSingleton<IApplicationPaths>(paths);
builder.Services.AddSingleton<INetworkConfigurationStore>(configurations);
builder.Services.AddSingleton<INetworkSecretStore>(secrets);
builder.Services.AddSingleton<ILanTerminalRegistry, FileLanTerminalRegistry>();

var app = builder.Build();
var registry = app.Services.GetRequiredService<ILanTerminalRegistry>();
var serverId = await registry.GetOrCreateServerIdAsync();
config = config with { ServerId = serverId, ServerHost = Environment.MachineName, ServerCertificateSha256 = fingerprint, IsConfigured = true };
await configurations.SaveAsync(config);

var api = app.MapGroup("/api/local/v1");
api.MapGet("/health", () => Results.Ok(new { available = true, serverId, serverName = config.ServerName, serverTime = DateTimeOffset.UtcNow }));
api.MapGet("/server-info", () => Results.Ok(new { serverId, serverName = config.ServerName, hostName = Environment.MachineName, port = config.ServerPort, certificateSha256 = fingerprint }));
api.MapPost("/pairing/complete", async (PairRequest request, ILanTerminalRegistry terminals, CancellationToken ct) =>
{
    try
    {
        var paired = await terminals.PairAsync(request.PairingCode, request.TerminalId, request.Name, request.ComputerName, ct);
        return Results.Ok(paired);
    }
    catch (UnauthorizedAccessException ex) { return Results.Unauthorized(); }
});
api.MapPost("/terminals/heartbeat", async (HttpContext http, ILanTerminalRegistry terminals, CancellationToken ct) =>
{
    if (!http.Request.Headers.TryGetValue("X-Terminal-Id", out var id) || !http.Request.Headers.TryGetValue("X-Terminal-Secret", out var secret) ||
        !await terminals.AuthenticateAsync(id.ToString(), secret.ToString(), ct)) return Results.Unauthorized();
    await terminals.TouchAsync(id.ToString(), ct); return Results.NoContent();
});
await app.RunAsync();

sealed record PairRequest(string PairingCode, string TerminalId, string Name, string ComputerName);

static class CertificateProvider
{
    public static async Task<X509Certificate2> GetOrCreateAsync(IApplicationPaths paths, INetworkSecretStore secrets)
    {
        var password = await secrets.LoadServerCertificatePasswordAsync();
        if (File.Exists(paths.ServerCertificatePath) && !string.IsNullOrWhiteSpace(password))
            return X509CertificateLoader.LoadPkcs12FromFile(paths.ServerCertificatePath, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);

        password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest($"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(Environment.MachineName); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
            foreach (var address in nic.GetIPProperties().UnicastAddresses.Select(x => x.Address).Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x)))
                san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        var pfx = generated.Export(X509ContentType.Pfx, password);
        await File.WriteAllBytesAsync(paths.ServerCertificatePath, pfx); CryptographicOperations.ZeroMemory(pfx);
        await secrets.SaveServerCertificatePasswordAsync(password);
        return X509CertificateLoader.LoadPkcs12FromFile(paths.ServerCertificatePath, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);
    }
}
