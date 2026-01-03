//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Serilog;
using System.Linq;
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
        readonly SharedService _shared;
        static readonly ILogger Logger = Log.ForContext<NavigationViewModel>();
        ICommand _populateCommand;
        TreeNodeModel _selectedNode;

        public NavigationViewModel()
        {
            _shared = GetService<SharedService>();

            Node = new TreeNodeModel(null, null, null);
            if (!IsDesignMode)
                PopulateCommand.Execute(Node);
        }

        #region properties

        public ICommand PopulateCommand
        {
            get
            {
                if (_populateCommand == null)
                    _populateCommand = new RelayCommand<TreeNodeModel>(PopulateAction);

                return _populateCommand;
            }
        }

        public PaneConfigModel PaneConfig => _shared.PaneConfiguration;

        public ApplicationConfigModel ApplicationConfig => _shared.ApplicationConfiguration;

        public TreeNodeModel Node { get; }

        public TreeNodeModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                _selectedNode = value;

                if (value == null)
                    return;

                Log.ForContext<NavigationViewModel>().Information("Node selected: Label={Label}, Service={Service}, Path={Path}",
                    value.Label, value.Service?.Name, (value.FileSystemObject as DirectoryModel)?.Path);

                var args = new SelectionChangedEventArgs(value.Service, value.Account, value.FileSystemObject as DirectoryModel);
                EventAggregator.Publish(args);
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

            if (node.IsHavingDummyChild)
                PopulateCommand.Execute(node);
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
                foreach (var service in GetService<ProviderService>().Providers)
                {
                    var serviceInstance = service as ProviderServiceBase;

                    var serviceNode = new TreeNodeModel(service as ProviderServiceBase, null, ItemType.Service)
                    {
                        Label = service.Name, ImagePath = service.ImagePath
                    };
                    serviceNode.NodeExpanded += OnNodeExpanded;
                    serviceNode.AddDummyChild();
                    AddChildNode(node, serviceNode);

                    serviceInstance.AccountAdded += (AccountModelBase account) => OnAccountAction(account, AccountAction.Added, serviceNode);
                    serviceInstance.AccountRemoved += (AccountModelBase account) => OnAccountAction(account, AccountAction.Removed, serviceNode);
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
                var accounts = await node.Service.GetAccountsAsync();
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
                var currentDirectory = await node.Service.GetDirectoryAsync(node.Account, node.FileSystemObject as DirectoryModel);
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
                    if (!accountNode.Account.Equals(account))
                        continue;

                    RemoveChildNode(node, accountNode);
                    break;
                }
            }
        }
    }
}
