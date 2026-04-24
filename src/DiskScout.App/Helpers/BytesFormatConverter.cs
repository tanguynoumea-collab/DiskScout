using System.Globalization;
using System.Windows.Data;

namespace DiskScout.Helpers;

public sealed class BytesFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long bytes = value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => 0,
        };
        if (bytes <= 0) return "0 o";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:F1} {units[u]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
}
