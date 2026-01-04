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
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public class NavigationViewModel : ViewModelBase
    {
        readonly SharedService? _shared;
        readonly Subscription<SelectionChangedEventArgs>? _onSelectionChanged;
        static readonly ILogger Logger = Log.ForContext<NavigationViewModel>();
        ICommand? _populateCommand;
        TreeNodeModel? _selectedNode;
        bool _suppressPublish;
        ObservableCollection<TreeNodeModel>? _favorites = new();

        public NavigationViewModel()
        {
            _shared = GetService<SharedService>();

            Node = new TreeNodeModel(null, null, null);
            Favorites = _favorites;
            if (!IsDesignMode)
                PopulateCommand?.Execute(Node);

            if (!IsDesignMode)
                _onSelectionChanged = EventAggregator?.Subscribe<SelectionChangedEventArgs>(OnExternalSelectionChanged);
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

            if (node.IsHavingDummyChild)
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
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            if (node.IsExpanded && !node.IsHavingDummyChild)
                return;

            Log.ForContext<NavigationViewModel>().Debug("PopulateAction start: NodeLabel={Label}, IsServiceRoot={IsServiceRoot}", node.Label, node.Service == null);

            if (node.Service == null)
            {
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

                        // Set initial selection to Home so app opens there — publish selection so Explorer loads it
                        try { SelectedNode = homeNode; } catch { }
                    });
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
                        AddChildNode(node, serviceNode);

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
                    AddChildNode(node, accountNode);
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
                    AddChildNode(node, fileSystemObjectNode);
                }
            }

            node.RemoveDummyChild();
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

        void OnExternalSelectionChanged(SelectionChangedEventArgs args)
        {
            if (args == null)
                return;

            // Find the node matching the service/account/directory
            var target = FindNodeForSelection(Node, args.Service, args.Account, args.Directory);
            if (target != null)
            {
                // Expand ancestors so the node becomes visible in the tree
                ExpandAncestors(Node, target);

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
            }
            else
            {
                Logger.Debug("OnExternalSelectionChanged: matching navigation node not found for Service={Service}, Account={Account}, Directory={Directory}",
                    args.Service?.Name, args.Account?.Name, args.Directory?.Path ?? args.Directory?.Name);
            }
        }

        TreeNodeModel? FindNodeForSelection(TreeNodeModel root, ProviderServiceBase? service, AccountModelBase? account, DirectoryModel? directory)
        {
            if (root == null)
                return null;

            // Check current node
            if (Equals(root.Service, service) && Equals(root.Account, account))
            {
                if (directory == null && (root.FileSystemObject == null || string.IsNullOrEmpty((root.FileSystemObject as DirectoryModel)?.Path)))
                    return root;

                if (directory != null && root.FileSystemObject is DirectoryModel d && string.Equals(d.Path, directory.Path))
                    return root;
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

        void ExpandAncestors(TreeNodeModel root, TreeNodeModel target)
        {
            if (root == null || target == null)
                return;

            // If target is a direct child, expand root and return
            if (root.Children.Contains(target))
            {
                root.IsExpanded = true;
                return;
            }

            foreach (var child in root.Children)
            {
                ExpandAncestors(child, target);
                if (child.IsExpanded && (child.Children.Contains(target) || child.Children.Count > 0 && child.Children.Contains(target)))
                {
                    root.IsExpanded = true;
                    return;
                }
            }
        }
    }
}
