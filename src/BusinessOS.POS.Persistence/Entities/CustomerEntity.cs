namespace BusinessOS.POS.Persistence.Entities;

public sealed class CustomerEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; } = true;
}
