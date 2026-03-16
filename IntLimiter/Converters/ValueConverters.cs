using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IntLimiter.Converters;

[ValueConversion(typeof(double), typeof(string))]
public class BpsToHumanReadableConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double bps = value is double d ? d : 0;
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000:F1} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000:F1} Mbps";
        if (bps >= 1_000) return $"{bps / 1_000:F1} Kbps";
        return $"{bps:F0} bps";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(long), typeof(string))]
public class BytesToHumanReadableConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long bytes = value is long l ? l : 0;
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024) return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(string))]
public class BoolToEnabledTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Enabled" : "Disabled";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
