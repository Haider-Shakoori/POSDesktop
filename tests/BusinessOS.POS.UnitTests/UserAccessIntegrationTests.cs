using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Application.Abstractions.Users;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class UserAccessIntegrationTests
{
    [Fact]
    public async Task User_creation_assigns_role_and_login_inherits_role_permissions()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var access = provider.GetRequiredService<IUserAccessService>();
            var snapshot = await access.GetAsync();
            var cashier = snapshot.Roles.Single(x => x.Name == "cashier");

            var user = await access.SaveUserAsync(new UserSaveRequest(
                null, "Test Cashier", "cashier14", "cashier@example.test", "en", true,
                [cashier.Id], "Password-123"));

            Assert.Equal("cashier14", user.Username);
            Assert.Contains("Cashier", user.Roles);

            var sessions = provider.GetRequiredService<IUserSessionService>();
            await sessions.LogoutAsync();
            var loggedIn = await sessions.LoginAsync("cashier14", "Password-123");

            Assert.Contains("cashier", loggedIn.Roles);
            Assert.Contains("pos.access", loggedIn.Permissions);
            Assert.DoesNotContain("users.manage", loggedIn.Permissions);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Last_active_owner_cannot_be_deactivated_or_lose_owner_role()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var access = provider.GetRequiredService<IUserAccessService>();
            var snapshot = await access.GetAsync();
            var owner = snapshot.Users.Single(x => x.Username == "owner");
            var ownerRole = snapshot.Roles.Single(x => x.Name == "owner");
            var cashier = snapshot.Roles.Single(x => x.Name == "cashier");

            var deactivate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                access.SaveUserAsync(new UserSaveRequest(
                    owner.Id, owner.Name, owner.Username, owner.Email, owner.PreferredLocale,
                    false, [ownerRole.Id], null)));
            Assert.Contains("own signed-in account", deactivate.Message, StringComparison.OrdinalIgnoreCase);

            var removeOwner = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                access.SaveUserAsync(new UserSaveRequest(
                    owner.Id, owner.Name, owner.Username, owner.Email, owner.PreferredLocale,
                    true, [cashier.Id], null)));
            Assert.Contains("last active owner", removeOwner.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Custom_role_is_editable_but_system_role_is_read_only()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var access = provider.GetRequiredService<IUserAccessService>();
            var snapshot = await access.GetAsync();
            var reportsView = snapshot.Permissions.Single(x => x.Name == "reports.view");
            var inventoryView = snapshot.Permissions.Single(x => x.Name == "inventory.view");

            var custom = await access.SaveRoleAsync(new RoleSaveRequest(
                null, "report_stock", "Report + Stock Viewer",
                [reportsView.Id, inventoryView.Id]));

            Assert.False(custom.IsSystem);
            Assert.Contains("View reports", custom.Permissions);
            Assert.Contains("View inventory", custom.Permissions);

            var manager = snapshot.Roles.Single(x => x.Name == "manager");
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                access.SaveRoleAsync(new RoleSaveRequest(
                    manager.Id, manager.Name, manager.Label, manager.PermissionIds)));
            Assert.Contains("read-only", blocked.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Password_reset_invalidates_old_password_and_access_changes_are_audited()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var access = provider.GetRequiredService<IUserAccessService>();
            var snapshot = await access.GetAsync();
            var cashier = snapshot.Roles.Single(x => x.Name == "cashier");

            var user = await access.SaveUserAsync(new UserSaveRequest(
                null, "Reset Cashier", "resetcashier", null, "fa", true,
                [cashier.Id], "Password-123"));
            await access.ResetPasswordAsync(user.Id, "Password-456");

            var sessions = provider.GetRequiredService<IUserSessionService>();
            await sessions.LogoutAsync();
            await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
                sessions.LoginAsync("resetcashier", "Password-123"));
            await sessions.LoginAsync("resetcashier", "Password-456");
            await sessions.LogoutAsync();
            await sessions.LoginAsync("owner", "Password-123");

            var after = await access.GetAsync();
            Assert.Contains(after.Audit, x => x.Event == "access.user.created");
            Assert.Contains(after.Audit, x => x.Event == "access.user.password_reset");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task User_manager_without_audit_permission_cannot_read_audit_history()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var access = provider.GetRequiredService<IUserAccessService>();
            var snapshot = await access.GetAsync();
            var usersManage = snapshot.Permissions.Single(x => x.Name == "users.manage");

            var role = await access.SaveRoleAsync(new RoleSaveRequest(
                null, "user_manager", "User Manager", [usersManage.Id]));
            await access.SaveUserAsync(new UserSaveRequest(
                null, "Limited User Manager", "usermanager", null, "en", true,
                [role.Id], "Password-123"));

            var sessions = provider.GetRequiredService<IUserSessionService>();
            await sessions.LogoutAsync();
            await sessions.LoginAsync("usermanager", "Password-123");

            var limited = await access.GetAsync();
            Assert.False(limited.CanViewAudit);
            Assert.Empty(limited.Audit);
        }
        finally { Cleanup(root); }
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TestPaths(root));
        services.AddBusinessOSPosPersistence();
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        await provider.GetRequiredService<IOwnerBootstrapService>()
            .CreateOwnerAsync("Batch 14 Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");
        return provider;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "BusinessOS-POS-access-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "access.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
