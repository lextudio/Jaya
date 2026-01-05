//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Serilog;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Jaya.IO;
using Jaya.IO.Models;

namespace Jaya.Ui.ViewModels
{
    public class NavigationViewModel : ViewModelBase
    {
        readonly SharedService? _shared;
        readonly Subscription<SelectionChangedEventArgs>? _onSelectionChanged;
        static readonly ILogger Logger = Log.ForContext(typeof(NavigationViewModel)).ForContext("SourceContext", "ViewModels");
        ICommand? _populateCommand;
        ICommand? _selectLocationCommand;
        TreeNodeModel? _selectedNode;
        bool _suppressPublish;
        bool _suppressPopulateOnExpand;
        readonly VolumeCacheService? _volumeCacheService;
        ObservableCollection<TreeNodeModel>? _favorites = new();
            ObservableCollection<LocationItemViewModel>? _locations = new();
            LocationItemViewModel? _selectedLocation;

        public NavigationViewModel()
        {
            _shared = GetService<SharedService>();
            _volumeCacheService = GetService<VolumeCacheService>();

            Node = new TreeNodeModel(null, null, null);
            Favorites = _favorites;
            Locations = _locations;
            if (!IsDesignMode)
                _onSelectionChanged = EventAggregator?.Subscribe<SelectionChangedEventArgs>(OnExternalSelectionChanged);

            if (!IsDesignMode)
                PopulateCommand?.Execute(Node);
        }

        ~NavigationViewModel()
        {
            if (_onSelectionChanged != null)
                EventAggregator?.UnSubscribe(_onSelectionChanged);
        }

        #region properties

        public ICommand PopulateCommand
        {
            get
            {
                _populateCommand ??= new RelayCommand<TreeNodeModel>(PopulateAction);
                return _populateCommand!;
            }
        }

        public ICommand SelectLocationCommand
        {
            get
            {
                _selectLocationCommand ??= new RelayCommand<LocationItemViewModel>(loc =>
                {
                    try
                    {
                        SelectedLocation = loc;
                    }
                    catch { }
                });
                return _selectLocationCommand!;
            }
        }

        public PaneConfigModel PaneConfig => _shared?.PaneConfiguration ?? new PaneConfigModel();

        public ApplicationConfigModel ApplicationConfig => _shared?.ApplicationConfiguration ?? new ApplicationConfigModel();

        public TreeNodeModel Node { get; }

        public TreeNodeModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (ReferenceEquals(_selectedNode, value))
                    return;

                _selectedNode = value;
                RaisePropertyChanged(nameof(SelectedNode));

                if (value == null)
                    return;

                Logger.Information("NavigationViewModel.SelectedNode setter invoked: Label={Label}, Service={Service}, Account={Account}, Path={Path}",
                    value.Label ?? string.Empty,
                    value.Service?.Name ?? string.Empty,
                    value.Account?.Name ?? string.Empty,
                    (value.FileSystemObject as DirectoryModel)?.Path ?? string.Empty);

                if (_suppressPublish)
                {
                    Logger.Debug("Suppressed publish for programmatic SelectedNode change.");
                    return;
                }

