//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Jaya.Shared;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using System;
using System.Globalization;
using System.IO;

namespace Jaya.Ui.Converters
{
    public class FileSystemObjectToImageConverter : IValueConverter
    {
        readonly MemoryCacheService? _cache;

        public FileSystemObjectToImageConverter()
        {
            _cache = ServiceLocator.Instance.GetService<MemoryCacheService>();
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var fso = value as FileSystemObjectModel;
            if (fso == null)
                return null;

            var iconSize = 48;
            if (parameter is string p && int.TryParse(p, out var parsedSize))
                iconSize = parsedSize;

            Uri uri;
            switch (fso.Type)
            {
                case FileSystemObjectType.Drive:
                    uri = new Uri(Constants.GetImageUrl(string.Format("Hdd-{0}.png", iconSize)), UriKind.RelativeOrAbsolute);
                    break;

                case FileSystemObjectType.Directory:
                    uri = new Uri(Constants.GetImageUrl(string.Format("Folder-{0}.png", iconSize)), UriKind.RelativeOrAbsolute);
                    break;

                case FileSystemObjectType.File:
                    return GetFileImage(fso as FileModel, iconSize);

                default:
                    return null;
            }

                try
                {
                    if (uri == null)
                        return null;

                    return new Bitmap(uri.ToString());
                }
                catch
                {
                    return null;
                }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        Bitmap? AddOrGetFromCache(Uri uri, Uri? fallbackUri = null)
        {
            if (uri == null)
                return null;

            if (_cache != null && _cache.TryGetValue(uri, out Bitmap cachedImage))
                return cachedImage;

            Bitmap? image = null;

            try
            {
                image = new Bitmap(uri.ToString());
                _cache?.Set(uri, image);
                return image;
            }
            catch (Exception)
            {
                if (fallbackUri == null)
                    return null;

                if (_cache != null && _cache.TryGetValue(fallbackUri, out Bitmap cachedFallback))
                {
                    _cache?.Set(uri, cachedFallback);
                    return cachedFallback;
                }

                image = new Bitmap(fallbackUri.ToString());
                _cache?.Set(fallbackUri, image);
                return image;
            }
        }

        Bitmap? GetFileImage(FileModel fso, int iconSize)
        {
            var fallbackUri = new Uri(Constants.GetImageUrl(string.Format("File-{0}.png", iconSize)), UriKind.RelativeOrAbsolute);

            if (string.IsNullOrEmpty(fso.Extension))
                return AddOrGetFromCache(fallbackUri);

            var extensionImageFile = Constants.GetImageUrl(string.Format("FileExtensions/{0}-{1}.png", fso.Extension, iconSize));
            var uri = new Uri(extensionImageFile, UriKind.RelativeOrAbsolute);

            return AddOrGetFromCache(uri, fallbackUri);
        }
    }
}
