namespace BusinessOS.POS.Persistence.Entities;

public sealed class ExpenseCategoryEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string EntryType { get; set; } = "expense";
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
