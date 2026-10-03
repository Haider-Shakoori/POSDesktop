namespace BusinessOS.POS.Persistence.Entities;

public sealed class PaymentMethodEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public bool IsCash { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
