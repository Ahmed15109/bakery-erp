using System.Globalization;
using System.Windows;

namespace Bakery.WPF.Helpers;

public static class FlowDirectionHelper
{
    public static FlowDirection FromCulture(CultureInfo cultureInfo)
    {
        return cultureInfo.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
