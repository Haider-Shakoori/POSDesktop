using BusinessOS.POS.Application;
using BusinessOS.POS.Domain;

namespace BusinessOS.POS.UnitTests;

public sealed class FoundationTests
{
    [Fact]
    public void Afghanistan_product_profile_is_locked()
    {
        Assert.Equal("AFN", ProductRules.CurrencyCode);
        Assert.False(ProductRules.TaxEnabled);
        Assert.Equal(["en", "fa", "ps"], ProductRules.SupportedLanguages);
    }

    [Fact]
    public void Core_pos_modules_are_declared()
    {
        var modules = Enum.GetValues<PosModule>();

        Assert.Contains(PosModule.PointOfSale, modules);
        Assert.Contains(PosModule.Inventory, modules);
        Assert.Contains(PosModule.Purchasing, modules);
        Assert.Contains(PosModule.CashAndShifts, modules);
        Assert.Contains(PosModule.DailyClosing, modules);
        Assert.Contains(PosModule.Reports, modules);
    }
}
