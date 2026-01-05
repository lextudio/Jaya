//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Jaya.Shared.Models;
using Jaya.Ui;
using Jaya.Shared.Base;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Jaya.Ui.ViewModels;
using Jaya.Shared;
using Jaya.Shared.Services;
using Avalonia.Threading;
using Serilog;

namespace Jaya.Ui.Views
{
    public partial class ExplorerView : UserControl
    {
        static readonly ILogger Logger = Log.ForContext<ExplorerView>();
        Subscription<OpenRequestedEventArgs>? _openRequested;
        Subscription<CutRequestedEventArgs>? _cutRequested;
        Subscription<CopyRequestedEventArgs>? _copyRequested;
        Subscription<PasteRequestedEventArgs>? _pasteRequested;
        Subscription<DeleteRequestedEventArgs>? _deleteRequested;
        Subscription<SelectItemsRequestedEventArgs>? _selectItemsRequested;

        public ExplorerView()
        {
            this.InitializeComponent();
            this.AddHandler(KeyDownEvent, ExplorerView_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, ExplorerView_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(ContextRequestedEvent, ExplorerView_ContextRequested, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            if (!Design.IsDesignMode)
            {
                // Attach MenuItem click handlers to close context menus immediately when an item is clicked.
                this.AttachedToVisualTree += (s, e) =>
                {
                    try
                    {
                        AttachMenuItemHandlers(this);
                    }
                    catch { }
                };
                var eventAggregator = ServiceLocator.Instance.GetService<ICommandService>()?.EventAggregator;
                if (eventAggregator != null)
                {
                    _openRequested = eventAggregator.Subscribe<OpenRequestedEventArgs>(args =>
                {
                    // Attempt to open the selected item(s) by invoking ViewModel command on UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var vm = DataContext as ExplorerViewModel;
                            if (vm == null)
                                return;

                            // Prefer selected item from details grid, then listboxes
                            var selected = DetailsDataGrid?.SelectedItem as Models.ExplorerItemModel
                                           ?? ListListBox?.SelectedItem as Models.ExplorerItemModel
                                           ?? IconsListBox?.SelectedItem as Models.ExplorerItemModel
                                           ?? TilesListBox?.SelectedItem as Models.ExplorerItemModel
                                           ?? ContentListBox?.SelectedItem as Models.ExplorerItemModel;

                            if (selected != null && vm?.InvokeObjectCommand != null)
                                vm.InvokeObjectCommand.Execute(selected);
                        }
                        catch { }
                    });
                });
                    _cutRequested = eventAggregator.Subscribe<CutRequestedEventArgs>(args =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var vm = DataContext as ExplorerViewModel;
                            if (vm == null)
                                return;

                            var selectedItems = GetSelectedItems();
                            if (selectedItems.Count > 0 && vm?.CutItemsCommand != null)
                                vm.CutItemsCommand.Execute(selectedItems);
                        }
                        catch { }
                    });
                });
                    _copyRequested = eventAggregator.Subscribe<CopyRequestedEventArgs>(args =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var vm = DataContext as ExplorerViewModel;
                            if (vm == null)
                                return;

                            var selectedItems = GetSelectedItems();
                            if (selectedItems.Count > 0 && vm?.CopyItemsCommand != null)
                                vm.CopyItemsCommand.Execute(selectedItems);
                        }
                        catch { }
                    });
                });
                    _pasteRequested = eventAggregator.Subscribe<PasteRequestedEventArgs>(args =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var vm = DataContext as ExplorerViewModel;
                            if (vm == null)
                                return;

                            if (vm?.PasteItemsCommand != null)
                                vm.PasteItemsCommand.Execute(null);
                        }
                        catch { }
                    });
                });
                    _deleteRequested = eventAggregator.Subscribe<DeleteRequestedEventArgs>(args =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var vm = DataContext as ExplorerViewModel;
                            if (vm == null)
                            {
                                Logger.Debug("DeleteRequested ignored: ExplorerView DataContext not ready.");
                                return;
                            }

                            var details = DetailsDataGrid ?? this.FindControl<DataGrid>("DetailsDataGrid");
                            var list = ListListBox ?? this.FindControl<ListBox>("ListListBox");
                            var icons = IconsListBox ?? this.FindControl<ListBox>("IconsListBox");
                            var tiles = TilesListBox ?? this.FindControl<ListBox>("TilesListBox");
                            var content = ContentListBox ?? this.FindControl<ListBox>("ContentListBox");

                            Logger.Debug("DeleteRequested received. View visible: Details={Details} List={List} Icons={Icons} Tiles={Tiles} Content={Content}",
                                details?.IsVisible,
                                list?.IsVisible,
                                icons?.IsVisible,
                                tiles?.IsVisible,
                                content?.IsVisible);

                            var selectedItems = GetSelectedItems();
                            Logger.Debug("DeleteRequested selection count={Count} items={Items}",
                                selectedItems.Count,
                                DescribeSelection(selectedItems));
                            if (selectedItems.Count > 0 && vm?.DeleteItemsCommand != null)
                                vm.DeleteItemsCommand.Execute(selectedItems);
                        }
                        catch { }
                    });
                });
                _selectItemsRequested = eventAggregator.Subscribe<SelectItemsRequestedEventArgs>(args =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (args == null || args.Paths == null || args.Paths.Count == 0)
                                return;

                            ApplySelection(args.Paths);
                            // After selecting items, if any selected model is in editing state,
                            // attempt to focus its inline TextBox so user can start typing immediately.
                            try
                            {
                                var vm = DataContext as ExplorerViewModel;
                                if (vm != null && vm.Item?.Children != null)
                                {
                                    var editing = vm.Item.Children.FirstOrDefault(c => c.IsEditing);
                                    if (editing != null)
                                    {
                                        // search visual tree for a TextBox whose DataContext matches the editing model
                                        var tb = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(textBox => ReferenceEquals(textBox.DataContext, editing));
                                        if (tb != null)
                                        {
                                            try { tb.Focus(); } catch { }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        catch { }
                    });
                });

                    DetachedFromVisualTree += ExplorerView_DetachedFromVisualTree;
                }
            }
        }

        void ExplorerView_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            try
            {
                if (e == null)
                    return;

                if (e.Key == Avalonia.Input.Key.Delete)
                {
                    // If focus is inside a TextBox that is used for inline editing (EditableName), ignore delete
                    var focused = Avalonia.Controls.TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Avalonia.Controls.Control;
                    if (focused is Avalonia.Controls.TextBox tb)
                    {
                        // assume inline edit TextBoxes have Tag bound to the model or share class names; check DataContext
                        var dc = tb.DataContext;
                        if (dc is Jaya.Ui.Models.ExplorerItemModel)
                        {
                            // user is editing filename — do not trigger delete
                            e.Handled = true;
                            return;
                        }
                    }

                    // Otherwise, invoke the delete command via ViewModel
                    var vm = this.DataContext as Jaya.Ui.ViewModels.ExplorerViewModel;
                    if (vm != null)
                    {
                        if (vm.SimpleCommand != null)
                        {
                            // Command parameter for delete
                            vm.SimpleCommand.Execute((byte)Jaya.Ui.CommandType.Delete);
                            e.Handled = true;
                        }
                    }
                }
            }
            catch { }
        }

        void ExplorerView_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                if (e == null)
                    return;

                var point = e.GetCurrentPoint(this);
                if (point.Properties.IsLeftButtonPressed)
                    CloseOpenContextMenus();

                // If focus is inside an inline TextBox and the pointer press occurred outside
                // of any TextBox within the items area, move focus to a safe focus target
                // so the inline editor loses focus and commits/cancels via LostFocus.
                var top = Avalonia.Controls.TopLevel.GetTopLevel(this);
                var focused = top?.FocusManager?.GetFocusedElement() as Avalonia.Controls.Control;
                if (focused is Avalonia.Controls.TextBox tb)
                {
                    // Use the event's Source (the visual that received the pointer) and walk up
                    // the visual parent chain to see whether we clicked inside the same TextBox.
                    var visual = e.Source as Avalonia.Visual;
                    bool clickedInsideTextBox = false;
                    while (visual != null)
                    {
                        if (ReferenceEquals(visual, tb))
                        {
                            clickedInsideTextBox = true;
                            break;
                        }
                        visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual) as Avalonia.Visual;
                    }

                    if (!clickedInsideTextBox)
                    {
                        // Prefer focusing a visible items control to keep keyboard navigation sensible
                        Avalonia.Controls.Control? focusTarget = null;
                        if (DetailsDataGrid?.IsVisible == true) focusTarget = DetailsDataGrid;
                        else if (ListListBox?.IsVisible == true) focusTarget = ListListBox;
                        else if (IconsListBox?.IsVisible == true) focusTarget = IconsListBox;
                        else if (TilesListBox?.IsVisible == true) focusTarget = TilesListBox;
                        else if (ContentListBox?.IsVisible == true) focusTarget = ContentListBox;
                        else focusTarget = this;

                        try { focusTarget?.Focus(); } catch { }
                    }
                }
            }
            catch { }
        }

        void ExplorerView_ContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            try
            {
                if (e == null)
                    return;

                if (IsWithinItem(e.Source as Avalonia.Visual))
                    return;

                if (EmptySpaceMenu == null)
                    return;

                if (e.TryGetPosition(this, out var point))
                {
                    EmptySpaceMenu.PlacementTarget = this;
                    EmptySpaceMenu.PlacementRect = new Avalonia.Rect(point, new Avalonia.Size(1, 1));
                }

                EmptySpaceMenu.Open(this);
                e.Handled = true;
            }
            catch { }
        }

        static bool IsWithinItem(Avalonia.Visual? visual)
        {
            while (visual != null)
            {
                if (visual is DataGridRow || visual is ListBoxItem)
                    return true;

                visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual) as Avalonia.Visual;
            }

            return false;
        }

        void CloseOpenContextMenus()
        {
            try
            {
                if (ContextMenu is ContextMenu rootMenu && rootMenu.IsOpen)
                {
                    rootMenu.Close();
                    return;
                }

                foreach (var control in this.GetVisualDescendants().OfType<Control>())
                {
                    if (control.ContextMenu is ContextMenu menu && menu.IsOpen)
                    {
                        menu.Close();
                        break;
                    }
                }
            }
            catch { }
        }

        void AttachMenuItemHandlers(Control root)
        {
            // Find all ContextMenu instances defined in the control's resources and visual tree
            var menus = root.GetVisualDescendants().OfType<ContextMenu>().ToList();
            foreach (var menu in menus)
            {
                foreach (var mi in menu.Items.OfType<MenuItem>())
                {
                    mi.Click -= MenuItem_Click_CloseContext;
                    mi.Click += MenuItem_Click_CloseContext;
                }
            }
        }

        void MenuItem_Click_CloseContext(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                // Close any context menus within this view to ensure they disappear immediately
                var menus = this.GetVisualDescendants().OfType<ContextMenu>().ToList();
                foreach (var cm in menus)
                {
                    cm.Close();
                }
            }
            catch { }
        }

        void ExplorerView_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _openRequested?.Dispose();
            _cutRequested?.Dispose();
            _copyRequested?.Dispose();
            _pasteRequested?.Dispose();
            _deleteRequested?.Dispose();
            _selectItemsRequested?.Dispose();
            _openRequested = null;
            _cutRequested = null;
            _copyRequested = null;
            _pasteRequested = null;
            _deleteRequested = null;
            _selectItemsRequested = null;
            DetachedFromVisualTree -= ExplorerView_DetachedFromVisualTree;
        }

        void InlineEdit_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (sender is Avalonia.Controls.TextBox tb)
                {
                    var model = tb.DataContext as Models.ExplorerItemModel;
                    if (model == null)
                        return;

                    // Only commit if the model is in editing state
                    if (!model.IsEditing)
                        return;

                    var vm = this.DataContext as ExplorerViewModel;
                    if (vm?.CommitRenameCommand != null && vm.CommitRenameCommand.CanExecute(model))
                    {
                        vm.CommitRenameCommand.Execute(model);
                    }
                }
            }
            catch { }
        }

        IReadOnlyList<Models.ExplorerItemModel> GetSelectedItems()
        {
            var details = DetailsDataGrid ?? this.FindControl<DataGrid>("DetailsDataGrid");
            var list = ListListBox ?? this.FindControl<ListBox>("ListListBox");
            var icons = IconsListBox ?? this.FindControl<ListBox>("IconsListBox");
            var tiles = TilesListBox ?? this.FindControl<ListBox>("TilesListBox");
            var content = ContentListBox ?? this.FindControl<ListBox>("ContentListBox");

            if (details?.IsVisible == true)
                return GetSelectedItems(details.SelectedItems, details.SelectedItem);

            if (list?.IsVisible == true)
                return GetSelectedItems(list.SelectedItems, list.SelectedItem);

            if (icons?.IsVisible == true)
                return GetSelectedItems(icons.SelectedItems, icons.SelectedItem);

            if (tiles?.IsVisible == true)
                return GetSelectedItems(tiles.SelectedItems, tiles.SelectedItem);

            if (content?.IsVisible == true)
                return GetSelectedItems(content.SelectedItems, content.SelectedItem);

            var selection = GetSelectedItems(details?.SelectedItems, details?.SelectedItem);
            if (selection.Count > 0)
                return selection;

            selection = GetSelectedItems(list?.SelectedItems, list?.SelectedItem);
            if (selection.Count > 0)
                return selection;

            selection = GetSelectedItems(icons?.SelectedItems, icons?.SelectedItem);
            if (selection.Count > 0)
                return selection;

            selection = GetSelectedItems(tiles?.SelectedItems, tiles?.SelectedItem);
            if (selection.Count > 0)
                return selection;

            selection = GetSelectedItems(content?.SelectedItems, content?.SelectedItem);
            if (selection.Count > 0)
                return selection;

            return new List<Models.ExplorerItemModel>();
        }

        static IReadOnlyList<Models.ExplorerItemModel> GetSelectedItems(IList? selectedItems, object? selectedItem)
        {
            var results = new List<Models.ExplorerItemModel>();
            if (selectedItems != null)
                results.AddRange(selectedItems.OfType<Models.ExplorerItemModel>());

            if (results.Count == 0 && selectedItem is Models.ExplorerItemModel single)
                results.Add(single);

            return results;
        }

        void ApplySelection(IReadOnlyList<string> paths)
        {
            var pathSet = BuildPathSet(paths);
            if (pathSet.Count == 0)
                return;

            var details = DetailsDataGrid ?? this.FindControl<DataGrid>("DetailsDataGrid");
            var list = ListListBox ?? this.FindControl<ListBox>("ListListBox");
            var icons = IconsListBox ?? this.FindControl<ListBox>("IconsListBox");
            var tiles = TilesListBox ?? this.FindControl<ListBox>("TilesListBox");
            var content = ContentListBox ?? this.FindControl<ListBox>("ContentListBox");

            if (details?.IsVisible == true && SelectInDataGrid(details, pathSet))
                return;
            if (list?.IsVisible == true && SelectInListBox(list, pathSet))
                return;
            if (icons?.IsVisible == true && SelectInListBox(icons, pathSet))
                return;
            if (tiles?.IsVisible == true && SelectInListBox(tiles, pathSet))
                return;
            if (content?.IsVisible == true && SelectInListBox(content, pathSet))
                return;

            if (SelectInDataGrid(details, pathSet))
                return;
            if (SelectInListBox(list, pathSet))
                return;
            if (SelectInListBox(icons, pathSet))
                return;
            if (SelectInListBox(tiles, pathSet))
                return;
            SelectInListBox(content, pathSet);
        }

        static HashSet<string> BuildPathSet(IReadOnlyList<string> paths)
        {
            var comparer = System.OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var set = new HashSet<string>(comparer);
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    set.Add(path);
            }

            return set;
        }

        static bool SelectInDataGrid(DataGrid? grid, HashSet<string> pathSet)
        {
            if (grid == null || pathSet.Count == 0)
                return false;

            var selectedItems = grid.SelectedItems;
            selectedItems?.Clear();
            Models.ExplorerItemModel? firstSelected = null;

            var gridItems = grid as IEnumerable ?? Array.Empty<object>();
            foreach (var item in gridItems)
            {
                if (item is not Models.ExplorerItemModel model)
                    continue;

                var path = (model.Object as FileSystemObjectModel)?.Path;
                if (string.IsNullOrWhiteSpace(path) || !pathSet.Contains(path))
                    continue;

                selectedItems?.Add(model);
                firstSelected ??= model;
            }

            if (firstSelected != null)
                grid.SelectedItem = firstSelected;

            return firstSelected != null;
        }

        static bool SelectInListBox(ListBox? listBox, HashSet<string> pathSet)
        {
            if (listBox == null || pathSet.Count == 0)
                return false;

            var selectedItems = listBox.SelectedItems;
            selectedItems?.Clear();
            Models.ExplorerItemModel? firstSelected = null;

            var listItems = listBox as IEnumerable ?? Array.Empty<object>();
            foreach (var item in listItems)
            {
                if (item is not Models.ExplorerItemModel model)
                    continue;

                var path = (model.Object as FileSystemObjectModel)?.Path;
                if (string.IsNullOrWhiteSpace(path) || !pathSet.Contains(path))
                    continue;

                selectedItems?.Add(model);
                firstSelected ??= model;
            }

            if (firstSelected != null)
                listBox.SelectedItem = firstSelected;

            return firstSelected != null;
        }

        static string DescribeSelection(IReadOnlyList<Models.ExplorerItemModel> items)
        {
            if (items == null || items.Count == 0)
                return "<none>";

            var preview = items
                .Take(3)
                .Select(item =>
                {
                    var name = item.DisplayName ?? item.Label ?? "<unnamed>";
                    var path = (item.Object as FileSystemObjectModel)?.Path;
                    if (!string.IsNullOrWhiteSpace(path))
                        return $"{name} @ {path}";
                    return name;
                })
                .ToList();

            var summary = string.Join(", ", preview);
            if (items.Count > 3)
                summary += $" (+{items.Count - 3} more)";

            return summary;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
