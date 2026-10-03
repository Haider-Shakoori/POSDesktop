namespace BusinessOS.POS.Domain.Access;

public static class RoleCatalog
{
    public static IReadOnlyDictionary<string, string> SystemRoles { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner"] = "Owner",
            ["administrator"] = "Administrator",
            ["manager"] = "Manager",
            ["cashier"] = "Cashier",
            ["stock_keeper"] = "Stock Keeper",
            ["accountant"] = "Accountant",
        };

    public static IReadOnlyDictionary<string, string[]> Assignments { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["manager"] =
            [
                "pos.access", "sales.view", "sales.create", "sales.return", "sales.void", "sales.hold",
                "sales.discount", "sales.override_min_price", "sales.credit", "sales.override_credit_limit",
                "purchases.view", "purchases.create", "purchases.approve", "purchases.receive",
                "purchases.direct_receive", "purchases.record_payment", "purchases.return",
                "inventory.view", "inventory.products.manage", "inventory.catalog.manage", "inventory.opening_stock",
                "inventory.adjust", "inventory.count", "inventory.count.approve", "inventory.writeoff",
                "customers.view", "customers.manage", "customers.quick_create", "customers.collect",
                "suppliers.view", "suppliers.manage", "suppliers.pay",
                "expenses.view", "expenses.create", "cash.view", "cash.manage", "reports.view", "reports.profit",
                "shifts.open", "shifts.close", "business_days.view", "business_days.close",
            ],
            ["cashier"] =
            [
                "pos.access", "sales.view", "sales.create", "sales.return", "sales.hold", "sales.credit",
                "customers.view", "customers.quick_create", "customers.collect", "cash.view", "shifts.open", "shifts.close",
            ],
            ["stock_keeper"] =
            [
                "purchases.view", "purchases.create", "purchases.receive",
                "inventory.view", "inventory.products.manage", "inventory.catalog.manage", "inventory.opening_stock",
                "inventory.adjust", "inventory.count", "inventory.writeoff", "suppliers.view",
            ],
            ["accountant"] =
            [
                "sales.view", "purchases.view", "purchases.record_payment",
                "customers.view", "customers.collect",
                "suppliers.view", "suppliers.pay", "expenses.view", "expenses.create", "cash.view",
                "reports.view", "reports.profit", "business_days.view", "business_days.close",
            ],
        };
}
