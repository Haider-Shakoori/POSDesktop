using BusinessOS.POS.Application;
using BusinessOS.POS.Domain;
using BusinessOS.POS.Domain.Access;
using BusinessOS.POS.Persistence.Security;
using Xunit;

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

    [Fact]
    public void Web_pos_permission_matrix_is_preserved()
    {
        Assert.Equal(47, PermissionCatalog.All.Count);
        Assert.Equal(6, RoleCatalog.SystemRoles.Count);
        Assert.Contains("pos.access", RoleCatalog.Assignments["cashier"]);
        Assert.Contains("shifts.close", RoleCatalog.Assignments["cashier"]);
        Assert.DoesNotContain("reports.profit", RoleCatalog.Assignments["cashier"]);
    }

    [Fact]
    public void Password_hasher_round_trips_without_storing_plaintext()
    {
        var hasher = new PasswordHasher();
        var encoded = hasher.Hash("A-strong-local-password");

        Assert.DoesNotContain("A-strong-local-password", encoded);
        Assert.True(hasher.Verify("A-strong-local-password", encoded));
        Assert.False(hasher.Verify("wrong-password", encoded));
    }
}
