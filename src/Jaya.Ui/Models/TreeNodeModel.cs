//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Ui.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Jaya.Ui.Models
{
    public class TreeNodeModel : ModelBase
    {
        TreeNodeModel? _dummyChild;
        readonly SharedService? _shared;

        public delegate void TreeNodeExpanded(TreeNodeModel node, bool isExpaded);
        public event TreeNodeExpanded? NodeExpanded;

        public TreeNodeModel(ProviderServiceBase? service, AccountModelBase? account, ItemType? nodeType)
        {
            Service = service;
            Account = account;
            Children = new ObservableCollection<TreeNodeModel>();
            NodeType = nodeType;

            if (Service != null && Account != null)
            {
                _shared = ServiceLocator.Instance.GetService<SharedService>();
                if (_shared?.ApplicationConfiguration != null)
                    _shared.ApplicationConfiguration.PropertyChanged += OnApplicationConfigChanged;
            }
        }

        ~TreeNodeModel()
        {
            if (_shared?.ApplicationConfiguration != null)
                _shared.ApplicationConfiguration.PropertyChanged -= OnApplicationConfigChanged;
        }

        #region properties

        internal ItemType? NodeType { get; }

        public bool IsDummy => NodeType == ItemType.Dummy;

        public bool IsService => NodeType == ItemType.Service;

        public bool IsDrive => NodeType == ItemType.Drive;

        public bool IsDirectory => NodeType == ItemType.Directory;

        public bool IsAccount => NodeType == ItemType.Account;

        public bool IsComputer => NodeType == ItemType.Computer;

        public ProviderServiceBase? Service { get; }

        public AccountModelBase? Account { get; }

        public string Label
        {
            get => Get<string>();
            set => Set(value);
        }

        public string ImagePath
        {
            get => Get<string>();
            set => Set(value);
        }

        public bool IsExpanded
        {
            get => Get<bool>();
            set
            {
                Set(value);

                var handler = NodeExpanded;
                if (handler != null)
                    handler.Invoke(this, value);
            }
        }

        public bool IsHavingDummyChild => _dummyChild != null;

        public ObservableCollection<TreeNodeModel> Children { get; }

        public FileSystemObjectModel? FileSystemObject
        {
            get => Get<FileSystemObjectModel?>();
            set
            {
                if (Set(value))
                {
                    RaisePropertyChanged(nameof(IsDriveIconVisible));
                    RaisePropertyChanged(nameof(IsExternalDriveIconVisible));
                }
            }
        }

        public bool IsDriveIconVisible => IsDrive && !NodeRepresentsExternalDrive();

        public bool IsExternalDriveIconVisible => IsDrive && NodeRepresentsExternalDrive();

        bool NodeRepresentsExternalDrive()
        {
            if (FileSystemObject is DirectoryModel directoryModel)
                return directoryModel.IsExternalDrive;

            return false;
        }

        private void OnApplicationConfigChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ApplicationConfigModel.IsHiddenItemVisible):
                    if (FileSystemObject != null)
                        FileSystemObject.RaisePropertyChanged(nameof(FileSystemObject.IsHidden));
                    break;
            }
        }

        #endregion

        public override bool Equals(object? obj)
        {
            var compareWith = obj as TreeNodeModel;
            if (compareWith == null)
                return false;

            return GetHashCode() == compareWith.GetHashCode();
        }

        public override int GetHashCode()
        {
            return new { Service, Account, FileSystemObject, IsDummy }.GetHashCode();
        }

        public void AddDummyChild()
        {
            if (IsHavingDummyChild)
                return;

            var child = _dummyChild = new TreeNodeModel(Service, Account, ItemType.Dummy);
            child.Label = "Loading...";
            Children.Add(child);
        }

        public void RemoveDummyChild()
        {
            if (!IsHavingDummyChild)
                return;

            if (_dummyChild != null)
            {
                Children.Remove(_dummyChild);
                _dummyChild = null;
            }
        }

        public override string ToString()
        {
            string result = base.ToString() ?? "";

            if (!string.IsNullOrEmpty(Label))
                result = Label;
            else if (Account != null)
                result = Account.Name ?? result;
            else if (FileSystemObject != null && !string.IsNullOrEmpty(FileSystemObject.Name))
                result = FileSystemObject.Name ?? result;
            else if (NodeType != null)
                result = NodeType.ToString() ?? result;

            return result;
        }
    }
}
