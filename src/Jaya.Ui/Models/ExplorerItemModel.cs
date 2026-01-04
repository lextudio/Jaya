//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using System;
using System.Collections.ObjectModel;

namespace Jaya.Ui.Models
{
    public class ExplorerItemModel: ModelBase
    {
        public ExplorerItemModel(ItemType? type, string label, object obj, string imagePath = null)
        {
            Type = type;
            Label = label;
            Children = new ObservableCollection<ExplorerItemModel>();
            Object = obj;
            ImagePath = imagePath;

            // Compute a display name that matches what the UI shows: prefer explicit label,
            // otherwise use the underlying object's name and include extension for files.
            string displayName = label;
            if (Object is FileModel fileModel)
            {
                var baseName = !string.IsNullOrWhiteSpace(label) && label != "File" ? label : fileModel.Name;
                if (!string.IsNullOrEmpty(fileModel.Extension))
                {
                    displayName = string.IsNullOrWhiteSpace(baseName)
                        ? $".{fileModel.Extension}"
                        : $"{baseName}.{fileModel.Extension}";
                }
                else
                {
                    displayName = string.IsNullOrWhiteSpace(baseName) ? fileModel.Name : baseName;
                }
            }
            else if (Object is DirectoryModel dirModel)
            {
                displayName = string.IsNullOrWhiteSpace(label) ? dirModel.Name : label;
            }

            DisplayName = displayName;
        }

        #region properties

        internal ItemType? Type { get; }

        public object Object { get; }

        public bool IsDummy => Type == ItemType.Dummy;

        public bool IsService => Type == ItemType.Service;

        public bool IsDrive => Type == ItemType.Drive;

        public bool IsDirectory => Type == ItemType.Directory;

        public bool IsAccount => Type == ItemType.Account;

        public bool IsComputer => Type == ItemType.Computer;

        public bool IsFile => Type == ItemType.File;

        public bool IsHavingMetaData => IsAccount || IsDrive || IsDirectory;

        public string Label
        {
            get => Get<string>();
            set => Set(value);
        }

        public string DisplayName
        {
            get => Get<string>();
            set => Set(value);
        }

        public string ImagePath
        {
            get => Get<string>();
            set => Set(value);
        }

        public ObservableCollection<ExplorerItemModel> Children { get; }

        public string AccessErrorMessage => (Object as DirectoryModel)?.AccessErrorMessage;

        public bool HasAccessError => !string.IsNullOrWhiteSpace(AccessErrorMessage);

        // Numeric size used for sorting. 0 if unknown/not a file.
        public long SizeSort
        {
            get
            {
                if (Object is FileModel file)
                    return file.Size!.Value;
                return 0;
            }
        }

        #endregion
    }
}
