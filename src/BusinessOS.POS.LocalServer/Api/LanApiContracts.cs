using System.Text.Json;

namespace BusinessOS.POS.LocalServer.Api;

public sealed record LanLoginRequest(string Username, string Password);
public sealed record LanLocaleRequest(string Locale);
public sealed record LanRpcRequest(IReadOnlyList<JsonElement> Arguments);
public sealed record LanServerInfo(
    string Service, string ApiVersion, string ServerId, string ServerName,
    string HostName, int Port, string CertificateSha256);
