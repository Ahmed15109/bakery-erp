using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Bakery.WPF.Converters;

public class AmountToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal amount)
        {
            return amount >= 0 ? Brushes.SeaGreen : Brushes.Crimson;
        }
        return Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
