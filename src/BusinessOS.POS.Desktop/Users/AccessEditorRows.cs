using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Users;

public sealed partial class SelectableRoleRow(long id, string name, string label) : ObservableObject
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class SelectablePermissionRow(long id, string name, string label) : ObservableObject
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isSelected;
}
