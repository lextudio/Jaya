//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia.Data.Converters;
using Jaya.Shared.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    public class FileSystemObjectSizeToStringConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count < 2)
                return null;

            if (!(values[1] is FileSystemObjectType type))
                return null;

            if (type == FileSystemObjectType.Directory)
                return string.Empty;

            long size = 0L;
            if (values[0] is long l)
                size = l;
            else if (values[0] is int i)
                size = i;
            else if (values[0] is string s && long.TryParse(s, out var parsed))
                size = parsed;

            return FileSystemObjectModel.SizeSuffix(size, 2);
        }

        public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
