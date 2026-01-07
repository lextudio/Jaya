using Avalonia.Data.Converters;
using Serilog;
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

        private static readonly ILogger Log = Serilog.Log.ForContext(typeof(EnumToBoolConverter)).ForContext("SourceContext", "Views");

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value == null || parameter == null)
                {
                    Log.Debug("Convert called with null value or parameter (value={Value}, parameter={Parameter})", value, parameter);
                    return false;
                }

                // Compare the enum value with the parameter
                // parameter is a string like "FolderType", "DriveType", etc.
                // value is the LocationIconType enum value
                var result = value.ToString() == parameter.ToString();
                Log.Debug("Convert: value={Value}, parameter={Parameter}, result={Result}", value, parameter, result);
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error converting enum to bool (value={Value}, parameter={Parameter})", value, parameter);
                return false;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Log.Verbose("ConvertBack called but not implemented (value={Value}, targetType={TargetType})", value, targetType);
            throw new NotImplementedException();
        }
    }
}
