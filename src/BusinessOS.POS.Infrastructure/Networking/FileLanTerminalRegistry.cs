using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class FileLanTerminalRegistry(IApplicationPaths paths) : ILanTerminalRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1,1);
    private string? _pairingCode;
    private DateTimeOffset _pairingExpiresAt;

    public async Task<string> GetOrCreateServerIdAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(state.ServerId)) return state.ServerId;
        state = state with { ServerId = Guid.CreateVersion7().ToString("N") };
        await SaveAsync(state, cancellationToken);
        return state.ServerId;
    }

    public Task<PairingCodeIssue> CreatePairingCodeAsync(TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        _pairingCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _pairingExpiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        return Task.FromResult(new PairingCodeIssue(_pairingCode, _pairingExpiresAt));
    }

    public async Task<PairTerminalResult> PairAsync(string pairingCode, string terminalId, string name, string computerName, CancellationToken cancellationToken = default)
    {
        if (_pairingCode is null || DateTimeOffset.UtcNow > _pairingExpiresAt || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pairingCode), Encoding.UTF8.GetBytes(_pairingCode)))
            throw new UnauthorizedAccessException("The pairing code is invalid or expired.");

        var serverId = await GetOrCreateServerIdAsync(cancellationToken);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var list = state.Terminals.Where(x => x.TerminalId != terminalId).ToList();
            list.Add(new StoredTerminal(terminalId, name.Trim(), computerName.Trim(), true, now, now, Hash(secret)));
            await SaveUnsafeAsync(state with { ServerId = serverId, Terminals = list }, cancellationToken);
        }
        finally { _gate.Release(); }
        _pairingCode = null;
        return new PairTerminalResult(terminalId, secret, serverId, now);
    }

    public async Task<bool> AuthenticateAsync(string terminalId, string terminalSecret, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        var row = state.Terminals.FirstOrDefault(x => x.TerminalId == terminalId && x.IsActive);
        if (row is null) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(row.SecretHash), Convert.FromHexString(Hash(terminalSecret)));
    }

    public async Task TouchAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var list = state.Terminals.Select(x => x.TerminalId == terminalId ? x with { LastSeenAt = DateTimeOffset.UtcNow } : x).ToList();
            await SaveUnsafeAsync(state with { Terminals = list }, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<LanTerminal>> ListAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Terminals
            .OrderBy(x => x.Name)
            .Select(x => new LanTerminal(x.TerminalId, x.Name, x.ComputerName, x.IsActive, x.RegisteredAt, x.LastSeenAt))
            .ToList();

    public async Task RevokeAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var list = state.Terminals.Select(x => x.TerminalId == terminalId ? x with { IsActive = false } : x).ToList();
            await SaveUnsafeAsync(state with { Terminals = list }, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private Task<State> LoadAsync(CancellationToken ct) => LockedLoadAsync(ct);
    private async Task<State> LockedLoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await LoadUnsafeAsync(ct); } finally { _gate.Release(); }
    }
    private async Task<State> LoadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(paths.LanTerminalsPath)) return new State();
        await using var stream = File.OpenRead(paths.LanTerminalsPath);
        return await JsonSerializer.DeserializeAsync<State>(stream, JsonOptions, ct) ?? new State();
    }
    private async Task SaveAsync(State state, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await SaveUnsafeAsync(state, ct); } finally { _gate.Release(); }
    }
    private async Task SaveUnsafeAsync(State state, CancellationToken ct)
    {
        paths.EnsureCreated();
        await using var stream = File.Create(paths.LanTerminalsPath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record State(string? ServerId = null, List<StoredTerminal>? TerminalRows = null)
    {
        public List<StoredTerminal> Terminals { get; init; } = TerminalRows ?? [];
    }
    private sealed record StoredTerminal(string TerminalId, string Name, string ComputerName, bool IsActive, DateTimeOffset RegisteredAt, DateTimeOffset? LastSeenAt, string SecretHash);
}
