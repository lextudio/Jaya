//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Jaya.Ui;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using Jaya.Ui.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public class ExplorerViewModel: ViewModelBase
    {
        static readonly ILogger FileSystemLogger = Log.ForContext("Category", "FileSystem")
                                                       .ForContext("Area", "FileSystem");

        readonly Subscription<SelectionChangedEventArgs>? _onSelectionChanged;
        readonly SharedService? _shared;
        SelectionChangedEventArgs? _lastSelectionArgs;

        ICommand? _invokeObject;
        ICommand? _deleteItems;
        ICommand? _cutItems;
        ICommand? _copyItems;
        ICommand? _pasteItems;
        ProviderServiceBase? _service;
        AccountModelBase? _account;
        List<FileSystemObjectModel> _clipboardItems = new();
        TransferMode _clipboardMode = TransferMode.Copy;

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
            if (_onSelectionChanged != null)
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

                return _invokeObject!;
            }
        }

        public ICommand DeleteItemsCommand
        {
            get
            {
                if (_deleteItems == null)
                    _deleteItems = new RelayCommand<IReadOnlyList<ExplorerItemModel>>(DeleteItems, isAsynchronous: true);

                return _deleteItems!;
            }
        }

        public ICommand CutItemsCommand
        {
            get
            {
                if (_cutItems == null)
                    _cutItems = new RelayCommand<IReadOnlyList<ExplorerItemModel>>(CutItems);

                return _cutItems!;
            }
        }

        public ICommand CopyItemsCommand
        {
            get
            {
                if (_copyItems == null)
                    _copyItems = new RelayCommand<IReadOnlyList<ExplorerItemModel>>(CopyItems);

                return _copyItems!;
            }
        }

        public ICommand PasteItemsCommand
        {
            get
            {
                if (_pasteItems == null)
                    _pasteItems = new RelayCommand(PasteItems);

                return _pasteItems!;
            }
        }

        public ApplicationConfigModel ApplicationConfig => _shared!.ApplicationConfiguration;

        public PaneConfigModel PaneConfig => _shared!.PaneConfiguration;

        public ExplorerItemModel? Item
        {
            get => Get<ExplorerItemModel?>();
            private set => Set(value);
        }

        #endregion

        void InvokeObject(ExplorerItemModel? obj)
        {
            if (obj == null)
                return;
            if (!obj.Type.HasValue)
                return;

            IsBusy = true;

            DirectoryModel? directory = null;
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
            EventAggregator?.Publish(eventArgs);
        }

        void DeleteItems(IReadOnlyList<ExplorerItemModel> items)
        {
            if (items == null || items.Count == 0)
                return;

            FileSystemLogger.Debug("DeleteItems invoked: Count={Count}, Service={Service}, Account={Account}",
                items.Count,
                _service?.Name ?? "<null>",
                _account?.Name ?? "<null>");

            if (_service == null || _account == null)
            {
                FileSystemLogger.Debug("Delete skipped: missing service/account context.");
                return;
            }

            if (_service is not IFileDeleteService deleteService)
            {
                FileSystemLogger.Warning("Delete requested but service does not support delete.");
                return;
            }

            var targets = items
                .Where(item => item != null && (item.IsFile || item.IsDirectory))
                .Select(item => item.Object)
                .OfType<FileSystemObjectModel>()
                .Where(obj => !string.IsNullOrWhiteSpace(obj.Path))
                .ToList();

            if (targets.Count == 0)
            {
                FileSystemLogger.Debug("Delete skipped: no file or directory targets in selection.");
                return;
            }

            try
            {
                var deleted = deleteService.DeleteAsync(_account, targets, DeleteMode.Trash).GetAwaiter().GetResult();
                FileSystemLogger.Information("Delete requested for {Count} items (anyDeleted={AnyDeleted})", targets.Count, deleted);
                if (deleted)
                {
                    Invoke(() => RemoveItemsFromView(targets));
                }
            }
            catch (Exception ex)
            {
                FileSystemLogger.Error(ex, "Failed to delete {Count} items", targets.Count);
            }
        }

        void CutItems(IReadOnlyList<ExplorerItemModel> items)
        {
            StoreClipboard(items, TransferMode.Move);
        }

        void CopyItems(IReadOnlyList<ExplorerItemModel> items)
        {
            StoreClipboard(items, TransferMode.Copy);
        }

        async void PasteItems()
        {
            if (_clipboardItems == null || _clipboardItems.Count == 0)
            {
                FileSystemLogger.Debug("Paste skipped: clipboard is empty.");
                return;
            }

            if (_service == null || _account == null)
            {
                FileSystemLogger.Debug("Paste skipped: missing service/account context.");
                return;
            }

            if (_service is not IFileTransferService transferService)
            {
                FileSystemLogger.Warning("Paste requested but service does not support transfer.");
                return;
            }

            var targetDirectory = Item?.Object as DirectoryModel;
            if (targetDirectory == null || string.IsNullOrWhiteSpace(targetDirectory.Path))
            {
                FileSystemLogger.Debug("Paste skipped: no target directory.");
                return;
            }

            try
            {
                var cancellation = new System.Threading.CancellationTokenSource();
                var progressWindow = new TransferProgressView();
                var progressViewModel = progressWindow.DataContext as TransferProgressViewModel;
                if (progressViewModel != null)
                {
                    progressViewModel.AttachCancellation(cancellation);
                    var verb = _clipboardMode == TransferMode.Move ? "Moving items" : "Copying items";
                    progressViewModel.Title = verb;
                    progressViewModel.HeaderText = $"{verb}...";
                    progressViewModel.StatusText = "Preparing items...";
                    progressViewModel.TargetPath = targetDirectory.Path;
                }

                var owner = App.Lifetime?.MainWindow;
                if (owner != null)
                    progressWindow.Show(owner);
                else
                    progressWindow.Show();

                var progress = new Progress<TransferProgressReport>(report =>
                {
                    if (progressViewModel != null)
                        progressViewModel.Update(report);
                });

                var createdItems = await transferService.TransferAsync(
                    _account,
                    _clipboardItems,
                    targetDirectory,
                    _clipboardMode,
                    progress,
                    cancellation.Token);
                FileSystemLogger.Information("Paste requested for {Count} items (created={Created})", _clipboardItems.Count, createdItems.Count);

                if (createdItems.Count > 0)
                {
                    Invoke(() => AddItemsToView(createdItems, selectAdded: true));
                }

                if (createdItems.Count > 0 && _clipboardMode == TransferMode.Move)
                    _clipboardItems.Clear();
            }
            catch (Exception ex)
            {
                FileSystemLogger.Error(ex, "Failed to paste {Count} items", _clipboardItems.Count);
            }
        }

        void StoreClipboard(IReadOnlyList<ExplorerItemModel> items, TransferMode mode)
        {
            if (items == null || items.Count == 0)
                return;

            var targets = items
                .Where(item => item != null && (item.IsFile || item.IsDirectory))
                .Select(item => item.Object)
                .OfType<FileSystemObjectModel>()
                .Where(obj => !string.IsNullOrWhiteSpace(obj.Path))
                .ToList();

            if (targets.Count == 0)
            {
                FileSystemLogger.Debug("Clipboard skipped: no file or directory targets in selection.");
                return;
            }

            _clipboardItems = targets;
            _clipboardMode = mode;
            FileSystemLogger.Debug("Clipboard stored {Count} items with mode={Mode}.", targets.Count, mode);
        }

        void AddItemsToView(IReadOnlyCollection<FileSystemObjectModel> createdItems, bool selectAdded)
        {
            if (createdItems == null || createdItems.Count == 0)
                return;

            if (Item?.Children == null)
                return;

            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var existingPaths = new HashSet<string>(
                Item.Children
                    .Select(child => (child.Object as FileSystemObjectModel)?.Path ?? string.Empty)
                    .Where(path => !string.IsNullOrWhiteSpace(path)),
                comparer);
            var addedPaths = selectAdded ? new List<string>() : null;

            foreach (var created in createdItems)
            {
                if (string.IsNullOrWhiteSpace(created.Path) || existingPaths.Contains(created.Path))
                    continue;

                var itemType = created.Type switch
                {
                    FileSystemObjectType.Directory => ItemType.Directory,
                    FileSystemObjectType.Drive => ItemType.Drive,
                    _ => ItemType.File
                };

                var label = created switch
                {
                    FileModel file => file.Name,
                    DirectoryModel directory => directory.Name,
                    _ => created.Name
                };

                Item.Children.Add(new ExplorerItemModel(itemType, label, created));
                existingPaths.Add(created.Path);
                addedPaths?.Add(created.Path);
            }

            if (selectAdded && addedPaths != null && addedPaths.Count > 0)
            {
                EventAggregator?.Publish(new SelectItemsRequestedEventArgs(addedPaths));
            }
        }

        void RemoveItemsFromView(IReadOnlyCollection<FileSystemObjectModel> targets)
        {
            if (targets == null || targets.Count == 0)
                return;

            if (Item?.Children == null)
                return;

            var removePaths = new HashSet<string>(targets.Select(target => target.Path), StringComparer.Ordinal);
            var toRemove = Item.Children
                .Where(child => child.Object is FileSystemObjectModel fso && !string.IsNullOrWhiteSpace(fso.Path) && removePaths.Contains(fso.Path))
                .ToList();

            foreach (var child in toRemove)
                Item.Children.Remove(child);

            if (toRemove.Count == 0)
            {
                FileSystemLogger.Debug("Delete succeeded but no matching items found in view.");
            }
            else
            {
                FileSystemLogger.Debug("Removed {Count} items from view after delete.", toRemove.Count);
            }
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

            if (args == null)
            {
                IsBusy = false;
                return;
            }

            _service = args.Service;
            _account = args.Account;

            if (_service == null)
            {
                // nothing to do if no service is provided
                IsBusy = false;
                return;
            }

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
            else if (args.Directory != null && _account != null)
            {
                var directory = await _service.GetDirectoryAsync(_account, args.Directory);
                if (directory == null)
                {
                    // empty directory or failed to load
                    Item = new ExplorerItemModel(ItemType.Directory, args.Directory.Path ?? "", new DirectoryModel());
                    IsBusy = false;
                    return;
                }

                var dirType = (directory.Type == FileSystemObjectType.Drive) ? ItemType.Drive : ItemType.Directory;
                var directoryItem = new ExplorerItemModel(dirType, directory.Name ?? string.Empty, directory);

                if (directory.Directories != null)
                {
                    foreach (var subDirectory in directory.Directories)
                    {
                        await Task.Run(new Action(() =>
                        {
                            var subDirectoryItem = new ExplorerItemModel(subDirectory.Type == FileSystemObjectType.Drive ? ItemType.Drive : ItemType.Directory, subDirectory.Name, subDirectory);
                            directoryItem.Children.Add(subDirectoryItem);
                        }));
                    }
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

        void ApplicationConfiguration_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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

        void LogDisplayedItems(string context, ExplorerItemModel? root)
        {
            if (root?.Children == null)
                return;

            FileSystemLogger.Information("Displaying {Count} items for {Context}", root.Children.Count, context);

            foreach (var child in root.Children)
            {
                var label = child.Label;
                string? path = null;
                string? id = null;

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

                FileSystemLogger.Verbose(
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
