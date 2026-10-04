namespace BusinessOS.POS.Persistence.Entities;

public sealed class LanPairingCodeEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaltBase64 { get; set; } = string.Empty;
    public string CodeHashBase64 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public int FailedAttempts { get; set; }
    public int MaxAttempts { get; set; } = 5;
}
