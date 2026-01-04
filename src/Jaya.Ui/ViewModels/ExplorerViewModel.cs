//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public class ExplorerViewModel: ViewModelBase
    {
        static readonly ILogger FileSystemLogger = Log.ForContext("Category", "FileSystem")
                                                       .ForContext("Area", "FileSystem");

        readonly Subscription<SelectionChangedEventArgs> _onSelectionChanged;
        readonly SharedService _shared;
        SelectionChangedEventArgs _lastSelectionArgs;

        ICommand _invokeObject;
        ProviderServiceBase _service;
        AccountModelBase _account;

        public ExplorerViewModel()
        {
            _shared = GetService<SharedService>();
            _onSelectionChanged = EventAggregator?.Subscribe<SelectionChangedEventArgs>(SelectionChanged);
            if (_shared?.ApplicationConfiguration != null)
            {
                _shared.ApplicationConfiguration.PropertyChanged += ApplicationConfiguration_PropertyChanged;
            }
        }

        ~ExplorerViewModel()
        {
            EventAggregator?.UnSubscribe(_onSelectionChanged);
            if (_shared?.ApplicationConfiguration != null)
                _shared.ApplicationConfiguration.PropertyChanged -= ApplicationConfiguration_PropertyChanged;
        }

        #region properties

        public ICommand InvokeObjectCommand
        {
            get
            {
                if (_invokeObject == null)
                    _invokeObject = new RelayCommand<ExplorerItemModel>(InvokeObject);

                return _invokeObject;
            }
        }

        public ApplicationConfigModel ApplicationConfig => _shared.ApplicationConfiguration;

        public PaneConfigModel PaneConfig => _shared.PaneConfiguration;

        public ExplorerItemModel Item
        {
            get => Get<ExplorerItemModel>();
            private set => Set(value);
        }

        #endregion

        void InvokeObject(ExplorerItemModel obj)
        {
            if (!obj.Type.HasValue)
                return;

            IsBusy = true;

            DirectoryModel directory = null;
            switch (obj.Type.Value)
            {
                case ItemType.Drive:
                case ItemType.Directory:
                    directory = obj.Object as DirectoryModel;
                    break;

                case ItemType.File:
                {
                    var file = obj.Object as FileModel;
                    var path = file?.Path;

                    FileSystemLogger.Information("File activated: {Label} path={Path}", obj.Label, path ?? "<unknown>");

                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            OpenFile(path);
                            FileSystemLogger.Information("Launched file: {Path}", path);
                        }
                        catch (Exception ex)
                        {
                            FileSystemLogger.Error(ex, "Failed to open file: {Path}", path);
                        }
                    }

                    IsBusy = false;
                    return;
                }

                case ItemType.Computer:
                    _account = obj.Object as AccountModelBase;
                    // Use an empty DirectoryModel as the root placeholder so SelectionChanged
                    // treats this the same way as account nodes in the tree.
                    directory = new DirectoryModel();
                    break;
                case ItemType.Account:
                    _account = obj.Object as AccountModelBase;
                    // Use an empty DirectoryModel as the root placeholder so SelectionChanged
                    // knows to load the account root directory.
                    directory = new DirectoryModel();
                    break;

                case ItemType.Service:
                    _service = obj.Object as ProviderServiceBase;
                    _account = null;
                    break;
            }

            IsBusy = false;

            var eventArgs = new SelectionChangedEventArgs(_service, _account, directory);
            EventAggregator.Publish(eventArgs);
        }

        static void OpenFile(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                // Use cmd /c start to open with default app
                var psi = new ProcessStartInfo("cmd", $"/c start \"\" \"{path}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(psi);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open", $"\"{path}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(psi);
            }
            else
            {
                // Assume linux/unix
                var psi = new ProcessStartInfo("xdg-open", $"\"{path}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(psi);
            }
        }

        async void SelectionChanged(SelectionChangedEventArgs args)
        {
            // remember so we can refresh on configuration changes
            _lastSelectionArgs = args;

            Item = null;
            IsBusy = true;

            
            _service = args.Service;
            _account = args.Account;

            if (_account == null)
            {
                var accounts = await _service.GetAccountsAsync();
                var serviceItem = new ExplorerItemModel(ItemType.Service, _service.Name, _service.ImagePath);

                foreach (var account in accounts)
                {
                    await Task.Run(new Action(() =>
                    {
                        var accountItem = new ExplorerItemModel(_service.IsRootDrive ? ItemType.Computer : ItemType.Account, account.Name, account);
                        serviceItem.Children.Add(accountItem);
                    }));
                }

                Item = serviceItem;
                LogDisplayedItems($"Service {_service.Name}", serviceItem);
            }
            else if (args.Directory != null)
            {
                var directory = await args.Service.GetDirectoryAsync(args.Account, args.Directory);
                var directoryItem = new ExplorerItemModel(directory.Type == FileSystemObjectType.Drive ? ItemType.Drive : ItemType.Directory, directory.Name, directory);

                foreach (var subDirectory in directory.Directories)
                {
                    await Task.Run(new Action(() =>
                    {
                        var subDirectoryItem = new ExplorerItemModel(subDirectory.Type == FileSystemObjectType.Drive ? ItemType.Drive : ItemType.Directory, subDirectory.Name, subDirectory);
                        directoryItem.Children.Add(subDirectoryItem);
                    }));
                }

                if (directory.Files != null)
                {
                    foreach (var file in directory.Files)
                    {
                        await Task.Run(new Action(() =>
                        {
                            var fileItem = new ExplorerItemModel(ItemType.File, file.Name, file);
                            directoryItem.Children.Add(fileItem);
                        }));
                    }
                }

                Item = directoryItem;
                LogDisplayedItems(directory.Path ?? directory.Name ?? "Directory", directoryItem);
            }

            IsBusy = false;
        }

        void ApplicationConfiguration_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.PropertyName))
                return;

            if (e.PropertyName == nameof(ApplicationConfigModel.IsFileNameExtensionVisible) ||
                e.PropertyName == nameof(ApplicationConfigModel.IsHiddenItemVisible))
            {
                // Re-run the last selection to refresh displayed items
                if (_lastSelectionArgs != null)
                {
                    try
                    {
                        Invoke(() => SelectionChanged(_lastSelectionArgs));
                    }
                    catch { }
                }
            }
        }

        void LogDisplayedItems(string context, ExplorerItemModel root)
        {
            if (root?.Children == null)
                return;

            FileSystemLogger.Information("Displaying {Count} items for {Context}", root.Children.Count, context);

            foreach (var child in root.Children)
            {
                var label = child.Label;
                string path = null;
                string id = null;

                switch (child.Object)
                {
                    case DirectoryModel directory:
                        label ??= directory.Name;
                        path ??= directory.Path;
                        id ??= directory.Id;
                        break;
                    case FileModel file:
                        label ??= file.Name;
                        path ??= file.Path;
                        id ??= file.Id;
                        break;
                    case AccountModelBase account:
                        label ??= account.Name;
                        id ??= account.Id;
                        break;
                }

                if (string.IsNullOrWhiteSpace(label))
                    label = child.Type?.ToString() ?? "Unnamed";

                var objectType = child.Object?.GetType().Name ?? "<none>";
                var fileModel = child.Object as FileModel;
                var fsObject = child.Object as FileSystemObjectModel;
                var extension = fileModel?.Extension;
                var size = fsObject?.SizeString;

                path ??= fsObject?.Path;
                id ??= fsObject?.Id;

                FileSystemLogger.Debug(
                    "Displayed item {Label} as {DisplayName} (ItemType={ItemType}, ObjectType={ObjectType}, Extension={Extension}, Size={Size}) path={Path} id={Id} under {Context}",
                    label,
                    child.DisplayName,
                    child.Type,
                    objectType,
                    string.IsNullOrEmpty(extension) ? "<none>" : extension,
                    string.IsNullOrEmpty(size) ? "<unknown>" : size,
                    path ?? "<unknown>",
                    id ?? "<unknown>",
                    context);
            }
        }
    }
}