                var args = new SelectionChangedEventArgs(value.Service, value.Account, value.FileSystemObject as DirectoryModel);
                EventAggregator?.Publish(args);
            }
        }

        public ObservableCollection<TreeNodeModel> Favorites
        {
            get => _favorites ?? (_favorites = new ObservableCollection<TreeNodeModel>());
            private set => Set(ref _favorites, value ?? new ObservableCollection<TreeNodeModel>());
        }

        public ObservableCollection<LocationItemViewModel> Locations
        {
            get => _locations ?? (_locations = new ObservableCollection<LocationItemViewModel>());
            private set => Set(ref _locations, value ?? new ObservableCollection<LocationItemViewModel>());
        }

        public LocationItemViewModel? SelectedLocation
        {
            get => _selectedLocation;
            set
            {
                if (ReferenceEquals(_selectedLocation, value))
                    return;

                _selectedLocation = value;
                RaisePropertyChanged(nameof(SelectedLocation));

                if (_selectedLocation == null)
                    return;

                if (_suppressPublish)
                {
                    Logger.Debug("Suppressed publish for programmatic SelectedLocation change.");
                    return;
                }

                // Publish selection to navigate to the chosen location
                var args = new SelectionChangedEventArgs(_selectedLocation.Service, _selectedLocation.Account, _selectedLocation.Directory);
                EventAggregator?.Publish(args);
            }
        }

        #endregion

        void OnNodeExpanded(TreeNodeModel node, bool isExpaded)
        {
            Log.ForContext<NavigationViewModel>().Information("Node {ExpandedState}: Label={Label}, Service={Service}, Path={Path}",
                isExpaded ? "Expanded" : "Collapsed",
                node.Label,
                node.Service?.Name,
                (node.FileSystemObject as DirectoryModel)?.Path);

            if (!isExpaded)
                return;

            if (_suppressPopulateOnExpand)
            {
                Logger.Debug("Populate suppressed for expansion: Label={Label}, Path={Path}",
                    node.Label,
                    (node.FileSystemObject as DirectoryModel)?.Path);
                return;
            }

            if (node.IsHavingDummyChild || node.NeedsPopulate)
                PopulateCommand?.Execute(node);
        }

        void AddChildNode(TreeNodeModel node, TreeNodeModel childNode)
        {
            Invoke(() =>
            {
                // Avoid adding duplicate child nodes (same label and path)
                var exists = node.Children.Any(c =>
                    string.Equals(c.Label, childNode.Label, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((c.FileSystemObject as Jaya.Shared.Models.DirectoryModel)?.Path,
                                  (childNode.FileSystemObject as Jaya.Shared.Models.DirectoryModel)?.Path,
                                  StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    node.Children.Add(childNode);
                    Log.ForContext<NavigationViewModel>().Information("Child added: Parent={Parent}, Child={Child}, Path={Path}",
                        node.Label, childNode.Label, (childNode.FileSystemObject as DirectoryModel)?.Path);
                }
            });
        }

        async Task AddChildNodeAsync(TreeNodeModel node, TreeNodeModel childNode)
        {
            if (node == null || childNode == null)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Avoid adding duplicate child nodes (same label and path)
                var exists = node.Children.Any(c =>
                    string.Equals(c.Label, childNode.Label, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((c.FileSystemObject as Jaya.Shared.Models.DirectoryModel)?.Path,
                                  (childNode.FileSystemObject as Jaya.Shared.Models.DirectoryModel)?.Path,
                                  StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    node.Children.Add(childNode);
                    Log.ForContext<NavigationViewModel>().Information("Child added: Parent={Parent}, Child={Child}, Path={Path}",
                        node.Label, childNode.Label, (childNode.FileSystemObject as DirectoryModel)?.Path);
                }
            });
        }

        void RemoveChildNode(TreeNodeModel node, TreeNodeModel childNode)
        {
            Invoke(() =>
            {
                if (node.Children.Remove(childNode))
                {
                    Log.ForContext<NavigationViewModel>().Information("Child removed: Parent={Parent}, Child={Child}", node.Label, childNode.Label);
                }
            });
        }

        async void PopulateAction(TreeNodeModel node)
        {
            try
            {
                await PopulateNodeAsync(node, updateSelection: true);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "PopulateAction failed");
            }
        }

        void OnAccountAction(AccountModelBase account, AccountAction action, TreeNodeModel node)
        {
            if (action == AccountAction.Added)
            {
                var accountNode = new TreeNodeModel(node.Service, account, ItemType.Account);
                accountNode.Label = account.Name;
                accountNode.FileSystemObject = new DirectoryModel();
                accountNode.ImagePath = account.ImagePath;
                accountNode.NodeExpanded += OnNodeExpanded;
                accountNode.AddDummyChild();
                AddChildNode(node, accountNode);
            }
            else if (action == AccountAction.Removed)
            {
                foreach (var accountNode in node.Children)
                {
                    if (!object.Equals(accountNode.Account, account))
                        continue;

                    RemoveChildNode(node, accountNode);
                    break;
                }
            }
        }

        async void OnExternalSelectionChanged(SelectionChangedEventArgs args)
        {
            if (args == null)
                return;

            Logger.Debug("External selection: Service={Service}, Account={Account}, Directory={Directory}",
                args.Service?.Name ?? string.Empty,
                args.Account?.Name ?? string.Empty,
                args.Directory?.Path ?? args.Directory?.Name ?? string.Empty);

            // Find the node matching the service/account/directory
            var target = FindNodeForSelection(Node, args.Service, args.Account, args.Directory);
            if (target == null)
            {
                Logger.Debug("External selection not found in current tree. Ensuring node path.");
                try
                {
                    target = await EnsureNodeForSelectionAsync(args);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "OnExternalSelectionChanged: failed to ensure tree node for selection");
                }
            }
            if (target != null)
            {
                Logger.Debug("External selection matched node: Label={Label}, Path={Path}",
                    target.Label,
                    (target.FileSystemObject as DirectoryModel)?.Path ?? string.Empty);

                // Expand ancestors so the node becomes visible in the tree
                try
                {
                    _suppressPopulateOnExpand = true;
                    var expanded = ExpandAncestors(Node, target);
                    Logger.Debug("Expanded ancestors for selection. Expanded={Expanded} Label={Label}", expanded, target.Label);
                }
                finally
                {
                    _suppressPopulateOnExpand = false;
                }

                // Set SelectedNode (this will publish selection again via setter)
                try
                {
                    _suppressPublish = true;
                    SelectedNode = target;
                }
                finally
                {
                    _suppressPublish = false;
                }
                // Also update location selection if the selected directory matches a known location
                try { UpdateLocationSelection(args); } catch { }
            }
            else
            {
                Logger.Debug("OnExternalSelectionChanged: matching navigation node not found for Service={Service}, Account={Account}, Directory={Directory}",
                    args.Service?.Name, args.Account?.Name, args.Directory?.Path ?? args.Directory?.Name);
            }
        }

        void UpdateLocationSelection(SelectionChangedEventArgs args)
        {
            if (args == null)
                return;

            try
            {
                _suppressPublish = true;
                // Find location with matching path
                foreach (var loc in Locations)
                {
                    if (loc.Directory != null && args.Directory != null && string.Equals(loc.Directory.Path, args.Directory.Path, StringComparison.OrdinalIgnoreCase))
                        SelectedLocation = loc;
                }
            }
            finally
            {
                _suppressPublish = false;
            }
        }

        string? GetTrashPath()
        {
            // macOS: ~/.Trash
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.MacOSX || Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    var trash = System.IO.Path.Combine(home, ".Trash");
                    return trash;
                }
                else if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    // Windows Recycle Bin is not a simple path; return empty and disable Trash entry on Windows for now
                    return string.Empty;
                }
            }
            catch { }

            return string.Empty;
        }

        TreeNodeModel? FindNodeForSelection(TreeNodeModel root, ProviderServiceBase? service, AccountModelBase? account, DirectoryModel? directory)
        {
            if (root == null)
                return null;

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            // Check current node
            if (Equals(root.Service, service) && Equals(root.Account, account))
            {
                if (directory == null && (root.FileSystemObject == null || string.IsNullOrEmpty((root.FileSystemObject as DirectoryModel)?.Path)))
                    return root;

                if (directory != null && root.FileSystemObject is DirectoryModel d)
                {
                    var rootPath = NormalizePath(d.Path, comparison);
                    var dirPath = NormalizePath(directory.Path, comparison);
                    if (!string.IsNullOrEmpty(rootPath) && !string.IsNullOrEmpty(dirPath) && string.Equals(rootPath, dirPath, comparison))
                        return root;
                }
            }

            // Search children
            foreach (var child in root.Children)
            {
                var found = FindNodeForSelection(child, service, account, directory);
                if (found != null)
                    return found;
            }

            return null;
        }

        bool ExpandAncestors(TreeNodeModel root, TreeNodeModel target)
        {
            if (root == null || target == null)
                return false;

            // If target is a direct child, expand root and return
            if (root.Children.Contains(target))
            {
                root.IsExpanded = true;
                return true;
            }

            foreach (var child in root.Children)
            {
                if (ExpandAncestors(child, target))
                {
                    root.IsExpanded = true;
                    return true;
                }
            }

            return false;
        }

        async Task<TreeNodeModel?> EnsureNodeForSelectionAsync(SelectionChangedEventArgs args)
        {
            if (args == null || args.Service == null)
                return null;

            Logger.Debug("EnsureNodeForSelection: Service={Service}, Account={Account}, Directory={Directory}",
                args.Service?.Name ?? string.Empty,
                args.Account?.Name ?? string.Empty,
                args.Directory?.Path ?? args.Directory?.Name ?? string.Empty);

            await EnsureRootPopulatedAsync();

            var serviceNode = Node.Children.FirstOrDefault(n => Equals(n.Service, args.Service) && n.Account == null);
            if (serviceNode == null)
                return null;

            await EnsureNodePopulatedAsync(serviceNode);

            TreeNodeModel? accountNode = null;
            if (args.Account != null)
                accountNode = serviceNode.Children.FirstOrDefault(n => Equals(n.Account, args.Account));

            if (accountNode == null)
                return null;

            if (args.Directory == null || string.IsNullOrWhiteSpace(args.Directory.Path))
                return accountNode;

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var targetPath = NormalizePath(args.Directory.Path ?? args.Directory.Name ?? string.Empty, comparison);
            if (string.IsNullOrEmpty(targetPath))
                return accountNode;

            if (IsFileSystemService(args.Service))
            {
                Logger.Debug("EnsureNodeForSelection: using fast path for File System target={TargetPath}", targetPath);
                return await EnsureFileSystemPathChainAsync(accountNode, targetPath, comparison) ?? accountNode;
            }

            await EnsureNodePopulatedAsync(accountNode);

            var current = FindBestPathMatchChild(accountNode, targetPath, comparison);
            if (current == null)
                return accountNode;

            var safety = 0;
            while (current != null && safety++ < 128)
            {
                var currentPath = NormalizePath((current.FileSystemObject as DirectoryModel)?.Path, comparison);
                if (!string.IsNullOrEmpty(currentPath) && string.Equals(currentPath, targetPath, comparison))
                    return current;

                await EnsureNodePopulatedAsync(current);

                var next = FindBestPathMatchChild(current, targetPath, comparison);
                if (next == null || ReferenceEquals(next, current))
                    return current;

                current = next;
            }

            return current;
        }

        async Task EnsureRootPopulatedAsync()
        {
            if (Node.Children.Count > 0 && !Node.IsHavingDummyChild)
                return;

            await PopulateNodeAsync(Node, updateSelection: false);
        }

        async Task EnsureNodePopulatedAsync(TreeNodeModel node)
        {
            if (node == null)
                return;

            if (node.Children.Count > 0 && !node.IsHavingDummyChild)
                return;

            await PopulateNodeAsync(node, updateSelection: false);
        }

        TreeNodeModel? FindBestPathMatchChild(TreeNodeModel parent, string targetPath, StringComparison comparison)
        {
            if (parent == null || parent.Children.Count == 0)
                return null;

            TreeNodeModel? best = null;
            var bestLength = -1;

            foreach (var child in parent.Children)
            {
                if (child.FileSystemObject is not DirectoryModel dir)
                    continue;

                var childPath = NormalizePath(dir.Path, comparison);
                if (string.IsNullOrEmpty(childPath))
                    continue;

                if (IsPathPrefix(childPath, targetPath, comparison) && childPath.Length > bestLength)
                {
                    best = child;
                    bestLength = childPath.Length;
                }
            }

            return best;
        }

        static string NormalizePath(string? path, StringComparison comparison)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var root = Path.GetPathRoot(path) ?? string.Empty;
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.IsNullOrEmpty(root) && string.Equals(trimmed, trimmedRoot, comparison))
                return root;

            return trimmed;
        }

        static bool IsPathPrefix(string candidate, string target, StringComparison comparison)
        {
            if (string.Equals(candidate, target, comparison))
                return true;

            if (target.Length <= candidate.Length)
                return false;

            if (candidate.EndsWith(Path.DirectorySeparatorChar) || candidate.EndsWith(Path.AltDirectorySeparatorChar))
                return target.StartsWith(candidate, comparison);

            if (!target.StartsWith(candidate, comparison))
                return false;

            var nextChar = target[candidate.Length];
            return nextChar == Path.DirectorySeparatorChar || nextChar == Path.AltDirectorySeparatorChar;
        }

        static bool IsFileSystemService(ProviderServiceBase? service)
        {
            return string.Equals(service?.Name, "File System", StringComparison.OrdinalIgnoreCase);
        }

        async Task<TreeNodeModel?> EnsureFileSystemPathChainAsync(TreeNodeModel accountNode, string targetPath, StringComparison comparison)
        {
            if (accountNode == null)
                return null;

            var volume = await GetVolumeForPathAsync(targetPath, comparison);
            var volumeRoot = volume != null ? NormalizePath(volume.MountPoint, comparison) : string.Empty;
            var volumeLabel = GetVolumeLabel(volume, targetPath);
            Logger.Debug("EnsureFileSystemPathChain: volumeRoot={VolumeRoot} volumeLabel={VolumeLabel}", volumeRoot, volumeLabel);
            var ancestorPaths = GetAncestorPaths(targetPath, comparison, volumeRoot);
            if (ancestorPaths.Count == 0)
                return accountNode;

            Logger.Debug("EnsureFileSystemPathChain: building chain for {TargetPath}. Depth={Depth}",
                targetPath,
                ancestorPaths.Count);

            var current = accountNode;
            foreach (var path in ancestorPaths)
            {
                var existing = FindChildByPath(current, path, comparison);
                if (existing == null)
                {
                    var isRoot = IsRootPath(path, comparison);
                    var dirName = isRoot && !string.IsNullOrEmpty(volumeLabel) ? volumeLabel : GetDisplayNameFromPath(path);
                    var dirModel = new DirectoryModel(isRoot)
                    {
                        Name = dirName,
                        Path = path
                    };
                    var nodeType = isRoot ? ItemType.Drive : ItemType.Directory;
                    var newNode = new TreeNodeModel(accountNode.Service, accountNode.Account, nodeType)
                    {
                        Label = dirName,
                        FileSystemObject = dirModel
                    };
                    newNode.NodeExpanded += OnNodeExpanded;
                    newNode.NeedsPopulate = true;
                    await AddChildNodeAsync(current, newNode);
                    existing = newNode;
                    Logger.Debug("EnsureFileSystemPathChain: inserted node {Label} path={Path} root={IsRoot}",
                        dirName,
                        path,
                        isRoot);
                }

                current = existing;
            }

            if (current.Children.Count == 0 && !current.IsHavingDummyChild)
                current.AddDummyChild();

            return current;
        }

        static List<string> GetAncestorPaths(string path, StringComparison comparison, string? stopAt)
        {
            var result = new List<string>();
            var current = NormalizePath(path, comparison);
            var stopPath = NormalizePath(stopAt, comparison);
            while (!string.IsNullOrEmpty(current))
            {
                result.Add(current);
                if (!string.IsNullOrEmpty(stopPath) && string.Equals(current, stopPath, comparison))
                    break;

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent))
                    break;

                var normalizedParent = NormalizePath(parent, comparison);
                if (string.Equals(normalizedParent, current, comparison))
                    break;

                current = normalizedParent;
            }

            result.Reverse();
            return result;
        }

        static TreeNodeModel? FindChildByPath(TreeNodeModel parent, string path, StringComparison comparison)
        {
            foreach (var child in parent.Children)
            {
                if (child.FileSystemObject is DirectoryModel dir)
                {
                    var childPath = NormalizePath(dir.Path, comparison);
                    if (!string.IsNullOrEmpty(childPath) && string.Equals(childPath, path, comparison))
                        return child;
                }
            }

            return null;
        }

        static bool IsRootPath(string path, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return false;

            return string.Equals(NormalizePath(path, comparison), NormalizePath(root, comparison), comparison);
        }

        static string GetDisplayNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(name))
                return name;

            return string.IsNullOrEmpty(trimmed) ? path : trimmed;
        }

        async Task<VolumeModel?> GetVolumeForPathAsync(string targetPath, StringComparison comparison)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return null;

            var now = DateTime.UtcNow;
            if (_volumeCacheService != null)
            {
                _volumeCacheService.EnsureFresh(TimeSpan.FromMinutes(5));
            }

            IReadOnlyList<VolumeModel>? volumes = null;
            if (_volumeCacheService != null)
            {
                Logger.Debug("GetVolumeForPath: querying volume cache snapshot.");
                volumes = await _volumeCacheService.GetVolumesSnapshotAsync().ConfigureAwait(false);
                Logger.Debug("GetVolumeForPath: volume cache snapshot count={Count}", volumes?.Count ?? 0);
            }

            volumes ??= Array.Empty<VolumeModel>();
            if (volumes.Count == 0)
            {
                Logger.Debug("GetVolumeForPath: cache empty. Querying FileSystem.GetVolumesAsync directly.");
                try
                {
                    volumes = await FileSystem.Default.GetVolumesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "GetVolumeForPath: failed to query volumes");
                    volumes = Array.Empty<VolumeModel>();
                }
            }

            var normalizedTarget = NormalizePath(targetPath, comparison);
            VolumeModel? best = null;
            var bestLength = -1;

            foreach (var volume in volumes)
            {
                var mount = NormalizePath(volume.MountPoint, comparison);
                if (string.IsNullOrEmpty(mount))
                    continue;

                if (IsPathPrefix(mount, normalizedTarget, comparison) && mount.Length > bestLength)
                {
                    best = volume;
                    bestLength = mount.Length;
                }
            }

            return best;
        }

        static string GetVolumeLabel(VolumeModel? volume, string? fallbackPath)
        {
            if (volume == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(volume.Name))
                return volume.Name;

            if (!string.IsNullOrWhiteSpace(volume.MountPoint))
            {
                var label = TryGetDriveLabel(volume.MountPoint);
                if (!string.IsNullOrWhiteSpace(label))
                    return label;
            }

            if (!string.IsNullOrWhiteSpace(fallbackPath))
            {
                try
                {
                    var root = Path.GetPathRoot(fallbackPath);
                    var label = TryGetDriveLabel(root);
                    if (!string.IsNullOrWhiteSpace(label))
                        return label;
                }
                catch { }
            }

            return volume.MountPoint ?? string.Empty;
        }

        static string? TryGetDriveLabel(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                var drive = new DriveInfo(path);
                if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                    return drive.VolumeLabel;
            }
            catch { }

            return null;
        }

        async Task PopulateNodeAsync(TreeNodeModel node, bool updateSelection)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            if (node.IsExpanded && !node.IsHavingDummyChild)
                return;

            Log.ForContext<NavigationViewModel>().Debug("PopulateAction start: NodeLabel={Label}, IsServiceRoot={IsServiceRoot}", node.Label, node.Service == null);

            if (node.Service == null)
            {
                TreeNodeModel? initialNode = null;
                LocationItemViewModel? initialLocation = null;

                // Populate Favorites as a separate collection (ListBox above the tree)
                try
                {
                    // Resolve file system provider and account (if available) so favorites navigate correctly
                    ProviderServiceBase? fileService = null;
                    AccountModelBase? fileAccount = null;
                    try
                    {
                        var providerService = GetService<ProviderService>();
                        var providers = providerService?.Providers;
                        if (providers != null)
                        {
                            foreach (var p in providers)
                            {
                                if (p is ProviderServiceBase ps && ps.Name == "File System")
                                {
                                    fileService = ps;
                                    break;
                                }
                            }
                        }

                        if (fileService != null)
                        {
                            var accounts = await fileService.GetAccountsAsync();
                            fileAccount = accounts?.FirstOrDefault();
                        }
                    }
                    catch { }

                    var favoritesLocal = new List<TreeNodeModel>();

                    // Populate Locations (Home, Downloads, Trash)
                    var locationsLocal = new List<LocationItemViewModel>();

                    // Home directory entry
                    var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var homeDir = new DirectoryModel { Path = homePath, Name = homePath };

                    TreeNodeModel homeNode;
                    if (fileService != null && fileAccount != null)
                    {
                        homeNode = new TreeNodeModel(fileService, fileAccount, ItemType.Directory)
                        {
                            Label = "Home",
                            FileSystemObject = homeDir
                        };
                    }
                    else
                    {
                        // Use ItemType.File to avoid directory expand glyph while still carrying FileSystemObject
                        homeNode = new TreeNodeModel(null, null, ItemType.File)
                        {
                            Label = "Home",
                            FileSystemObject = homeDir
                        };
                    }

                    favoritesLocal.Add(homeNode);

                    // Add locations Home
                    var homeLocation = new LocationItemViewModel
                    {
                        Label = Environment.UserName,
                        Directory = homeDir,
                        Service = fileService,
                        Account = fileAccount,
                        ImageResourceKey = "Icon.FolderOpen.Image"
                    };
                    locationsLocal.Add(homeLocation);

                    // Downloads entry
                    try
                    {
                        var downloadsPath = System.IO.Path.Combine(homePath, "Downloads");
                        var downloadsDir = new DirectoryModel { Path = downloadsPath, Name = downloadsPath };
                        TreeNodeModel downloadsNode;
                        if (fileService != null && fileAccount != null)
                        {
                            downloadsNode = new TreeNodeModel(fileService, fileAccount, ItemType.Directory)
                            {
                                Label = "Downloads",
                                FileSystemObject = downloadsDir
                            };
                        }
                        else
                        {
                            // Use ItemType.File to avoid directory expand glyph while still carrying FileSystemObject
                            downloadsNode = new TreeNodeModel(null, null, ItemType.File)
                            {
                                Label = "Downloads",
                                FileSystemObject = downloadsDir
                            };
                        }
                        favoritesLocal.Add(downloadsNode);

                        var downloadsLocation = new LocationItemViewModel
                        {
                            Label = "Downloads",
                            Directory = downloadsDir,
                            Service = fileService,
                            Account = fileAccount,
                            ImageResourceKey = "Icon.ArrowDown.Image"
                        };
                        locationsLocal.Add(downloadsLocation);
                    }
                    catch { }

                    // Trash entry (platform specific)
                    try
                    {
                        var trashPath = GetTrashPath();
                        if (!string.IsNullOrEmpty(trashPath))
                        {
                            var trashDir = new DirectoryModel { Path = trashPath, Name = trashPath };
                            var trashLocation = new LocationItemViewModel
                            {
                                Label = "Trash",
                                Directory = trashDir,
                                Service = fileService,
                                Account = fileAccount,
                                ImageResourceKey = "Icon.Delete.Image"
                            };
                            locationsLocal.Add(trashLocation);
                        }
                    }
                    catch { }

                    // Update the observable collection on UI thread
                    Invoke(() =>
                    {
                        Favorites.Clear();
                        if (favoritesLocal != null)
                        {
                            foreach (var f in favoritesLocal)
                                Favorites.Add(f);
                        }

                        // Populate Locations collection and set initial selection to Home
                        Locations.Clear();
                        foreach (var l in locationsLocal)
                            Locations.Add(l);

                        // Diagnostic logging: list the locations we populated (Label, Path, ImageResourceKey)
                        try
                        {
                            foreach (var loc in locationsLocal)
                            {
                                Logger.Information("Location populated: Label={Label}, Path={Path}, ImageResourceKey={Key}",
                                    loc.Label ?? string.Empty,
                                    loc.Directory?.Path ?? string.Empty,
                                    loc.ImageResourceKey ?? string.Empty);
                            }
                        }
                        catch { }
                    });
                    initialNode = homeNode;
                    initialLocation = locationsLocal.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Logger.Verbose(ex, "Failed to populate Favorites collection");
                }

                var providerService2 = GetService<ProviderService>();
                var providers2 = providerService2?.Providers;
                if (providers2 != null)
                {
                    foreach (var service in providers2)
                    {
                        var serviceInstance = service as ProviderServiceBase;

                        var serviceNode = new TreeNodeModel(service as ProviderServiceBase, null, ItemType.Service)
                        {
                            Label = service.Name, ImagePath = service.ImagePath
                        };
                        serviceNode.NodeExpanded += OnNodeExpanded;
                        serviceNode.AddDummyChild();
                        await AddChildNodeAsync(node, serviceNode);

                        if (serviceInstance != null)
                        {
                            serviceInstance.AccountAdded += (AccountModelBase account) => OnAccountAction(account, AccountAction.Added, serviceNode);
                            serviceInstance.AccountRemoved += (AccountModelBase account) => OnAccountAction(account, AccountAction.Removed, serviceNode);
                        }
                    }
                }

                // Log the top-tier nodes (services) for diagnostics with richer info
                try
                {
                    var topNodesDetailed = Node.Children.Select(n => new
                    {
                        Label = n.Label ?? n.ToString(),
                        ItemType = n.NodeType.ToString(),
                        ServiceType = n.Service?.GetType().FullName,
                        Assembly = n.Service?.GetType().Assembly.GetName().Name,
                        ProviderHash = n.Service?.GetHashCode()
                    }).ToArray();

                    Logger.Information("Navigation top-tier nodes detailed: {@Nodes}", topNodesDetailed);
                }
                catch (Exception ex)
                {
                    Logger.Verbose(ex, "Failed to log detailed navigation top-tier nodes");
                }

                if (updateSelection)
                {
                    Invoke(() =>
                    {
                        if (SelectedNode == null)
                        {
                            try { SelectedNode = initialNode; } catch { }
                        }

                        if (SelectedLocation == null)
                        {
                            try { SelectedLocation = initialLocation ?? Locations.FirstOrDefault(); } catch { }
                        }
                    });
                }
            }
            else if (node.Account == null)
            {
                var svc = node.Service;
                if (svc == null)
                    return;

                var accounts = await svc.GetAccountsAsync();
                foreach (var account in accounts)
                {
                    var accountNode = new TreeNodeModel(node.Service, account, node.Service.IsRootDrive ? ItemType.Computer : ItemType.Account);
                    accountNode.Label = account.Name;
                    accountNode.FileSystemObject = new DirectoryModel();
                    accountNode.ImagePath = account.ImagePath;
                    accountNode.NodeExpanded += OnNodeExpanded;
                    accountNode.AddDummyChild();
                    await AddChildNodeAsync(node, accountNode);
                }
            }
            else
            {
                var svc = node.Service;
                if (svc == null)
                    return;

                var currentDirectory = await svc.GetDirectoryAsync(node.Account, node.FileSystemObject as DirectoryModel);
                if (currentDirectory == null)
                {
                    node.RemoveDummyChild();
                    return;
                }

                foreach (var directory in currentDirectory.Directories)
                {
                    var fileSystemObjectNode = new TreeNodeModel(node.Service, node.Account, directory.Type == FileSystemObjectType.Drive ? ItemType.Drive : ItemType.Directory);
                    fileSystemObjectNode.Label = directory.Name;
                    fileSystemObjectNode.FileSystemObject = directory;
                    fileSystemObjectNode.NodeExpanded += OnNodeExpanded;
                    fileSystemObjectNode.AddDummyChild();
                    await AddChildNodeAsync(node, fileSystemObjectNode);
                }
            }

            node.RemoveDummyChild();
            node.NeedsPopulate = false;
        }
    }
}
