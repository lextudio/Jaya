using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    public class BooleanToOpacityConverter : IValueConverter
    {
        public double HiddenOpacity { get; set; } = 0.55;

        public double VisibleOpacity { get; set; } = 1.0;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return HiddenOpacity;

            return VisibleOpacity;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
