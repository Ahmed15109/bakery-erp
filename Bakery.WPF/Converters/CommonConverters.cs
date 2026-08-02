using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Bakery.Domain.Enums;
using MaterialDesignThemes.Wpf;

namespace Bakery.WPF.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes < 0) return "—";
        if (bytes < 1024) return $"{bytes:N0} B";

        var size = bytes / 1024d;
        var unit = "KB";
        if (size >= 1024)
        {
            size /= 1024d;
            unit = "MB";
        }
        if (size >= 1024)
        {
            size /= 1024d;
            unit = "GB";
        }
        return $"{size:N1} {unit}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BooleanToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "نشط" : "غير نشط";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToActiveIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? PackIconKind.EyeOutline : PackIconKind.EyeOffOutline;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringToInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s.Trim()[0].ToString().ToUpper();
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}


public class BalanceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal balance)
        {
            if (balance > 0) return new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
            if (balance < 0) return new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red
        }
        return new SolidColorBrush(Color.FromRgb(117, 117, 117)); // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PartyTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PartyType type)
        {
            return type switch
            {
                PartyType.Customer => new SolidColorBrush(Color.FromRgb(21, 101, 192)), // Blue
                PartyType.Supplier => new SolidColorBrush(Color.FromRgb(239, 108, 0)),  // Orange
                PartyType.Employee => new SolidColorBrush(Color.FromRgb(46, 125, 50)),  // Green
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BalanceToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal balance)
        {
            string sign = balance > 0 ? "+" : "";
            return $"{sign}{balance:N0} ج.م";
        }
        return "0 ج.م";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PartyBalanceToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        PartyType partyType;
        decimal balance;

        if (value is Bakery.Application.DTOs.Accounting.PartyDto party)
        {
            partyType = party.Type;
            balance = party.Balance;
        }
        else if (value is Bakery.Application.DTOs.Accounting.PartySummaryDto summary)
        {
            partyType = summary.Type;
            balance = summary.CurrentBalance;
        }
        else
        {
            return "0.00 ج.م";
        }

        if (balance == 0) return "0.00 ج.م";

      
        decimal displayBalance = partyType == PartyType.Customer ? balance : -balance;

        string sign = displayBalance > 0 ? "+" : "";
        return $"{sign}{displayBalance:N2} ج.م";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PartyBalanceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        PartyType partyType;
        decimal balance;

        if (value is Bakery.Application.DTOs.Accounting.PartyDto party)
        {
            partyType = party.Type;
            balance = party.Balance;
        }
        else if (value is Bakery.Application.DTOs.Accounting.PartySummaryDto summary)
        {
            partyType = summary.Type;
            balance = summary.CurrentBalance;
        }
        else
        {
            return new SolidColorBrush(Color.FromRgb(117, 117, 117)); // Gray
        }

        if (balance == 0) return new SolidColorBrush(Color.FromRgb(117, 117, 117)); // Gray

        decimal displayBalance = partyType == PartyType.Customer ? balance : -balance;

        return displayBalance > 0 
            ? new SolidColorBrush(Color.FromRgb(46, 125, 50))  // Green (Money owed to bakery)
            : new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red (Money owed by bakery)
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}


public class FlexibleDecimalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
           
            return d.ToString("G29", culture);
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return 0m;

       
        
        string decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        string alternativeSeparator = decimalSeparator == "." ? "," : ".";
        
        if (s.EndsWith(decimalSeparator) || s.EndsWith(alternativeSeparator))
        {
            return Binding.DoNothing;
        }

        if (decimal.TryParse(s, NumberStyles.Any, culture, out decimal result))
        {
            return result;
        }

        string normalized = s.Replace(alternativeSeparator, decimalSeparator);
        if (decimal.TryParse(normalized, NumberStyles.Any, culture, out result))
        {
            return result;
        }

        return Binding.DoNothing; 
    }
}

public class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            
            var egCulture = new CultureInfo("ar-EG");
            return d.ToString("N0", egCulture) + " ج.م";
        }
        return "0 ج.م";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class NullableMovementAmountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var amount = value is decimal decimalValue ? decimalValue : 0m;

        return amount == 0 ? "—" : amount.ToString("N2", CultureInfo.GetCultureInfo("ar-EG"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v) return v == Visibility.Visible;
        return false;
    }
}

public class DateOnlyToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateOnly dateOnly)
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            return DateOnly.FromDateTime(dateTime);
        }
        return null;
    }
}
