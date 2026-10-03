using System.Windows;

namespace BusinessOS.POS.Desktop.Appearance;

public static class ThemeManager
{
    private const string GlassSource = "Themes/Glass.xaml";

    public static AppearanceTheme Current { get; private set; } = AppearanceTheme.Classic;

    public static void Apply(AppearanceTheme theme)
    {
        var application = Application.Current;
        if (application is null)
        {
            Current = theme;
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var glass = dictionaries.FirstOrDefault(d =>
            d.Source is not null &&
            d.Source.OriginalString.EndsWith("Glass.xaml", StringComparison.OrdinalIgnoreCase));

        if (theme == AppearanceTheme.Glass && glass is null)
        {
            var assemblyName = typeof(ThemeManager).Assembly.GetName().Name;
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/{assemblyName};component/{GlassSource}",
                    UriKind.Absolute)
            });
        }
        else if (theme == AppearanceTheme.Classic && glass is not null)
        {
            dictionaries.Remove(glass);
        }

        Current = theme;
    }
}
