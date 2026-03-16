using System;
using System.Linq;
using System.Windows;

namespace IntLimiter;

public partial class App : Application
{
    public static void SwitchTheme(bool isDark)
    {
        var uri = new Uri(
            isDark
                ? "Themes/Windows11Styles.xaml"
                : "Themes/Windows11Styles.Light.xaml",
            UriKind.Relative);

        var dicts = Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d =>
            d.Source != null && d.Source.OriginalString.Contains("Windows11Styles"));
        if (existing != null)
            dicts.Remove(existing);

        dicts.Add(new ResourceDictionary { Source = uri });
    }
}
