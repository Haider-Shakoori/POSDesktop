namespace BusinessOS.POS.Persistence.Entities;

public sealed class DocumentSequenceEntity
{
    public string Key { get; set; } = string.Empty;
    public long NextValue { get; set; } = 1;
}
