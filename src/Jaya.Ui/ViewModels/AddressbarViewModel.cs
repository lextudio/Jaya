//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Ui.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public class AddressbarViewModel : ViewModelBase
    {
        readonly NavigationService? _navigationService;
        readonly Subscription<SelectionChangedEventArgs>? _onSelectionChanged;
        readonly char[] _pathSeparator;
        ICommand? _clearSearch, _search;
        ICommand? _enterEditMode, _commitAddress, _cancelEdit;
        System.Collections.ObjectModel.ObservableCollection<string> _history = new System.Collections.ObjectModel.ObservableCollection<string>();
        bool _isInEditMode;
        string? _addressText = string.Empty;
        ItemType? _nodeType;

        public AddressbarViewModel()
        {
            _pathSeparator = new char[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            };
            _navigationService = GetService<NavigationService>();
            _onSelectionChanged = EventAggregator?.Subscribe<SelectionChangedEventArgs>(SelectionChanged);

            SearchQuery = string.Empty;
            SearchWatermark = "Search";
            History = new System.Collections.ObjectModel.ObservableCollection<string>();
        }

        ~AddressbarViewModel()
        {
            if (_onSelectionChanged != null)
                EventAggregator?.UnSubscribe(_onSelectionChanged);
        }

        #region properties

        public ICommand SearchCommand
        {
            get
            {
                if (_search == null)
                    _search = new RelayCommand<string>(SearchAction);

                return _search;
            }
        }

        public ICommand ClearSearchCommand
        {
            get
            {
                if (_clearSearch == null)
                    _clearSearch = new RelayCommand(ClearSearch);

                return _clearSearch;
            }
        }

        public ICommand EnterEditModeCommand => _enterEditMode ??= new RelayCommand(EnterEditMode);

        public ICommand CommitAddressCommand => _commitAddress ??= new RelayCommand(CommitAddress);

        public ICommand CancelEditCommand => _cancelEdit ??= new RelayCommand(CancelEdit);

        public System.Collections.ObjectModel.ObservableCollection<string> History
        {
            get => _history ??= new System.Collections.ObjectModel.ObservableCollection<string>();
            private set => _history = value;
        }

        public bool IsInEditMode
        {
            get => _isInEditMode;
            set => Set(ref _isInEditMode, value);
        }

        public string AddressText
        {
            get => _addressText ?? string.Empty;
            set => Set(ref _addressText, value ?? string.Empty);
        }

        ItemType? NodeType
        {
            get => _nodeType;
            set
            {
                if (value == _nodeType)
                    return;

                var oldValue = _nodeType;
                _nodeType = value;

                TriggerFileSystemObjectTypeChanged(oldValue);
                TriggerFileSystemObjectTypeChanged(value);
            }
        }

        public bool IsService => NodeType == ItemType.Service;

        public bool IsDrive => NodeType == ItemType.Drive;

        public bool IsDirectory => NodeType == ItemType.Directory;

        public bool IsAccount => NodeType == ItemType.Account;

        public bool IsComputer => NodeType == ItemType.Computer;

        public ICommand? NavigateBackCommand => _navigationService?.NavigateBackCommand;

        public ICommand? NavigateForwardCommand => _navigationService?.NavigateForwardCommand;

        public string SearchQuery
        {
            get => Get<string>() ?? string.Empty;
            set => Set(value);
        }

        public string ImagePath
        {
            get => Get<string>() ?? string.Empty;
            private set => Set(value);
        }

        public List<string> PathParts
        {
            get => Get<List<string>>() ?? new List<string>();
            private set => Set(value);
        }

        public string SearchWatermark
        {
            get => Get<string>() ?? string.Empty;
            private set => Set(value);
        }

        #endregion

        void TriggerFileSystemObjectTypeChanged(ItemType? type)
        {
            switch (type)
            {
                case ItemType.Service:
                    RaisePropertyChanged(nameof(IsService));
                    break;

                case ItemType.Account:
                    RaisePropertyChanged(nameof(IsAccount));
                    break;

                case ItemType.Computer:
                    RaisePropertyChanged(nameof(IsComputer));
                    break;

                case ItemType.Drive:
                    RaisePropertyChanged(nameof(IsDrive));
                    break;

                case ItemType.Directory:
                    RaisePropertyChanged(nameof(IsDirectory));
                    break;
            }
        }

        void SearchAction(string searchQuery)
        {

        }

        void ClearSearch()
        {
            SearchQuery = string.Empty;
        }

        void SelectionChanged(SelectionChangedEventArgs args)
        {
            var service = args.Service;
            var account = args.Account;
            var directory = args.Directory;

            if (service == null)
            {
                // Nothing to display reliably
                PathParts = new List<string>();
                SearchWatermark = "Search";
                ImagePath = string.Empty;
                NodeType = null;
                return;
            }

            var pathParts = new List<string> { service.Name ?? string.Empty };
            if (account == null)
            {
                SearchWatermark = string.Format("Search {0}", service.Name);
                ImagePath = service.ImagePath;
                NodeType = ItemType.Service;
            }
            else if (directory == null || string.IsNullOrEmpty(directory.Path))
            {
                pathParts.Add(account.Name ?? string.Empty);

                SearchWatermark = string.Format("Search {0}", account.Name);
                ImagePath = account.ImagePath;
                NodeType = service.IsRootDrive ? ItemType.Computer : ItemType.Account;
            }
            else
            {
                SearchWatermark = string.Format("Search {0}", directory.Name);
                if (directory.Type == FileSystemObjectType.Drive)
                    NodeType = ItemType.Drive;
                else
                    NodeType = ItemType.Directory;

                pathParts.AddRange((directory.Path ?? string.Empty).Split(_pathSeparator, StringSplitOptions.RemoveEmptyEntries));
            }

            PathParts = pathParts;
            if (!IsInEditMode)
            {
                AddressText = string.Join(Path.DirectorySeparatorChar.ToString(), PathParts ?? new List<string>());
            }
        }

        void EnterEditMode()
        {
            IsInEditMode = true;
            AddressText = string.Join(Path.DirectorySeparatorChar.ToString(), PathParts ?? new System.Collections.Generic.List<string>());
        }

        void CommitAddress()
        {
            if (string.IsNullOrWhiteSpace(AddressText))
            {
                IsInEditMode = false;
                return;
            }

            try
            {
                // publish a direct path navigation request so higher-level services can handle it
                EventAggregator?.Publish(new DirectPathNavigationRequest(AddressText));

                // add to history
                if (!History.Contains(AddressText))
                    History.Insert(0, AddressText);
            }
            catch (Exception)
            {
                // swallow for now; navigation service may display errors
            }
            finally
            {
                IsInEditMode = false;
            }
        }

        void CancelEdit()
        {
            IsInEditMode = false;
            AddressText = string.Join(Path.DirectorySeparatorChar.ToString(), PathParts ?? new System.Collections.Generic.List<string>());
        }
    }
}
