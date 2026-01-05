using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia;
using System;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    public class ResourceValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            if (value is string key)
            {
                try
                {
                    var dict = Application.Current?.Resources;
                    if (dict != null)
                    {
                        if (dict.TryGetResource((object)key, (Avalonia.Styling.ThemeVariant?)null, out var found))
                        {
                            try { Console.WriteLine($"ResourceValueConverter: key={key} resolved type={(found != null ? found.GetType().FullName : "null")}\n"); } catch { }
                            return found;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
