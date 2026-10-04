namespace BusinessOS.POS.Persistence.Entities;

public sealed class LanSessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
