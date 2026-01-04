using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    public class BooleanInverseConverter : IValueConverter
    {
        public static readonly BooleanInverseConverter Instance = new BooleanInverseConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return true;
        }
    }
}
