//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia.Data.Converters;
using Jaya.Shared;
using Jaya.Shared.Models;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using System;
using System.Globalization;

namespace Jaya.Ui.Converters
{
    public class FileSystemObjectToFileNameConverter : IValueConverter
    {
        readonly SharedService? _shared;

        public FileSystemObjectToFileNameConverter()
        {
            _shared = ServiceLocator.Instance.GetService<SharedService>();
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            FileSystemObjectModel? fso = null;
            if (value is ExplorerItemModel item)
            {
                if (item.Object is FileSystemObjectModel model)
                    fso = model;
                else
                    return item.DisplayName ?? item.Label;
            }
            else
            {
                fso = value as FileSystemObjectModel;
            }

            if (fso == null)
                return null;

            switch (fso.Type)
            {
                case FileSystemObjectType.Drive:
                case FileSystemObjectType.Directory:
                    return fso.Name;

                case FileSystemObjectType.File:
                    var file = fso as FileModel;
                    if (file == null)
                        return null;

                    var config = _shared?.ApplicationConfiguration;
                    if (config != null && config.IsFileNameExtensionVisible && !string.IsNullOrWhiteSpace(file.Extension))
                        return string.Format("{0}.{1}", file.Name, file.Extension);

                    return file.Name;

                default:
                    return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
