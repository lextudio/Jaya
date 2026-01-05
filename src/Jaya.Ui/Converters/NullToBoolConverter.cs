using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool invert = false;
            if (parameter is string s && s == "invert")
                invert = true;

            bool result = value != null && !(value is string str && string.IsNullOrEmpty(str));
            return invert ? !result : result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
