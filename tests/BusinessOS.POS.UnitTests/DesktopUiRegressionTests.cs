using System.Text.RegularExpressions;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class DesktopUiRegressionTests
{
    [Fact]
    public void Every_declared_sidebar_module_has_a_concrete_content_template()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "MainWindowViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "MainWindow.xaml"));

        var keys = Regex.Matches(
                viewModel,
                """new(?: NavigationItemViewModel)?\("([^"]+)",""")
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.Contains(
                $"Value=\"{key}\"",
                xaml,
                StringComparison.Ordinal);
        }

        Assert.Contains("""x:Key="UsersTemplate" """, xaml, StringComparison.Ordinal);
        Assert.Contains("""Value="users" """, xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_xaml_remains_afn_only_and_contains_no_usd_or_dollar_currency_labels()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "BusinessOS.POS.Desktop");
        var files = Directory.EnumerateFiles(desktop, "*.xaml", SearchOption.AllDirectories).ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var xaml = File.ReadAllText(file);
            Assert.DoesNotContain("USD", xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("$", xaml, StringComparison.Ordinal);
        }

        var combined = string.Join("\n", files.Select(File.ReadAllText));
        Assert.Contains("AFN", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Signed_in_shell_and_first_run_setup_are_bound_to_runtime_flow_direction()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "MainWindow.xaml"));
        var ownerSetup = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "Authentication", "OwnerSetupWindow.xaml"));
        var mainVm = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "MainWindowViewModel.cs"));
        var ownerVm = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "Authentication", "OwnerSetupViewModel.cs"));

        Assert.Contains("""FlowDirection="{Binding FlowDirection}" """, main, StringComparison.Ordinal);
        Assert.Contains("""FlowDirection="{Binding FlowDirection}" """, ownerSetup, StringComparison.Ordinal);

        Assert.Contains("RightToLeft", mainVm, StringComparison.Ordinal);
        Assert.Contains("RightToLeft", ownerVm, StringComparison.Ordinal);
        Assert.Contains("\"دری\"", mainVm, StringComparison.Ordinal);
        Assert.Contains("\"پښتو\"", mainVm, StringComparison.Ordinal);
        Assert.Contains("\"دری\"", ownerVm, StringComparison.Ordinal);
        Assert.Contains("\"پښتو\"", ownerVm, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_table_style_keeps_centered_rows_separators_and_theme_selected_state()
    {
        var root = FindRepositoryRoot();
        var grid = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "Themes", "DataGrid.xaml"));

        Assert.Contains("""VerticalContentAlignment" Value="Center" """, grid, StringComparison.Ordinal);
        Assert.Contains("""BorderThickness" Value="0,0,0,1" """, grid, StringComparison.Ordinal);
        Assert.Contains("TableSelectedBrush", grid, StringComparison.Ordinal);
        Assert.Contains("""Foreground" Value="White" """, grid, StringComparison.Ordinal);
        Assert.Contains("""FontWeight" Value="Bold" """, grid, StringComparison.Ordinal);
        Assert.Contains("""HorizontalContentAlignment" Value="Left" """, grid, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_final_modules_no_longer_fall_back_to_the_scaffold_placeholder()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "BusinessOS.POS.Desktop", "MainWindow.xaml"));

        var expected = new[]
        {
            "dashboard", "pos", "sales", "products", "inventory", "purchasing",
            "customers", "cash", "closing", "expenses", "reports", "users",
            "terminals", "settings",
        };

        foreach (var key in expected)
            Assert.Contains($"Value=\"{key}\"", xaml, StringComparison.Ordinal);

        Assert.Equal(expected.Length, expected.Count(key =>
            xaml.Contains($"Value=\"{key}\"", StringComparison.Ordinal)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BusinessOS.POS.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate BusinessOS.POS.sln from the test execution directory.");
    }
}
