using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Users;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Users;

public sealed partial class UsersViewModel : ObservableObject
{
    private readonly IUserAccessService _access;
    private bool _loaded;

    public UsersViewModel(IUserAccessService access)
    {
        _access = access;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewUserCommand = new RelayCommand(NewUser);
        SaveUserCommand = new AsyncRelayCommand(SaveUserAsync);
        ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync);
        NewRoleCommand = new RelayCommand(NewRole);
        SaveRoleCommand = new AsyncRelayCommand(SaveRoleAsync);
    }

    public ObservableCollection<AccessUserRow> Users { get; } = [];
    public ObservableCollection<AccessRoleRow> Roles { get; } = [];
    public ObservableCollection<AccessAuditRow> Audit { get; } = [];
    public ObservableCollection<SelectableRoleRow> UserRoles { get; } = [];
    public ObservableCollection<SelectablePermissionRow> RolePermissions { get; } = [];
    public string[] Languages { get; } = ["en", "fa", "ps"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewUserCommand { get; }
    public IAsyncRelayCommand SaveUserCommand { get; }
    public IAsyncRelayCommand ResetPasswordCommand { get; }
    public IRelayCommand NewRoleCommand { get; }
    public IAsyncRelayCommand SaveRoleCommand { get; }

    [ObservableProperty] private AccessUserRow? _selectedUser;
    [ObservableProperty] private AccessRoleRow? _selectedRole;
    [ObservableProperty] private long? _editingUserId;
    [ObservableProperty] private string _userName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string _preferredLocale = "en";
    [ObservableProperty] private bool _userIsActive = true;
    [ObservableProperty] private string _userPassword = string.Empty;
    [ObservableProperty] private long? _editingRoleId;
    [ObservableProperty] private string _roleName = string.Empty;
    [ObservableProperty] private string _roleLabel = string.Empty;
    [ObservableProperty] private bool _roleIsSystem;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private bool _isBusy;

    public string UserEditorTitle => EditingUserId is null ? "New user" : "Edit user";
    public string RoleEditorTitle => EditingRoleId is null ? "New custom role" : "Edit custom role";
    public bool CanEditRole => !RoleIsSystem;

    partial void OnSelectedUserChanged(AccessUserRow? value)
    {
        if (value is null) return;
        EditingUserId = value.Id;
        UserName = value.Name;
        Username = value.Username;
        Email = value.Email;
        PreferredLocale = value.PreferredLocale;
        UserIsActive = value.IsActive;
        UserPassword = string.Empty;
        foreach (var role in UserRoles) role.IsSelected = value.RoleIds.Contains(role.Id);
        OnPropertyChanged(nameof(UserEditorTitle));
    }

    partial void OnSelectedRoleChanged(AccessRoleRow? value)
    {
        if (value is null) return;
        EditingRoleId = value.Id;
        RoleName = value.Name;
        RoleLabel = value.Label;
        RoleIsSystem = value.IsSystem;
        foreach (var permission in RolePermissions)
            permission.IsSelected = value.PermissionIds.Contains(permission.Id);
        OnPropertyChanged(nameof(RoleEditorTitle));
        OnPropertyChanged(nameof(CanEditRole));
    }

    partial void OnEditingUserIdChanged(long? value) => OnPropertyChanged(nameof(UserEditorTitle));
    partial void OnEditingRoleIdChanged(long? value) => OnPropertyChanged(nameof(RoleEditorTitle));
    partial void OnRoleIsSystemChanged(bool value) => OnPropertyChanged(nameof(CanEditRole));

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            var selectedUserId = SelectedUser?.Id;
            var selectedRoleId = SelectedRole?.Id;
            var snapshot = await _access.GetAsync();

            Users.Clear();
            foreach (var user in snapshot.Users) Users.Add(user);

            Roles.Clear();
            UserRoles.Clear();
            foreach (var role in snapshot.Roles)
            {
                Roles.Add(role);
                UserRoles.Add(new SelectableRoleRow(role.Id, role.Name, role.Label));
            }

            RolePermissions.Clear();
            foreach (var permission in snapshot.Permissions)
                RolePermissions.Add(new SelectablePermissionRow(permission.Id, permission.Name, permission.Label));

            Audit.Clear();
            foreach (var row in snapshot.Audit) Audit.Add(row);

            SelectedUser = selectedUserId is null
                ? Users.FirstOrDefault()
                : Users.FirstOrDefault(x => x.Id == selectedUserId.Value) ?? Users.FirstOrDefault();
            SelectedRole = selectedRoleId is null
                ? Roles.FirstOrDefault()
                : Roles.FirstOrDefault(x => x.Id == selectedRoleId.Value) ?? Roles.FirstOrDefault();

            StatusMessage = "Users, roles and audit history refreshed.";
        });
    }

    private void NewUser()
    {
        SelectedUser = null;
        EditingUserId = null;
        UserName = string.Empty;
        Username = string.Empty;
        Email = null;
        PreferredLocale = "en";
        UserIsActive = true;
        UserPassword = string.Empty;
        foreach (var role in UserRoles) role.IsSelected = false;
        StatusMessage = "New user form ready.";
    }

    private async Task SaveUserAsync()
    {
        await RunBusyAsync(async () =>
        {
            var saved = await _access.SaveUserAsync(new UserSaveRequest(
                EditingUserId,
                UserName,
                Username,
                Email,
                PreferredLocale,
                UserIsActive,
                UserRoles.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
                string.IsNullOrWhiteSpace(UserPassword) ? null : UserPassword));
            await RefreshCoreAsync(saved.Id, SelectedRole?.Id);
            UserPassword = string.Empty;
            StatusMessage = "User saved.";
        });
    }

    private async Task ResetPasswordAsync()
    {
        if (EditingUserId is null)
        {
            StatusMessage = "Save the user before resetting a password.";
            return;
        }

        if (string.IsNullOrWhiteSpace(UserPassword))
        {
            StatusMessage = "Enter the new password first.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _access.ResetPasswordAsync(EditingUserId.Value, UserPassword);
            UserPassword = string.Empty;
            StatusMessage = "Password reset completed.";
        });
    }

    private void NewRole()
    {
        SelectedRole = null;
        EditingRoleId = null;
        RoleName = string.Empty;
        RoleLabel = string.Empty;
        RoleIsSystem = false;
        foreach (var permission in RolePermissions) permission.IsSelected = false;
        StatusMessage = "New custom role form ready.";
    }

    private async Task SaveRoleAsync()
    {
        if (RoleIsSystem)
        {
            StatusMessage = "System roles are read-only.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var saved = await _access.SaveRoleAsync(new RoleSaveRequest(
                EditingRoleId,
                RoleName,
                RoleLabel,
                RolePermissions.Where(x => x.IsSelected).Select(x => x.Id).ToList()));
            await RefreshCoreAsync(SelectedUser?.Id, saved.Id);
            StatusMessage = "Custom role saved.";
        });
    }

    private async Task RefreshCoreAsync(long? userId, long? roleId)
    {
        var snapshot = await _access.GetAsync();

        Users.Clear();
        foreach (var user in snapshot.Users) Users.Add(user);

        Roles.Clear();
        UserRoles.Clear();
        foreach (var role in snapshot.Roles)
        {
            Roles.Add(role);
            UserRoles.Add(new SelectableRoleRow(role.Id, role.Name, role.Label));
        }

        RolePermissions.Clear();
        foreach (var permission in snapshot.Permissions)
            RolePermissions.Add(new SelectablePermissionRow(permission.Id, permission.Name, permission.Label));

        Audit.Clear();
        foreach (var row in snapshot.Audit) Audit.Add(row);

        SelectedUser = userId is null ? Users.FirstOrDefault() : Users.FirstOrDefault(x => x.Id == userId);
        SelectedRole = roleId is null ? Roles.FirstOrDefault() : Roles.FirstOrDefault(x => x.Id == roleId);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
