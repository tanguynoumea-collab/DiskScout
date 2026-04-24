using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskScout.Helpers;

public sealed class PercentToStarGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => 0,
        };
        // Floor at tiny non-zero to avoid column collapse edge-cases.
        var weight = percent <= 0 ? 0.0001 : percent;
        return new GridLength(weight, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
}
