using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Jaya.Ui.Models;

namespace Jaya.Ui.Converters
{
    public class TreeNodeLabelConverter : IValueConverter
    {
        public static readonly TreeNodeLabelConverter Instance = new TreeNodeLabelConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var node = value as TreeNodeModel;
            if (node == null)
                return value?.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(node.Label))
                return node.Label;

            if (node.Account != null && !string.IsNullOrEmpty(node.Account.Name))
                return node.Account.Name;

            if (node.FileSystemObject != null && !string.IsNullOrEmpty(node.FileSystemObject.Name))
                return node.FileSystemObject.Name;

            if (node.NodeType != null)
                return node.NodeType.ToString();

            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
