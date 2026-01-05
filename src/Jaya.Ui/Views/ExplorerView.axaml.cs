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
using Avalonia.Input;

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
        Avalonia.Input.PointerPressedEventArgs? _dragStartArgs;
        bool _isDragging;

        public ExplorerView()
        {
            this.InitializeComponent();
            this.AddHandler(KeyDownEvent, ExplorerView_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, ExplorerView_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(ContextRequestedEvent, ExplorerView_ContextRequested, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, Root_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, Root_PointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, Root_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(DragDrop.DragOverEvent, Root_DragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(DragDrop.DropEvent, Root_Drop, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            if (!Design.IsDesignMode)
            {
                // Attach MenuItem click handlers to close context menus immediately when an item is clicked.
                this.AttachedToVisualTree += (s, e) =>
                {
                    try
                    {
                        AttachMenuItemHandlers(this);
                        // Also attach handlers to DataGrid for drag-drop in Details view
                        var dataGrid = this.FindControl<DataGrid>("DetailsDataGrid");
                        if (dataGrid != null)
                        {
                            Logger.Debug("Attaching drag-drop handlers to DetailsDataGrid");
                            // Try both strategies: Tunnel (from top down) and Bubble (from bottom up)
                            dataGrid.AddHandler(DragDrop.DragOverEvent, DetailsDataGrid_DragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                            dataGrid.AddHandler(DragDrop.DropEvent, DetailsDataGrid_Drop, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                            dataGrid.AddHandler(DragDrop.DragOverEvent, DetailsDataGrid_DragOver, Avalonia.Interactivity.RoutingStrategies.Bubble);
                            dataGrid.AddHandler(DragDrop.DropEvent, DetailsDataGrid_Drop, Avalonia.Interactivity.RoutingStrategies.Bubble);
                            // Clear selection when clicking empty space in Details view
                            dataGrid.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, DetailsDataGrid_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Bubble);
                            // Save sort settings when user sorts columns
                            dataGrid.Sorting += (ss, ee) =>
                            {
                                try
                                {
                                    var vm = DataContext as Jaya.Ui.ViewModels.ExplorerViewModel;
                                    if (vm == null)
                                        return;

                                    // Try to detect the sorted column via reflection: look for a property named 'SortDirection' on columns
                                    foreach (var col in dataGrid.Columns)
                                    {
                                        try
                                        {
                                            var prop = col.GetType().GetProperty("SortDirection");
                                            if (prop != null)
                                            {
                                                var val = prop.GetValue(col);
                                                if (val != null)
                                                {
                                                    // Found sorted column
                                                    var sortMember = (col as Avalonia.Controls.DataGridColumn)?.SortMemberPath ?? col.Header?.ToString() ?? string.Empty;
                                                    var asc = val.ToString()?.IndexOf("Asc", StringComparison.OrdinalIgnoreCase) >= 0;
                                                    var currentDir = vm.Item?.Object as Jaya.Shared.Models.DirectoryModel;
                                                    if (currentDir != null && !string.IsNullOrWhiteSpace(sortMember))
                                                        vm.SaveDirectorySort(currentDir.Path, sortMember, asc);
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            };
                            Logger.Debug("Drag-drop handlers attached successfully (Tunnel + Bubble)");
                           
                                try
                                {
                                    if (OperatingSystem.IsMacOS())
                                    {
                                        var col = dataGrid.Columns.FirstOrDefault(c => (c as Avalonia.Controls.DataGridTextColumn)?.Binding?.ToString()?.Contains("Object.Type") == true || string.Equals(c.Header?.ToString(), "Type", StringComparison.OrdinalIgnoreCase));
                                        if (col != null)
                                            col.Header = "Kind";
                                    }
                                }
                                catch { }
                        }
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
                // Restore directory sort when ViewModel.Item changes
                this.DataContextChanged += (s, e) =>
                {
                    try
                    {
                        var vm = this.DataContext as Jaya.Ui.ViewModels.ExplorerViewModel;
                        if (vm != null)
                        {
                            vm.PropertyChanged += (vs, ve) =>
                            {
                                try
                                {
                                    if (ve.PropertyName == nameof(Jaya.Ui.ViewModels.ExplorerViewModel.Item))
                                    {
                                        var grid = this.FindControl<DataGrid>("DetailsDataGrid");
                                        var dir = vm.Item?.Object as Jaya.Shared.Models.DirectoryModel;
                                        if (grid != null && dir != null)
                                        {
                                            var sort = vm.GetDirectorySort(dir.Path);
                                            if (sort.HasValue)
                                            {
                                                // Try to find matching column and set its SortDirection via reflection
                                                foreach (var col in grid.Columns)
                                                {
                                                    try
                                                    {
                                                        var dgCol = col as Avalonia.Controls.DataGridColumn;
                                                        var sortMember = dgCol?.SortMemberPath ?? col.Header?.ToString();
                                                        if (!string.IsNullOrWhiteSpace(sortMember) && string.Equals(sortMember, sort.Value.sortMember, StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            var prop = col.GetType().GetProperty("SortDirection");
                                                            if (prop != null && prop.PropertyType.IsEnum)
                                                            {
                                                                // Find enum value matching Ascending/Descending
                                                                var enumType = prop.PropertyType;
                                                                var desired = sort.Value.ascending ? "Ascending" : "Descending";
                                                                var enumVal = Enum.GetValues(enumType).Cast<object>().FirstOrDefault(x => x.ToString()?.IndexOf(desired, StringComparison.OrdinalIgnoreCase) >= 0);
                                                                if (enumVal != null)
                                                                {
                                                                    prop.SetValue(col, enumVal);
                                                                }
                                                            }
                                                            break;
                                                        }
                                                    }
                                                    catch { }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            };
                        }
                    }
                    catch { }
                };
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

        void Root_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                if (e == null)
                    return;

                var point = e.GetCurrentPoint(this);
                if (!point.Properties.IsLeftButtonPressed)
                {
                    _dragStartArgs = null;
                    _isDragging = false;
                    return;
                }

                if (!IsWithinItem(e.Source as Avalonia.Visual))
                {
                    _dragStartArgs = null;
                    _isDragging = false;
                    return;
                }

                _dragStartArgs = e;
                _isDragging = false;
                Logger.Debug("Root_PointerPressed: drag candidates prepared");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in Root_PointerPressed");
            }
        }

        void Root_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            try
            {
                if (_dragStartArgs == null)
                    return;

                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _dragStartArgs = null;
                    _isDragging = false;
                    return;
                }

                var start = _dragStartArgs.GetPosition(this);
                var current = e.GetPosition(this);
                var dx = Math.Abs(current.X - start.X);
                var dy = Math.Abs(current.Y - start.Y);
                if (!_isDragging && (dx > 4 || dy > 4))
                {
                    _isDragging = true;
                    Logger.Debug("Drag threshold exceeded: dx={DX}, dy={DY}, starting drag operation", dx, dy);
                    StartDragOperation();
                }
            }
            catch (Exception ex) 
            {
                Logger.Error(ex, "Error in Root_PointerMoved");
            }
        }

        void Root_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            _dragStartArgs = null;
            _isDragging = false;
        }

        async void StartDragOperation()
        {
            try
            {
                var vm = DataContext as ExplorerViewModel;
                if (vm == null)
                {
                    Logger.Debug("StartDragOperation: no ViewModel");
                    return;
                }

                var selected = GetSelectedItems();
                if (selected.Count == 0)
                {
                    Logger.Debug("StartDragOperation: no items selected");
                    return;
                }

                var paths = selected.Where(s => s.Object is FileSystemObjectModel)
                                     .Select(s => (s.Object as FileSystemObjectModel)?.Path)
                                     .Where(p => !string.IsNullOrEmpty(p))
                                     .ToArray();

                if (paths.Length == 0)
                {
                    Logger.Debug("StartDragOperation: no valid paths selected");
                    return;
                }

                Logger.Debug("StartDragOperation: initiating drag with {Count} paths", paths.Length);
                var data = new Avalonia.Input.DataObject();
                data.Set("Jaya.Paths", paths);

                if (_dragStartArgs != null)
                    await DragDrop.DoDragDrop(_dragStartArgs, data, DragDropEffects.Move | DragDropEffects.Copy);
                else
                    await DragDrop.DoDragDrop((Avalonia.Input.PointerEventArgs?)null, data, DragDropEffects.Move | DragDropEffects.Copy);
                Logger.Debug("StartDragOperation: drag operation completed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in StartDragOperation");
            }
            finally
            {
                _dragStartArgs = null;
                _isDragging = false;
            }
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

        void Root_DragOver(object? sender, DragEventArgs e)
        {
            try
            {
                // Accept if our data object contains Jaya.Paths
                if (e.Data != null && e.Data.Contains("Jaya.Paths"))
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                        e.DragEffects = DragDropEffects.Copy;
                    else
                        e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    Logger.Verbose("DragOver accepted with effects={Effects}", e.DragEffects);
                }
                else
                {
                    Logger.Debug("DragOver rejected: no Jaya.Paths in data");
                }
            }
            catch (Exception ex) 
            {
                Logger.Error(ex, "Error in Root_DragOver");
            }
        }

        async void Root_Drop(object? sender, DragEventArgs e)
        {
            await ProcessDropAsync(e);
        }

        void DetailsDataGrid_DragOver(object? sender, DragEventArgs e)
        {
            Logger.Verbose("DetailsDataGrid_DragOver triggered");
            Root_DragOver(sender, e);
        }

        void DetailsDataGrid_Drop(object? sender, DragEventArgs e)
        {
            Logger.Verbose("DetailsDataGrid_Drop triggered");
            // Fire and forget the async operation
            _ = ProcessDropAsync(e);
        }

        private async System.Threading.Tasks.Task ProcessDropAsync(DragEventArgs e)
        {
            Logger.Debug("ProcessDropAsync: entry point reached");
            try
            {
                if (e.Data == null || !e.Data.Contains("Jaya.Paths"))
                {
                    Logger.Debug("Drop rejected: no Jaya.Paths in data");
                    return;
                }

                var obj = e.Data.Get("Jaya.Paths") as string[];
                if (obj == null || obj.Length == 0)
                {
                    Logger.Debug("Drop rejected: empty paths");
                    return;
                }

                Logger.Debug("Drop detected: {Count} paths", obj.Length);

                // Determine drop target by visual under pointer/source
                var targetModel = FindExplorerItemModel(e.Source as Avalonia.Visual);
                if (targetModel == null)
                {
                    Logger.Debug("Target not found via e.Source, trying InputHitTest");
                    var point = e.GetPosition(this);
                    targetModel = FindExplorerItemModel(this.InputHitTest(point) as Avalonia.Visual);
                }

                if (targetModel == null)
                {
                    Logger.Debug("Drop rejected: no target model found");
                    return;
                }

                Logger.Debug("Target found: {Label}, IsDirectory={IsDir}, Type={Type}", targetModel.Label, targetModel.IsDirectory, targetModel.Type);

                var vm = DataContext as ExplorerViewModel;
                if (vm == null)
                {
                    Logger.Debug("Drop rejected: no ViewModel");
                    return;
                }

                // Only allow dropping onto directories
                if (!targetModel.IsDirectory && targetModel.Type != ItemType.Account && targetModel.Type != ItemType.Computer)
                {
                    Logger.Debug("Drop rejected: target is not a directory");
                    return;
                }

                // call ViewModel to handle drop
                var effect = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;
                Logger.Debug("Invoking HandleDropAsync with effect={Effect}", effect);
                await vm.HandleDropAsync(obj, targetModel.Object as Jaya.Shared.Models.DirectoryModel, effect);
                e.Handled = true;
                Logger.Debug("Drop handled successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in ProcessDropAsync");
            }
        }

        void DetailsDataGrid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                var src = e.Source as Avalonia.Visual;
                var foundRow = false;
                while (src != null)
                {
                    if (src is Avalonia.Controls.DataGridRow)
                    {
                        foundRow = true;
                        break;
                    }
                    src = (src as Visual)?.GetVisualParent() as Avalonia.Visual;
                }

                if (!foundRow)
                {
                    var dg = sender as Avalonia.Controls.DataGrid;
                    if (dg != null)
                        dg.SelectedItems?.Clear();
                }
            }
            catch { }
        }
        
        static Models.ExplorerItemModel? FindExplorerItemModel(Avalonia.Visual? visual)
        {
            var depth = 0;
            while (visual != null && depth < 20)  // Limit search depth
            {
                if (visual.DataContext is Models.ExplorerItemModel model)
                {
                    Logger.Debug("Found ExplorerItemModel at depth {Depth}: {Label}", depth, model.Label);
                    return model;
                }

                visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual) as Avalonia.Visual;
                depth++;
            }

            Logger.Debug("Did not find ExplorerItemModel (searched {Depth} levels)", depth);
            return null;
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
