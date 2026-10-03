namespace BusinessOS.POS.Persistence.Entities;

public sealed class UnitEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public string? Symbol { get; set; }
    public int DecimalPlaces { get; set; }
    public bool IsActive { get; set; } = true;
}
