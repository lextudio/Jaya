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
using Avalonia;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Jaya.IO;
using Jaya.IO.Models;

namespace Jaya.Ui.ViewModels
{
    public class NavigationViewModel : ViewModelBase
    {
        readonly SharedService? _shared;
        static readonly ILogger Logger = Log.ForContext(typeof(NavigationViewModel)).ForContext("SourceContext", "ViewModels");
        ICommand? _populateCommand;
        ICommand? _selectLocationCommand;
        
        TreeNodeModel? _selectedNode;
        bool _suppressPublish;
        bool _suppressPopulateOnExpand;
        readonly VolumeCacheService? _volumeCacheService;
        ObservableCollection<LocationItemViewModel>? _favorites = new();
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
            {
                if (_volumeCacheService != null)
                    _volumeCacheService.VolumesChanged += OnVolumesChanged;
            }

            if (!IsDesignMode)
                PopulateCommand?.Execute(Node);
        }

        Bitmap? LoadBitmapFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                var uri = new Uri(path, UriKind.RelativeOrAbsolute);
                
                // Handle avares:// embedded resources using AssetLoader.Open
                if (uri.IsAbsoluteUri && uri.Scheme == "avares")
                {
                    Logger.Debug("Attempting to load avares resource: {Path}", path);
                    
                    try
                    {
                        // Use Avalonia's AssetLoader.Open static method (per Avalonia docs)
                        using (var stream = AssetLoader.Open(uri))
                        {
                            var bitmap = new Bitmap(stream);
                            Logger.Debug("Successfully loaded avares resource via AssetLoader.Open: {Path}", path);
                            return bitmap;
                        }
                    }
                    catch (Exception avEx)
                    {
                        Logger.Warning(avEx, "Failed to load avares resource via AssetLoader.Open: {Path}", path);
                        return null;
                    }
                }
                
