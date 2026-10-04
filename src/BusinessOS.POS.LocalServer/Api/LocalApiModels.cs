namespace BusinessOS.POS.LocalServer.Api;

public sealed record LocalServerInfoResponse(
    string Service,
    string ApiVersion,
    string ApplicationVersion,
    string MinimumClientVersion,
    string ServerId,
    string ServerName,
    string HostName,
    int Port,
    string CertificateSha256);

public sealed record PairTerminalApiRequest(
    string PairingCode,
    string TerminalId,
    string Name,
    string ComputerName,
    string TerminalRole);

public sealed record LoginApiRequest(string Username, string Password);
