using System.Globalization;
using System.Windows.Data;

namespace UDM18.Client.Converters;

public sealed class GameAreaHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var windowHeight = value is double height && double.IsFinite(height) ? height : 780d;
        var reserved = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 150d;
        return Math.Clamp(windowHeight - reserved, 470d, 700d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
