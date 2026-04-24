using System.Globalization;
using System.Windows.Data;

namespace DiskScout.Helpers;

/// <summary>Returns true when the numeric value is non-zero. Used to enable a button only when count > 0.</summary>
public sealed class ZeroToBoolInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            _ => false,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value;
}