                // Handle file:// or relative paths
                var bmp = new Bitmap(path);
                Logger.Debug("Successfully loaded bitmap from path: {Path}", path);
                return bmp;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to load bitmap from path: {Path}", path);
                return null;
            }
        }

        ~NavigationViewModel()
        {
            if (_volumeCacheService != null)
                _volumeCacheService.VolumesChanged -= OnVolumesChanged;
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

        public ObservableCollection<LocationItemViewModel> Favorites
        {
            get => _favorites ?? (_favorites = new ObservableCollection<LocationItemViewModel>());
            private set => Set(ref _favorites, value ?? new ObservableCollection<LocationItemViewModel>());
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
            Logger.Debug("Node {ExpandedState}: Label={Label}, Service={Service}, Path={Path}",
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
                    Logger.Debug("Child added: Parent={Parent}, Child={Child}, Path={Path}",
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
                    Logger.Debug("Child added: Parent={Parent}, Child={Child}, Path={Path}",
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
                accountNode.Image = account.Image;
                    if (accountNode.Image == null && !string.IsNullOrEmpty(accountNode.ImagePath))
                        accountNode.Image = LoadBitmapFromPath(accountNode.ImagePath);
                accountNode.NodeExpanded += OnNodeExpanded;
                accountNode.AddDummyChild();
                Logger.Debug("Created Account Node: Label={Label}, NodeType={NodeType}, IsAccount={IsAccount}, ImagePath={ImagePath}", 
                    accountNode.Label, accountNode.NodeType, accountNode.IsAccount, accountNode.ImagePath);
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

        void OnVolumesChanged(object? sender, VolumeCacheChangedEventArgs e)
        {
            if (IsDesignMode)
                return;

            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await RefreshFileSystemVolumeNodesAsync(e.Volumes);
                    // Also refresh the Locations list to include current volumes
                    try { await UpdateLocationsWithVolumesAsync(e.Volumes).ConfigureAwait(false); } catch { }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Volume cache change handling failed");
                }
            });
        }

        async Task UpdateLocationsWithVolumesAsync(IReadOnlyList<VolumeModel>? volumes)
        {
            if (IsDesignMode)
                return;

            if (volumes == null || volumes.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // Preserve Home and Trash entries already in Locations; insert volumes after Home
                    var existingHome = Locations.FirstOrDefault(l => string.Equals(l.Directory?.Path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
                    var trashPath = GetTrashPath();

                    // Build new list: Home, volumes..., Trash (if exists)
                    var newLocations = new List<LocationItemViewModel>();
                    if (existingHome != null)
                        newLocations.Add(existingHome);

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var v in volumes)
                    {
                        var mount = v.MountPoint ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(mount) || !seen.Add(mount))
                            continue;

                        var lower = mount.ToLowerInvariant();
                        if (lower.StartsWith("/proc") || lower.StartsWith("/sys") || lower.StartsWith("/run") || lower.StartsWith("/dev") || lower.StartsWith("/var") || lower.StartsWith("/private"))
                            continue;
                        if (lower.Contains("/snap/") || lower.Contains("/containers/") || lower.Contains("/core") || lower.Contains("/gvfs"))
                            continue;
                        if (!v.IsInternal && !v.IsRemovable && string.IsNullOrWhiteSpace(v.Name))
                            continue;

                        var label = GetVolumeDisplayName(v);
                        var volDir = new DirectoryModel { Path = mount, Name = label };
                        var volLocation = new LocationItemViewModel
                        {
                            Label = label,
                            Directory = volDir,
                            Service = existingHome?.Service,
                            Account = existingHome?.Account,
                            IconType = LocationIconType.Drive
                        };
                        newLocations.Add(volLocation);
                    }

                    if (!string.IsNullOrEmpty(trashPath))
                    {
                        var trashDir = new DirectoryModel { Path = trashPath, Name = trashPath };
                        var trashLocation = new LocationItemViewModel
                        {
                            Label = "Trash",
                            Directory = trashDir,
                            Service = existingHome?.Service,
                            Account = existingHome?.Account,
                            IconType = LocationIconType.Trash
                        };
                        newLocations.Add(trashLocation);
                    }

                    // Replace Locations collection contents
                    Locations.Clear();
                    foreach (var loc in newLocations)
                        Locations.Add(loc);
                }
                catch { }
            });
        }

        async Task RefreshFileSystemVolumeNodesAsync(IReadOnlyList<VolumeModel> volumes)
        {
            if (volumes == null || volumes.Count == 0)
                return;

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var comparer = comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var volumeByMount = new Dictionary<string, VolumeModel>(comparer);
            foreach (var volume in volumes)
            {
                var mount = NormalizePath(volume.MountPoint, comparison);
                if (string.IsNullOrEmpty(mount) || volumeByMount.ContainsKey(mount))
                    continue;

                volumeByMount.Add(mount, volume);
            }

            if (volumeByMount.Count == 0)
                return;

            var fileSystemServices = Node.Children.Where(n => IsFileSystemService(n.Service)).ToList();
            foreach (var serviceNode in fileSystemServices)
            {
                foreach (var accountNode in serviceNode.Children.ToList())
                    await UpdateAccountVolumeNodesAsync(accountNode, volumeByMount, comparison);
            }
        }

        async Task UpdateAccountVolumeNodesAsync(
            TreeNodeModel accountNode,
            Dictionary<string, VolumeModel> volumeByMount,
            StringComparison comparison)
        {
            if (accountNode == null)
                return;

            var comparer = volumeByMount.Comparer;
            var existingDriveNodes = new Dictionary<string, TreeNodeModel>(comparer);
            foreach (var child in accountNode.Children)
            {
                if (child.FileSystemObject is not DirectoryModel dir || dir.Type != FileSystemObjectType.Drive)
                    continue;

                var mount = NormalizePath(dir.Path, comparison);
                if (!string.IsNullOrEmpty(mount))
                    existingDriveNodes[mount] = child;
            }

            foreach (var pair in volumeByMount)
            {
                var mount = pair.Key;
                var volume = pair.Value;
                if (existingDriveNodes.TryGetValue(mount, out var node))
                {
                    var label = GetVolumeDisplayName(volume);
                    if (!string.Equals(node.Label, label, StringComparison.Ordinal))
                        node.Label = label;

                    if (node.FileSystemObject is DirectoryModel dir)
                    {
                        if (!string.Equals(dir.Name, label, StringComparison.Ordinal))
                            dir.Name = label;
                        dir.IsExternalDrive = volume.IsRemovable || !volume.IsInternal;
                    }

                    continue;
                }

                if (accountNode.IsHavingDummyChild && accountNode.Children.Count == 1 && accountNode.NeedsPopulate)
                    continue;

                var driveModel = new DirectoryModel(true)
                {
                    Name = GetVolumeDisplayName(volume),
                    Path = volume.MountPoint,
                    IsExternalDrive = volume.IsRemovable || !volume.IsInternal
                };
                var driveNode = new TreeNodeModel(accountNode.Service, accountNode.Account, ItemType.Drive)
                {
                    Label = driveModel.Name,
                    FileSystemObject = driveModel
                };
                driveNode.NodeExpanded += OnNodeExpanded;
                driveNode.AddDummyChild();
                    if (driveNode.Image == null && !string.IsNullOrEmpty(driveNode.ImagePath))
                        driveNode.Image = LoadBitmapFromPath(driveNode.ImagePath);
                await AddChildNodeAsync(accountNode, driveNode);
            }

            if (existingDriveNodes.Count == 0)
                return;

            var selectedNode = SelectedNode;
            var selectedPath = (selectedNode?.FileSystemObject as DirectoryModel)?.Path;
            foreach (var pair in existingDriveNodes)
            {
                if (volumeByMount.ContainsKey(pair.Key))
                    continue;

                var node = pair.Value;
                if (ReferenceEquals(node, selectedNode))
                    continue;

                if (!string.IsNullOrEmpty(selectedPath) && IsPathPrefix(pair.Key, NormalizePath(selectedPath, comparison), comparison))
                    continue;

                Logger.Debug("Removing missing drive node: Label={Label}, Path={Path}", node.Label, (node.FileSystemObject as DirectoryModel)?.Path);
                RemoveChildNode(accountNode, node);
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

        static string GetVolumeDisplayName(VolumeModel volume)
        {
            if (!string.IsNullOrWhiteSpace(volume.Name))
                return volume.Name ?? string.Empty;

            var trimmed = volume.MountPoint?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(trimmed))
                return Path.GetFileName(trimmed);

            return volume.MountPoint ?? string.Empty;
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

                    var favoritesLocal = new List<LocationItemViewModel>();

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

                    // Add Home to Locations (Home stays in Locations, above Trash)
                    var homeLocation = new LocationItemViewModel
                    {
                        Label = Environment.UserName,
                        Directory = homeDir,
                        Service = fileService,
                        Account = fileAccount,
                        IconType = LocationIconType.Folder
                    };
                    locationsLocal.Add(homeLocation);

                    // Add mounted volumes (drives) to Locations after Home
                    try
                    {
                        IReadOnlyList<VolumeModel>? vols = null;
                        if (_volumeCacheService != null)
                        {
                            vols = await _volumeCacheService.GetFilteredVolumesSnapshotAsync().ConfigureAwait(false);
                        }

                        if (vols == null || vols.Count == 0)
                        {
                            try
                            {
                                var raw = await FileSystem.Default.GetVolumesAsync().ConfigureAwait(false) ?? Array.Empty<VolumeModel>();
                                vols = raw.Count > 0 ? raw : Array.Empty<VolumeModel>();
                            }
                            catch { vols = Array.Empty<VolumeModel>(); }
                        }

                        if (vols != null && vols.Count > 0)
                        {
                            foreach (var vol in vols)
                            {
                                var mount = vol.MountPoint ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(mount))
                                    continue;

                                var label = GetVolumeDisplayName(vol);
                                var volDir = new DirectoryModel { Path = mount, Name = label };
                                var volLocation = new LocationItemViewModel
                                {
                                    Label = label,
                                    Directory = volDir,
                                    Service = fileService,
                                    Account = fileAccount,
                                    IconType = LocationIconType.Drive
                                };
                                locationsLocal.Add(volLocation);
                            }
                        }
                    }
                    catch { }

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
                        // Place Downloads in Favorites (above Locations) — add LocationItemViewModel
                        var downloadsLocation = new LocationItemViewModel
                        {
                            Label = "Downloads",
                            Directory = downloadsDir,
                            Service = fileService,
                            Account = fileAccount,
                            IconType = LocationIconType.Download
                        };
                        favoritesLocal.Add(downloadsLocation);
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
                                IconType = LocationIconType.Trash
                            };
                            locationsLocal.Add(trashLocation);
                        }
                    }
                    catch { }

                    // Add Desktop to favorites
                    try
                    {
                        var desktopPath = System.IO.Path.Combine(homePath, "Desktop");
                        var desktopDir = new DirectoryModel { Path = desktopPath, Name = desktopPath };
                        TreeNodeModel desktopNode;
                        if (fileService != null && fileAccount != null)
                        {
                            desktopNode = new TreeNodeModel(fileService, fileAccount, ItemType.Directory)
                            {
                                Label = "Desktop",
                                FileSystemObject = desktopDir
                            };
                        }
                        else
                        {
                            desktopNode = new TreeNodeModel(null, null, ItemType.File)
                            {
                                Label = "Desktop",
                                FileSystemObject = desktopDir
                            };
                        }
                        // Add Desktop as a favorite LocationItem
                        var desktopLocation = new LocationItemViewModel
                        {
                            Label = "Desktop",
                            Directory = desktopDir,
                            Service = fileService,
                            Account = fileAccount,
                            IconType = LocationIconType.Computer
                        };
                        favoritesLocal.Add(desktopLocation);
                    }
                    catch { }

                    // Try to add cache volume to Locations (if available in volume cache)
                    try
                    {
                        if (_volumeCacheService != null)
                        {
                            var vols = await _volumeCacheService.GetFilteredVolumesSnapshotAsync().ConfigureAwait(false);
                            if (vols != null)
                            {
                                var cacheVol = vols.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.Name) && v.Name.IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0);
                                if (cacheVol == null)
                                    cacheVol = vols.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.MountPoint) && v.MountPoint.IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0);

                                if (cacheVol != null)
                                {
                                    var cacheDir = new DirectoryModel { Path = cacheVol.MountPoint, Name = GetVolumeDisplayName(cacheVol) };
                                    var cacheLocation = new LocationItemViewModel
                                    {
                                        Label = "Cache",
                                        Directory = cacheDir,
                                        Service = fileService,
                                        Account = fileAccount,
                                        IconType = LocationIconType.Drive
                                    };
                                    locationsLocal.Add(cacheLocation);
                                }
                            }
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

                        // log provider discovered
                        Logger.Debug("Discovered provider: Name={Name}, Type={Type}, IsEnabled={IsEnabled}",
                            service?.Name ?? string.Empty,
                            service?.GetType().Name ?? string.Empty,
                            serviceInstance?.IsEnabled ?? false);

                        // skip disabled providers
                        if (serviceInstance != null && !serviceInstance.IsEnabled)
                        {
                            Logger.Debug("Skipping disabled provider: Name={Name}, Type={Type}", service?.Name, service?.GetType().Name);
                            continue;
                        }

                        var svc = service;
                        var serviceNode = new TreeNodeModel(svc as ProviderServiceBase, null, ItemType.Service)
                        {
                            Label = svc?.Name ?? string.Empty,
                            ImagePath = svc?.ImagePath ?? string.Empty,
                            Image = (svc as ProviderServiceBase)?.Image
                        };
                        Logger.Debug("Created Service Node: Label={Label}, NodeType={NodeType}, IsService={IsService}, ImagePath={ImagePath}", 
                            serviceNode.Label, serviceNode.NodeType, serviceNode.IsService, serviceNode.ImagePath);
                        if (serviceNode.Image == null && !string.IsNullOrEmpty(serviceNode.ImagePath))
                        {
                            Logger.Debug("Service node Image is null; attempting fallback load from ImagePath: {ImagePath}", serviceNode.ImagePath);
                            serviceNode.Image = LoadBitmapFromPath(serviceNode.ImagePath);
                            if (serviceNode.Image != null)
                                Logger.Debug("Fallback load succeeded for service node: {Label}", serviceNode.Label);
                            else
                                Logger.Warning("Fallback load failed for service node: Label={Label}, ImagePath={ImagePath}", serviceNode.Label, serviceNode.ImagePath);
                        }
                        serviceNode.NodeExpanded += OnNodeExpanded;
                        serviceNode.AddDummyChild();
                        await AddChildNodeAsync(node, serviceNode);

                        if (serviceInstance != null)
                        {
                            serviceInstance.AccountAdded += (AccountModelBase account) => OnAccountAction(account, AccountAction.Added, serviceNode);
                            serviceInstance.AccountRemoved += (AccountModelBase account) => OnAccountAction(account, AccountAction.Removed, serviceNode);

                            // subscribe to IsEnabled changes so we can add/remove nodes dynamically
                            serviceInstance.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(ProviderServiceBase.IsEnabled))
                                {
                                    // log the change
                                    Logger.Debug("Provider IsEnabled changed: Name={Name}, NewValue={IsEnabled}", serviceInstance.Name, serviceInstance.IsEnabled);

                                    // run on UI thread
                                    Invoke(async () =>
                                    {
                                        if (serviceInstance.IsEnabled)
                                        {
                                            Logger.Debug("Enabling provider node: Name={Name}", serviceInstance.Name);
                                            // add node if it doesn't exist
                                            var exists = Node?.Children != null && Node.Children.Any(n => (n.Service?.GetHashCode() ?? 0) == serviceInstance.GetHashCode());
                                            if (!exists)
                                            {
                                                var newNode = new TreeNodeModel(serviceInstance, null, ItemType.Service)
                                                {
                                                    Label = serviceInstance.Name,
                                                    ImagePath = serviceInstance.ImagePath,
                                                    Image = serviceInstance.Image
                                                };
                                                if (newNode.Image == null && !string.IsNullOrEmpty(newNode.ImagePath))
                                                {
                                                    Logger.Debug("Dynamic enable: node Image is null; attempting fallback load from ImagePath: {ImagePath}", newNode.ImagePath);
                                                    newNode.Image = LoadBitmapFromPath(newNode.ImagePath);
                                                    if (newNode.Image != null)
                                                        Logger.Debug("Dynamic enable: fallback load succeeded for: {Label}", newNode.Label);
                                                    else
                                                        Logger.Warning("Dynamic enable: fallback load failed for: Label={Label}, ImagePath={ImagePath}", newNode.Label, newNode.ImagePath);
                                                }
                                                newNode.NodeExpanded += OnNodeExpanded;
                                                newNode.AddDummyChild();
                                                var parentNode = Node;
                                                if (parentNode != null)
                                                    await AddChildNodeAsync(parentNode, newNode);
                                            }
                                        }
                                        else
                                        {
                                            Logger.Debug("Disabling provider node: Name={Name}", serviceInstance.Name);
                                            // remove existing node(s)
                                            var toRemove = Node?.Children?.Where(n => (n.Service?.GetHashCode() ?? 0) == serviceInstance.GetHashCode()).ToList() ?? new System.Collections.Generic.List<TreeNodeModel>();
                                            var parentNode2 = Node;
                                            if (parentNode2 != null)
                                            {
                                                foreach (var rem in toRemove)
                                                    RemoveChildNode(parentNode2, rem);
                                            }
                                        }
                                    });
                                }
                            };
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

                    Logger.Debug("Navigation top-tier nodes detailed: {@Nodes}", topNodesDetailed);
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
                    accountNode.Image = account.Image;
                    accountNode.NodeExpanded += OnNodeExpanded;
                    accountNode.AddDummyChild();
                    var isComputer = node.Service.IsRootDrive;
                    Logger.Debug("Created {NodeTypeLabel} Node in PopulateNodeAsync: Label={Label}, NodeType={NodeType}, IsComputer={IsComputer}, IsAccount={IsAccount}, ImagePath={ImagePath}", 
                        isComputer ? "Computer" : "Account",
                        accountNode.Label, accountNode.NodeType, accountNode.IsComputer, accountNode.IsAccount, accountNode.ImagePath);
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
                    Logger.Debug("Created {NodeTypeLabel} Node: Label={Label}, NodeType={NodeType}, IsDrive={IsDrive}, IsDirectory={IsDirectory}", 
                        directory.Type == FileSystemObjectType.Drive ? "Drive" : "Directory",
                        fileSystemObjectNode.Label, fileSystemObjectNode.NodeType, fileSystemObjectNode.IsDrive, fileSystemObjectNode.IsDirectory);
                    await AddChildNodeAsync(node, fileSystemObjectNode);
                }
            }

            node.RemoveDummyChild();
            node.NeedsPopulate = false;
        }
    }
}
