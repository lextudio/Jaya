using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    /// <summary>
    /// Converts an enum value to a boolean indicating if it matches the provided parameter.
    /// Used for visibility binding in icon templates where IconType should match the desired icon.
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public static readonly EnumToBoolConverter Instance = new EnumToBoolConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            // Compare the enum value with the parameter
            // parameter is a string like "FolderType", "DriveType", etc.
            // value is the LocationIconType enum value
            return value.ToString() == parameter.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
