namespace BusinessOS.POS.Persistence.Entities;

public sealed class BrandEntity
{
    public long Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public bool IsActive { get; set; } = true;
}
