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
using BusinessOS.POS.Domain.Authentication;

var paths = new ApplicationPaths();
paths.EnsureCreated();

var bootstrapConfigurationStore = new NetworkConfigurationStore(paths);
var networkConfiguration = await bootstrapConfigurationStore.LoadAsync();
if (networkConfiguration.Mode != DeploymentMode.Server)
{
    throw new InvalidOperationException(
        "BusinessOS POS Local Server can run only when this computer is configured as Main POS Server.");
}

var bootstrapSecrets = new WindowsNetworkSecretStore(paths);
var certificateProvider = new LocalServerCertificateProvider(bootstrapSecrets);
using var certificate = await certificateProvider.GetOrCreateAsync();
var certificateSha256 = LocalServerCertificateProvider.Fingerprint(certificate);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    options.ListenAnyIP(networkConfiguration.ServerPort, listen => listen.UseHttps(certificate));
});

builder.Services.AddSingleton<IApplicationPaths>(paths);
builder.Services.AddSingleton<INetworkConfigurationStore>(bootstrapConfigurationStore);
builder.Services.AddSingleton<INetworkSecretStore>(bootstrapSecrets);
builder.Services.AddBusinessOSPosPersistence();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IUserSessionService, RequestUserSessionService>();
builder.Services.AddSingleton<LanAuthenticationService>();
builder.Services.AddScoped<TerminalAuthenticationFilter>();
builder.Services.AddScoped<UserSessionAuthenticationFilter>();
builder.Services.AddScoped<LanRpcDispatcher>();
builder.Services.AddSingleton<LanServerRuntimeState>();
builder.Services.AddHostedService<UdpDiscoveryResponder>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    try
    {
        await next();
    }
    catch (PermissionDeniedException)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "This user does not have permission for this operation." });
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

app.Services.GetRequiredService<LanServerRuntimeState>()
    .Initialize(identity, networkConfiguration, certificateSha256);

var local = app.MapGroup("/api/local/v1");

local.MapGet("/health", (LanServerRuntimeState runtime) =>
{
    var state = runtime.Require();
    return Results.Ok(new LocalServerHealth(true, "v1", state.Identity.ServerId, DateTimeOffset.UtcNow));
});

local.MapGet("/server-info", (LanServerRuntimeState runtime) =>
{
    var state = runtime.Require();
    return Results.Ok(new LanServerInfo(
        "BusinessOS.POS.LocalServer", "v1", state.Identity.ServerId,
        state.Identity.ServerName, Environment.MachineName,
        state.Configuration.ServerPort, state.CertificateSha256));
});

local.MapPost("/pairing/complete", async (
    PairTerminalRequest request,
    ILocalTerminalService terminals,
    CancellationToken cancellationToken) =>
{
    var paired = await terminals.PairAsync(request, cancellationToken);
    return Results.Ok(paired);
});

var terminal = local.MapGroup(string.Empty);
terminal.AddEndpointFilter<TerminalAuthenticationFilter>();

terminal.MapGet("/terminals/heartbeat", (HttpContext http) =>
{
    var registered = (RegisteredTerminal)http.Items[TerminalAuthenticationFilter.TerminalItemKey]!;
    return Results.Ok(new { registered.TerminalId, serverTime = DateTimeOffset.UtcNow });
});

terminal.MapPost("/auth/login", async (
    LanLoginRequest request,
    HttpContext http,
    LanAuthenticationService authentication,
    CancellationToken cancellationToken) =>
{
    var registered = (RegisteredTerminal)http.Items[TerminalAuthenticationFilter.TerminalItemKey]!;
    return Results.Ok(await authentication.LoginAsync(
        registered.TerminalId, request.Username, request.Password, cancellationToken));
});

var authorized = terminal.MapGroup(string.Empty);
authorized.AddEndpointFilter<UserSessionAuthenticationFilter>();

authorized.MapPost("/auth/logout", async (
    HttpContext http,
    LanAuthenticationService authentication,
    CancellationToken cancellationToken) =>
{
    var terminalInfo = (RegisteredTerminal)http.Items[TerminalAuthenticationFilter.TerminalItemKey]!;
    var token = http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
    await authentication.LogoutAsync(terminalInfo.TerminalId, token, cancellationToken);
    return Results.NoContent();
});

authorized.MapPost("/auth/locale", async (
    LanLocaleRequest request,
    IUserSessionService sessions,
    CancellationToken cancellationToken) =>
{
    await sessions.UpdatePreferredLocaleAsync(request.Locale, cancellationToken);
    return Results.NoContent();
});

authorized.MapPost("/rpc/{serviceName}/{methodName}", async (
    string serviceName,
    string methodName,
    LanRpcRequest request,
    LanRpcDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    var result = await dispatcher.InvokeAsync(serviceName, methodName, request, cancellationToken);
    return result is null ? Results.NoContent() : Results.Json(result);
});

await app.RunAsync();
