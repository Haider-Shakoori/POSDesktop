namespace BusinessOS.POS.Persistence.Entities;

public sealed class CategoryEntity
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public CategoryEntity? Parent { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
