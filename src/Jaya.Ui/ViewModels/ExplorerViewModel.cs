//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Avalonia.Threading;
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
    public class ExplorerViewModel : ViewModelBase
    {
        static readonly ILogger FileSystemLogger = Log.ForContext("Category", "FileSystem")
                                                       .ForContext("Area", "FileSystem");

        readonly Subscription<SelectionChangedEventArgs>? _onSelectionChanged;
        readonly Subscription<NewFolderRequestedEventArgs>? _onNewFolder;
        readonly SharedService? _shared;
        SelectionChangedEventArgs? _lastSelectionArgs;

        ICommand? _invokeObject;
        ICommand? _renameItem;
        ICommand? _commitRename;
        ICommand? _cancelRename;
        ICommand? _deleteItems;
        ICommand? _cutItems;
        ICommand? _copyItems;
        ICommand? _pasteItems;
        ICommand? _openTerminalCommand;
        ProviderServiceBase? _service;
        bool _applyOverwriteToAll = false;
        AccountModelBase? _account;
        List<FileSystemObjectModel> _clipboardItems = new();
        TransferMode _clipboardMode = TransferMode.Copy;

        public ExplorerViewModel()
        {
            _shared = GetService<SharedService>();
            _onSelectionChanged = EventAggregator?.Subscribe<SelectionChangedEventArgs>(SelectionChanged);
            _onNewFolder = EventAggregator?.Subscribe<NewFolderRequestedEventArgs>(NewFolderRequested);
            // If navigation service already has a selection (published before this VM subscribed), apply it now
            try
            {
                var nav = GetService<NavigationService>();
                var current = nav?.CurrentSelection;
                if (current != null)
                {
                    // Defer to the SelectionChanged handler to populate view
                    Invoke(() => SelectionChanged(current));
                }
            }
            catch { }
            if (_shared?.ApplicationConfiguration != null)
            {
                _shared.ApplicationConfiguration.PropertyChanged += ApplicationConfiguration_PropertyChanged;
            }
        }

        ~ExplorerViewModel()
        {
            if (_onSelectionChanged != null)
                EventAggregator?.UnSubscribe(_onSelectionChanged);
            if (_onNewFolder != null)
                EventAggregator?.UnSubscribe(_onNewFolder);
            if (_shared?.ApplicationConfiguration != null)
                _shared.ApplicationConfiguration.PropertyChanged -= ApplicationConfiguration_PropertyChanged;
        }

        // Remember per-directory sort settings: map directory path -> (sortMemberPath, ascending)
        readonly Dictionary<string, (string sortMember, bool ascending)> _directorySortSettings = new();

        public void SaveDirectorySort(string? directoryPath, string sortMember, bool ascending)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(sortMember))
                return;

            _directorySortSettings[directoryPath] = (sortMember, ascending);
        }

        public (string sortMember, bool ascending)? GetDirectorySort(string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return null;

            if (_directorySortSettings.TryGetValue(directoryPath, out var v))
                return v;

            return null;
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

        public ICommand RenameItemCommand
        {
            get
            {
                if (_renameItem == null)
                    _renameItem = new RelayCommand<ExplorerItemModel>(StartRename);

                return _renameItem!;
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

        public ICommand OpenTerminalCommand
        {
            get
            {
                if (_openTerminalCommand == null)
                    _openTerminalCommand = new RelayCommand<object?>(OpenTerminal);
                return _openTerminalCommand!;
            }
        }

        public bool CanPaste
        {
            get => Get<bool>(nameof(CanPaste));
            private set => Set(value);
        }

        public ICommand CommitRenameCommand
        {
            get
            {
                if (_commitRename == null)
                    _commitRename = new RelayCommand<ExplorerItemModel>(CommitRename, isAsynchronous: true);
                return _commitRename!;
            }
        }

        public ICommand CancelRenameCommand
        {
            get
            {
                if (_cancelRename == null)
                    _cancelRename = new RelayCommand<ExplorerItemModel>(CancelRename);
                return _cancelRename!;
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

        void StartRename(ExplorerItemModel? item)
        {
            if (item == null)
                return;

            if (!item.IsFile && !item.IsDirectory)
                return;

            // initialize edit name and toggle edit mode
            item.EditableName = item.DisplayName;
            item.IsEditing = true;
        }

        void OpenTerminal(object? parameter)
        {
            try
            {
                // Determine the target folder to open in terminal.
                // If a specific item was provided via the context menu, use that;
                // otherwise fall back to the current `Item` selection.
                string targetPath = string.Empty;
                if (parameter != null && parameter is ExplorerItemModel paramItem)
                {
                    var obj = paramItem.Object as FileSystemObjectModel;
                    if (obj is FileModel pf)
                        targetPath = System.IO.Path.GetDirectoryName(pf.Path) ?? pf.Path ?? string.Empty;
                    else if (obj is DirectoryModel pd)
                        targetPath = pd.Path ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    var fsObj = Item?.Object as FileSystemObjectModel;
                    if (fsObj != null)
                    {
                        if (fsObj is FileModel fileModel)
                        {
                            try { targetPath = System.IO.Path.GetDirectoryName(fileModel.Path) ?? fileModel.Path; } catch { targetPath = fileModel.Path ?? string.Empty; }
                        }
                        else if (fsObj is DirectoryModel dirModel2)
                        {
                            targetPath = dirModel2.Path ?? string.Empty;
                        }
                    }
                    else
                    {
                        var dirModel = Item?.Object as DirectoryModel;
                        targetPath = dirModel?.Path ?? string.Empty;
                    }
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                    return;

                // Determine whether to prefer iTerm: user preference if set, otherwise detect installation
                bool preferIterm;
                try
                {
                    preferIterm = _shared?.ApplicationConfiguration?.PreferIterm ?? (System.IO.Directory.Exists("/Applications/iTerm.app") || System.IO.File.Exists("/Applications/iTerm.app"));
                }
                catch
                {
                    preferIterm = System.IO.Directory.Exists("/Applications/iTerm.app") || System.IO.File.Exists("/Applications/iTerm.app");
                }

                FileSystemLogger.Information("OpenTerminal requested: target={TargetPath} preferIterm={PreferIterm}", targetPath, preferIterm);

                if (OperatingSystem.IsMacOS())
                {
                    // Helper: escape single quotes for shell arguments by replacing ' with '"'"'
                    string EscapeSingleQuotes(string s) => s?.Replace("'", "'\"'\"'") ?? string.Empty;
                        var shellSafePath = EscapeSingleQuotes(targetPath);

                    // Helper: write AppleScript to temp file and execute with osascript
                    void RunAppleScriptFromFile(string script)
                    {
                        try
                        {
                            var tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jaya-terminal-{Guid.NewGuid()}.applescript");
                            System.IO.File.WriteAllText(tmpPath, script);
                            FileSystemLogger.Debug("OpenTerminal: wrote appleScript to {TmpPath} scriptStart=\n{Script}\nscriptEnd", tmpPath, script);
                            var psi = new ProcessStartInfo("osascript", tmpPath) { UseShellExecute = false };
                            FileSystemLogger.Information("OpenTerminal: executing osascript {TmpPath}", tmpPath);
                            var p = Process.Start(psi);
                            try { p?.WaitForExit(2000); } catch { }
                            try { System.IO.File.Delete(tmpPath); } catch { }
                        }
                        catch (Exception ex)
                        {
                            FileSystemLogger.Warning(ex, "Failed to run AppleScript via temporary file");
                        }
                    }

                    if (preferIterm)
                    {
                        // iTerm2 AppleScript: create a window and run cd to the path
                        var script = "tell application \"iTerm\"\n" +
                                     "  create window with default profile\n" +
                                     "  tell current session of current window\n" +
                                     $"    write text \"cd '{shellSafePath}'; clear\"\n" +
                                     "  end tell\n" +
                                     "  activate\n" +
                                     "end tell";
                        FileSystemLogger.Information("OpenTerminal: running iTerm appleScript (preferIterm=true)");
                        RunAppleScriptFromFile(script);
                    }
                    else
                    {
                        // Terminal AppleScript: open Terminal and run cd to the path
                        var script = "tell application \"Terminal\"\n" +
                                     $"  do script \"cd '{shellSafePath}'; clear\"\n" +
                                     "  activate\n" +
                                     "end tell";
                        FileSystemLogger.Information("OpenTerminal: running Terminal appleScript (preferIterm=false)");
                        RunAppleScriptFromFile(script);
                    }
                }
                else if (OperatingSystem.IsWindows())
                {
                    // Use wt (Windows Terminal) if available, otherwise cmd
                    try
                        {
                            FileSystemLogger.Information("OpenTerminal: launching wt -d {TargetPath}", targetPath);
                            Process.Start(new ProcessStartInfo("wt", $"-d \"{targetPath}\"") { UseShellExecute = true });
                    }
                    catch
                    {
                            FileSystemLogger.Information("OpenTerminal: wt failed, falling back to cmd /K cd /d {TargetPath}", targetPath);
                            Process.Start(new ProcessStartInfo("cmd", $"/K cd /d \"{targetPath}\"") { UseShellExecute = true });
                    }
                }
                else
                {
                    // Linux: try gnome-terminal, x-terminal-emulator, or xterm
                        try { FileSystemLogger.Information("OpenTerminal: launching gnome-terminal --working-directory={TargetPath}", targetPath); Process.Start(new ProcessStartInfo("gnome-terminal", $"--working-directory=\"{targetPath}\"") { UseShellExecute = true }); return; } catch (Exception ex) { FileSystemLogger.Debug(ex, "gnome-terminal failed"); }
                        try { FileSystemLogger.Information("OpenTerminal: launching x-terminal-emulator --working-directory={TargetPath}", targetPath); Process.Start(new ProcessStartInfo("x-terminal-emulator", $"--working-directory=\"{targetPath}\"") { UseShellExecute = true }); return; } catch (Exception ex) { FileSystemLogger.Debug(ex, "x-terminal-emulator failed"); }
                        try { FileSystemLogger.Information("OpenTerminal: launching xterm and cd to {TargetPath}", targetPath); Process.Start(new ProcessStartInfo("xterm", $"-e \"cd \"{targetPath}\"; bash\"") { UseShellExecute = true }); return; } catch (Exception ex) { FileSystemLogger.Debug(ex, "xterm failed"); }
                }
            }
            catch (Exception ex)
            {
                FileSystemLogger.Error(ex, "Failed to open terminal");
            }
        }

        async void CommitRename(ExplorerItemModel? item)
        {
            if (item == null)
                return;

            var itemLocal = item!;

            // capture service/account to local variables and validate to avoid nullable warnings
            var serviceLocal = _service;
            var accountLocal = _account;
            if (serviceLocal == null || accountLocal == null)
                return;

            if (!item.IsFile && !item.IsDirectory)
                return;

            var newName = item.EditableName?.Trim();
            if (string.IsNullOrWhiteSpace(newName) || newName == itemLocal.DisplayName)
            {
                itemLocal.IsEditing = false;
                return;
            }

            var fso = item.Object as FileSystemObjectModel;
            if (fso == null || string.IsNullOrWhiteSpace(fso.Path))
            {
                itemLocal.IsEditing = false;
                return;
            }

            try
            {
                FileSystemLogger.Debug("CommitRename: starting for item={Label} currentDisplayName={DisplayName} editableName={EditableName}", item?.Label, item?.DisplayName, item?.EditableName);
                // Check for local conflict (destination exists)
                var parentDir = System.IO.Path.GetDirectoryName(fso.Path) ?? string.Empty;
                var destPath = System.IO.Path.Combine(parentDir, newName);
                FileSystemLogger.Debug("CommitRename: computed parentDir={ParentDir} destPath={Dest}", parentDir, destPath);
                ConflictPromptViewModel? cvm = null;
                if (System.IO.File.Exists(destPath) || System.IO.Directory.Exists(destPath))
                {
                    // Show conflict prompt
                    var conflictDialog = new Views.ConflictPromptView();
                    cvm = conflictDialog.DataContext as ConflictPromptViewModel;
                    var owner = App.Lifetime?.MainWindow;
                    if (owner != null)
                        await conflictDialog.ShowDialog(owner);
                    else
                        conflictDialog.Show();

                    var choice = cvm?.Result ?? ConflictResult.Cancel;
                    if (choice == ConflictResult.Cancel)
                    {
                        itemLocal.IsEditing = false;
                        return;
                    }

                    if (choice == ConflictResult.KeepBoth)
                    {
                        // compute unique name
                        var unique = GetUniqueNameInFolder(parentDir, newName);
                        newName = unique;
                        destPath = System.IO.Path.Combine(parentDir, newName);
                    }

                    // If Overwrite selected, we'll pass the desired name and let provider handle overwrite if supported.
                }
                // determine overwrite flag
                bool overwrite = _applyOverwriteToAll;
                FileSystemLogger.Debug("CommitRename: overwrite initial={OverwriteFlag} applyOverwriteToAll={ApplyAll}", overwrite, _applyOverwriteToAll);
                // if we previously showed a dialog, cvm variable will carry user's choice

                if (serviceLocal is not Jaya.Shared.Services.IFileRenameService renameService)
                    return;

                // if user selected overwrite in the dialog, set overwrite accordingly
                if (cvm != null && cvm.Result == ConflictResult.Overwrite)
                {
                    overwrite = true;
                    if (cvm.ApplyToAll)
                        _applyOverwriteToAll = true;
                }

                var progress = new Progress<TransferProgressReport>(report => { });

                // Create non-null local copies for use after await to satisfy nullable analysis.
                var accountForCall = accountLocal!;
                var fsoPath = fso.Path ?? string.Empty;

                FileSystemLogger.Debug("CommitRename: calling provider.RenameAsync account={Account} sourcePath={Source} newName={NewName} overwrite={Overwrite}", accountForCall.Name, fsoPath, newName, overwrite);
                var result = await renameService.RenameAsync(accountForCall, fso, newName, overwrite, progress, System.Threading.CancellationToken.None);
                FileSystemLogger.Debug("CommitRename: provider.RenameAsync returned resultName={Result}", result?.Path);
                if (result != null)
                {
                    Invoke(() =>
                    {
                        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                        var existing = Item?.Children?.FirstOrDefault(c =>
                        {
                            var obj = c.Object as FileSystemObjectModel;
                            if (obj == null || string.IsNullOrWhiteSpace(obj.Path))
                                return false;

                            return comparer.Equals(obj.Path, fsoPath);
                        });

                        if (existing != null)
                        {
                            var index = Item?.Children?.IndexOf(existing) ?? -1;
                            if (index >= 0)
                            {
                                var newLabel = result switch
                                {
                                    // Pass the base name (without extension) as the label so ExplorerItemModel
                                    // will compose DisplayName using the FileModel's Extension. Passing a
                                    // label that already contains the extension caused duplicate ".ext".
                                    Jaya.Shared.Models.FileModel file => file.Name,
                                    Jaya.Shared.Models.DirectoryModel dir => dir.Name,
                                    _ => result.Name
                                };

                                var newItem = new ExplorerItemModel(existing.Type, newLabel, result, existing.ImagePath);

                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                FileSystemLogger.Warning(ex, "Rename failed for {Path}", fso.Path);
            }
            finally
            {
                item.IsEditing = false;
            }
        }

        void CancelRename(ExplorerItemModel? item)
        {
            if (item == null)
                return;
            item.IsEditing = false;
        }

        string GetUniqueNameInFolder(string folder, string name)
        {
            // If no conflict, return original name
            var dest = System.IO.Path.Combine(folder, name);
            if (!System.IO.File.Exists(dest) && !System.IO.Directory.Exists(dest))
                return name;

            var baseName = name;
            var ext = string.Empty;
            if (System.IO.Path.HasExtension(name))
            {
                ext = System.IO.Path.GetExtension(name);
                baseName = name.Substring(0, name.Length - ext.Length);
            }

            for (int i = 1; i < 10000; i++)
            {
                var candidate = $"{baseName} ({i}){ext}";
                var candidatePath = System.IO.Path.Combine(folder, candidate);
                if (!System.IO.File.Exists(candidatePath) && !System.IO.Directory.Exists(candidatePath))
                    return candidate;
            }

            // fallback
            return name + " (copy)";
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

                // Update CanPaste flag based on clipboard contents
                CanPaste = _clipboardItems != null && _clipboardItems.Count > 0;
                try
                {
                    var shared = GetService<SharedService>();
                    if (shared != null)
                        shared.UpdatePasteAvailability(_clipboardItems);
                }
                catch { }
                // Ensure progress window is closed even if the transfer service didn't report completion
                try
                {
                    if (progressWindow != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try { progressWindow.Close(); } catch { }
                        }, DispatcherPriority.Background);
                    }
                }
                catch { }
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
            CanPaste = _clipboardItems != null && _clipboardItems.Count > 0;
            try
            {
                var shared = GetService<SharedService>();
                if (shared != null)
                    shared.UpdatePasteAvailability(_clipboardItems);
            }
            catch { }
        }

        void NewFolderRequested(NewFolderRequestedEventArgs? args)
        {
            // Fire-and-forget create new folder operation on UI thread
            try
            {
                _ = CreateNewFolderAsync();
            }
            catch (Exception ex)
            {
                FileSystemLogger.Warning(ex, "NewFolderRequested handler failed");
            }
        }

        async Task CreateNewFolderAsync()
        {
            try
            {
                var currentDir = Item?.Object as DirectoryModel;
                if (currentDir == null || string.IsNullOrWhiteSpace(currentDir.Path))
                {
                    FileSystemLogger.Debug("CreateNewFolder skipped: no current directory context.");
                    return;
                }

                var parentPath = currentDir.Path;
                // Use macOS Finder style default: "untitled folder", "untitled folder 2", ...
                var baseName = "untitled folder";
                // GetUniqueNameInFolder expects a filename; it will respect extensions. For mac-style numbering
                // we reuse the same function but ensure to try variants with numeric suffixes matching Finder behavior.
                var unique = baseName;
                if (System.IO.Directory.Exists(System.IO.Path.Combine(parentPath, unique)))
                {
                    // Try Finder-like suffixes: "untitled folder 2", "untitled folder 3", ...
                    for (int i = 2; i < 10000; i++)
                    {
                        var candidate = $"{baseName} {i}";
                        var candidatePath = System.IO.Path.Combine(parentPath, candidate);
                        if (!System.IO.Directory.Exists(candidatePath))
                        {
                            unique = candidate;
                            break;
                        }
                    }
                }
                var newPath = System.IO.Path.Combine(parentPath, unique);

                // Require provider support: only create via provider's IFileCreateService.
                if (!(_service is Jaya.Shared.Services.IFileCreateService createService) || _account == null)
                {
                    FileSystemLogger.Debug("CreateNewFolder skipped: service does not support IFileCreateService or account missing.");
                    return;
                }

                FileSystemObjectModel? createdObj = null;
                // Try base name and numbered variants until provider succeeds
                var attempts = new List<string> { unique };
                for (int i = 2; i < 10000; i++)
                {
                    attempts.Add($"{baseName} {i}");
                    if (attempts.Count >= 50) break; // safety cap
                }

                foreach (var candidate in attempts)
                {
                    try
                    {
                        createdObj = await createService.CreateDirectoryAsync(_account, currentDir, candidate);
                        if (createdObj != null)
                        {
                            unique = candidate;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        FileSystemLogger.Warning(ex, "Provider CreateDirectoryAsync failed for candidate {Candidate}", candidate);
                    }
                }

                if (createdObj == null)
                {
                    FileSystemLogger.Debug("CreateNewFolder: provider failed to create any candidate name.");
                    return;
                }

                var created = createdObj as DirectoryModel ?? new DirectoryModel() { Name = createdObj.Name ?? unique, Path = createdObj.Path };

                // Add and then put the created item into edit mode so user can rename immediately
                Invoke(() =>
                {
                    AddItemsToView(new[] { created }, selectAdded: true);

                    // Find the newly added ExplorerItemModel and enable editing
                    try
                    {
                        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                        var newItem = Item?.Children?.FirstOrDefault(c => (c.Object as FileSystemObjectModel)?.Path != null && comparer.Equals((c.Object as FileSystemObjectModel)!.Path, created.Path));
                        if (newItem != null)
                        {
                            newItem.EditableName = newItem.DisplayName;
                            newItem.IsEditing = true;
                            // Publish a selection so view attempts to focus the new item
                            EventAggregator?.Publish(new SelectItemsRequestedEventArgs(new[] { created.Path }));
                        }
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                FileSystemLogger.Warning(ex, "CreateNewFolderAsync failed");
            }
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

        public async System.Threading.Tasks.Task HandleDropAsync(string[] sourcePaths, DirectoryModel? targetDirectory, Avalonia.Input.DragDropEffects effect)
        {
            if (sourcePaths == null || sourcePaths.Length == 0)
                return;

            if (_service == null || _account == null)
            {
                FileSystemLogger.Debug("Drop ignored: missing service/account context.");
                return;
            }

            try
            {
                var transferService = ServiceLocator.Instance.GetProviders().OfType<IFileTransferService>().FirstOrDefault();
                if (transferService == null)
                {
                    FileSystemLogger.Warning("Drop requested but transfer service not available.");
                    return;
                }

                var mode = effect == Avalonia.Input.DragDropEffects.Copy ? TransferMode.Copy : TransferMode.Move;

                var cancellation = new System.Threading.CancellationTokenSource();
                var progressWindow = new Views.TransferProgressView();
                var progressViewModel = progressWindow.DataContext as TransferProgressViewModel;
                if (progressViewModel != null)
                {
                    progressViewModel.AttachCancellation(cancellation);
                    var verb = mode == TransferMode.Move ? "Moving items" : "Copying items";
                    progressViewModel.Title = verb;
                    progressViewModel.HeaderText = $"{verb}...";
                    progressViewModel.StatusText = "Preparing items...";
                    progressViewModel.TargetPath = targetDirectory?.Path ?? string.Empty;
                }

                var owner = App.Lifetime?.MainWindow;
                if (owner != null)
                    progressWindow.Show(owner);
                else
                    progressWindow.Show();

                var progress = new System.Progress<TransferProgressReport>(report => { if (progressViewModel != null) progressViewModel.Update(report); });

                // Map string paths to FileSystemObjectModel instances where possible; fall back to raw paths via provider later if needed
                var items = new List<FileSystemObjectModel>();
                if (Item?.Children != null)
                {
                    var lookup = Item.Children
                        .Select(c => c.Object as FileSystemObjectModel)
                        .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Path))
                        .ToDictionary(f => f.Path!, f => f, StringComparer.OrdinalIgnoreCase);

                    foreach (var p in sourcePaths)
                    {
                        if (p != null && lookup.TryGetValue(p, out var found))
                            items.Add(found);
                    }
                }

                var results = await transferService.TransferAsync(_account, items, targetDirectory, mode, progress, cancellation.Token);
                FileSystemLogger.Information("Drop transfer requested for {Count} items (created={Created})", items.Count, results?.Count ?? 0);

                if (results != null && results.Count > 0)
                {
                    // Only add created items to this view if the transfer target matches the current directory
                    try
                    {
                        var currentDir = Item?.Object as DirectoryModel;
                        if (currentDir != null && targetDirectory != null && !string.IsNullOrWhiteSpace(currentDir.Path) && !string.IsNullOrWhiteSpace(targetDirectory.Path))
                        {
                            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                            if (comparer.Equals(currentDir.Path, targetDirectory.Path))
                            {
                                Invoke(() => AddItemsToView(results, selectAdded: true));
                            }
                        }
                        else if (currentDir != null && targetDirectory == null)
                        {
                            // If targetDirectory is null but currentDir exists, conservatively add to view
                            Invoke(() => AddItemsToView(results, selectAdded: true));
                        }
                    }
                    catch { }
                }

                // If this was a Move, remove the original items from the current view
                try
                {
                    if (mode == TransferMode.Move && items != null && items.Count > 0)
                    {
                        Invoke(() => RemoveItemsFromView(items));
                    }
                }
                catch (Exception ex)
                {
                    FileSystemLogger.Warning(ex, "Failed to remove moved items from view");
                }

                // Close the progress window on UI thread to ensure it doesn't stay open
                try
                {
                    if (progressWindow != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try { progressWindow.Close(); } catch { }
                        }, DispatcherPriority.Background);
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                FileSystemLogger.Error(ex, "Failed to handle drop for {Count} items", sourcePaths.Length);
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
