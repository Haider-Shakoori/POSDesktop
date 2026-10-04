using System.IO.Compression;
using System.Threading.RateLimiting;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Infrastructure;
using BusinessOS.POS.Infrastructure.Networking;
using BusinessOS.POS.LocalServer.Api;
using BusinessOS.POS.LocalServer.Authentication;
using BusinessOS.POS.LocalServer.Discovery;
using BusinessOS.POS.LocalServer.Runtime;
using BusinessOS.POS.LocalServer.Security;
using BusinessOS.POS.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection.Extensions;

var paths = new ApplicationPaths();
paths.EnsureCreated();

var bootstrapConfigurationStore = new NetworkConfigurationStore(paths);
var networkConfiguration = await bootstrapConfigurationStore.LoadAsync();
if (networkConfiguration.Mode != DeploymentMode.Server)
    throw new InvalidOperationException(
        "BusinessOS POS Local Server can run only when this computer is configured as Main POS Server.");

var bootstrapServices = new ServiceCollection();
bootstrapServices.AddSingleton<IApplicationPaths>(paths);
bootstrapServices.AddBusinessOSPosInfrastructure();
using var bootstrapProvider = bootstrapServices.BuildServiceProvider();
var bootstrapSecrets = bootstrapProvider.GetRequiredService<INetworkSecretStore>();
var certificateProvider = new LocalServerCertificateProvider(paths, bootstrapSecrets);
using var certificate = await certificateProvider.GetOrCreateAsync();
var certificateSha256 = LocalServerCertificateProvider.Sha256Fingerprint(certificate);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "BusinessOS POS Local Server");

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    options.ListenAnyIP(networkConfiguration.ServerPort, listen => listen.UseHttps(certificate));
});

builder.Services.AddSingleton<IApplicationPaths>(paths);
builder.Services.AddBusinessOSPosInfrastructure();
builder.Services.AddBusinessOSPosPersistence();

builder.Services.AddScoped<LanRequestContext>();
builder.Services.AddScoped<TerminalAuthenticationFilter>();
builder.Services.AddScoped<UserSessionAuthenticationFilter>();
builder.Services.AddScoped<LanUserAuthenticationService>();
builder.Services.Replace(ServiceDescriptor.Scoped<IUserSessionService, RequestUserSessionService>());
builder.Services.Replace(ServiceDescriptor.Scoped<IPermissionAuthorizer, RequestPermissionAuthorizer>());
builder.Services.AddSingleton<LocalServerRuntimeState>();
builder.Services.AddHostedService<UdpDiscoveryResponder>();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(x => x.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(x => x.Level = CompressionLevel.Fastest);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("pairing", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.AddPolicy("lan", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 900,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
});

var app = builder.Build();
app.UseResponseCompression();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try { await next(); }
    catch (PermissionDeniedException)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "Permission denied." });
    }
    catch (UnauthorizedAccessException ex)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
    catch (ArgumentException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
});

await app.Services.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
var identity = await app.Services.GetRequiredService<ILocalTerminalService>()
    .GetOrCreateServerIdentityAsync(networkConfiguration.ServerName);

networkConfiguration = networkConfiguration with
{
    ServerHost = Environment.MachineName,
    ServerId = identity.ServerId,
    ServerCertificateSha256 = certificateSha256,
    IsConfigured = true,
};
await app.Services.GetRequiredService<INetworkConfigurationStore>()
    .SaveAsync(networkConfiguration);

app.Services.GetRequiredService<LocalServerRuntimeState>()
    .Initialize(identity, networkConfiguration, certificateSha256);

var local = app.MapGroup("/api/local/v1").RequireRateLimiting("lan");

local.MapGet("/health", (LocalServerRuntimeState runtime) =>
{
    var state = runtime.Require();
    return Results.Ok(new LocalServerHealth(
        true, "v1", state.Identity.ServerId, DateTimeOffset.UtcNow));
});

local.MapGet("/server-info", (LocalServerRuntimeState runtime) =>
{
    var state = runtime.Require();
    return Results.Ok(new LocalServerInfoResponse(
        "BusinessOS.POS.LocalServer",
        "v1",
        typeof(LocalServerInfoResponse).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "1.0.0",
        state.Identity.ServerId,
        state.Identity.ServerName,
        Environment.MachineName,
        state.Configuration.ServerPort,
        state.CertificateSha256));
});

local.MapPost("/pairing/complete", async (
    PairTerminalApiRequest request,
    ILocalTerminalService terminals,
    CancellationToken ct) =>
{
    var result = await terminals.PairAsync(
        new PairTerminalRequest(
            request.PairingCode,
            request.TerminalId,
            request.Name,
            request.ComputerName,
            request.TerminalRole),
        ct);
    return Results.Ok(result);
}).RequireRateLimiting("pairing");

var terminal = local.MapGroup(string.Empty);
terminal.AddEndpointFilter<TerminalAuthenticationFilter>();

terminal.MapPost("/auth/login", async (
    LoginApiRequest request,
    LanRequestContext requestContext,
    LanUserAuthenticationService authentication,
    CancellationToken ct) =>
{
    var registered = requestContext.Terminal
        ?? throw new UnauthorizedAccessException("Terminal authentication is required.");
    return Results.Ok(await authentication.LoginAsync(
        registered.TerminalId,
        request.Username,
        request.Password,
        ct));
});

terminal.MapPost("/terminals/heartbeat", (LanRequestContext requestContext) =>
    Results.Ok(new
    {
        terminal_id = requestContext.Terminal!.TerminalId,
        server_time = DateTimeOffset.UtcNow,
    }));

var authorized = terminal.MapGroup(string.Empty);
authorized.AddEndpointFilter<UserSessionAuthenticationFilter>();

authorized.MapPost("/auth/logout", async (
    IUserSessionService session,
    CancellationToken ct) =>
{
    await session.LogoutAsync(ct);
    return Results.NoContent();
});

authorized.MapPost("/auth/locale/{locale}", async (
    string locale,
    IUserSessionService session,
    CancellationToken ct) =>
{
    await session.UpdatePreferredLocaleAsync(locale, ct);
    return Results.NoContent();
});

// Operational endpoints are mapped in LanOperationalEndpoints to keep Program startup auditable.
LanOperationalEndpoints.Map(authorized);

await app.RunAsync();

public partial class Program;
