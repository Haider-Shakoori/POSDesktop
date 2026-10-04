namespace BusinessOS.POS.Persistence.Entities;

public sealed class LanUserSessionEntity
{
    public long Id { get; set; }
    public string TokenHashBase64 { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public long UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
