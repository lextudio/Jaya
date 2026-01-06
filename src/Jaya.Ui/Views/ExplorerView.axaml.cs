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
using System.ComponentModel;
using System.Linq;
using Jaya.Ui.Services;
using System.Reflection;
using Jaya.Ui.ViewModels;
using Jaya.Shared;
using Jaya.Shared.Services;
using Avalonia.Threading;
using Serilog;
using Avalonia.Input;
using System.Text.Json;

namespace Jaya.Ui.Views
{
    public partial class ExplorerView : UserControl
    {
        static readonly ILogger Logger = Log.ForContext(typeof(ExplorerView)).ForContext("SourceContext", "Views");
        static readonly DataFormat<byte[]> JayaPathsFormat = DataFormat.CreateBytesApplicationFormat("Jaya.Paths");

        Subscription<OpenRequestedEventArgs>? _openRequested;
        Subscription<CutRequestedEventArgs>? _cutRequested;
        Subscription<CopyRequestedEventArgs>? _copyRequested;
        Subscription<PasteRequestedEventArgs>? _pasteRequested;
        Subscription<DeleteRequestedEventArgs>? _deleteRequested;
        Subscription<SelectAllRequestedEventArgs>? _selectAllRequested;
        Subscription<SelectNoneRequestedEventArgs>? _selectNoneRequested;
        Subscription<InvertSelectionRequestedEventArgs>? _invertSelectionRequested;
        Subscription<SelectItemsRequestedEventArgs>? _selectItemsRequested;
        Subscription<CopyPathRequestedEventArgs>? _copyPathRequested;
        Avalonia.Input.PointerPressedEventArgs? _dragStartArgs;
        bool _isDragging;
        ExplorerViewModel? _viewModel;
        PropertyChangedEventHandler? _viewModelPropertyChanged;
        DataGrid? _detailsGrid;
        EventHandler<AvaloniaPropertyChangedEventArgs>? _detailsGridPropertyChanged;

        public ExplorerView()
        {
            this.InitializeComponent();
            this.AddHandler(KeyDownEvent, ExplorerView_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, ExplorerView_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, Root_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, Root_PointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, Root_PointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(DragDrop.DragOverEvent, Root_DragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(DragDrop.DropEvent, Root_Drop, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AttachViewModel(this.DataContext as ExplorerViewModel);
            if (!Design.IsDesignMode)
            {
                // Attach MenuItem click handlers to close context menus immediately when an item is clicked.
                this.AttachedToVisualTree += (s, e) =>
                {
                    try
                    {
                        // Attach ContextMenuBehavior: assign the empty-space menu directly from resources if available
                        if (this.Resources.ContainsKey("EmptySpaceMenu"))
                        {
                            var cm = this.FindControl<ContextMenu>("EmptySpaceMenu");
                            if (cm != null) Jaya.Ui.Behaviors.ContextMenuBehavior.SetEmptySpaceContextMenu(this, cm);
                        }
                        // Provide a row-context menu factory that chooses a menu per item DataContext
                        Jaya.Ui.Behaviors.ContextMenuBehavior.SetRowContextMenuFactory(this, (obj) =>
                        {
                            try
                            {
                                if (obj == null) return null;
                                if (obj is Models.ExplorerItemModel em)
                                {
                                    if (em.IsDirectory && this.Resources.ContainsKey("DirectoryRowMenu"))
                                        return this.FindControl<ContextMenu>("DirectoryRowMenu");
                                    if (!em.IsDirectory && this.Resources.ContainsKey("FileRowMenu"))
                                        return this.FindControl<ContextMenu>("FileRowMenu");
                                }
                                if (this.Resources.ContainsKey("ItemRowMenu"))
                                    return this.FindControl<ContextMenu>("ItemRowMenu");
                            }
                            catch { }
                            return null;
                        });

                        // Also attach handlers to DataGrid for drag-drop in Details view
                        var dataGrid = this.FindControl<DataGrid>("DetailsDataGrid");
                        if (dataGrid != null)
                        {
                            Logger.Debug("Attaching drag-drop handlers to DetailsDataGrid");
                            AttachDetailsGrid(dataGrid);
                            // Enable centralized selection behavior
                            try { Jaya.Ui.Behaviors.SelectionBehavior.SetIsEnabled(dataGrid, true); } catch { }
                            // Try both strategies: Tunnel (from top down) and Bubble (from bottom up)
                            dataGrid.AddHandler(DragDrop.DragOverEvent, DetailsDataGrid_DragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                            dataGrid.AddHandler(DragDrop.DropEvent, DetailsDataGrid_Drop, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                            dataGrid.AddHandler(DragDrop.DragOverEvent, DetailsDataGrid_DragOver, Avalonia.Interactivity.RoutingStrategies.Bubble);
                            dataGrid.AddHandler(DragDrop.DropEvent, DetailsDataGrid_Drop, Avalonia.Interactivity.RoutingStrategies.Bubble);

                            // Attach the new behavior as well (safe to call even if already wired)
                            Jaya.Ui.Behaviors.DragDropBehavior.SetIsEnabled(dataGrid, true);
                            Jaya.Ui.Behaviors.DragDropBehavior.SetIsEnabled(this, true);

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

                                    var columnArgs = ee as Avalonia.Controls.DataGridColumnEventArgs;
                                    var sortedColumn = columnArgs?.Column;
                                    if (sortedColumn == null)
                                        return;

                                    var sortMember = sortedColumn.SortMemberPath ?? sortedColumn.Header?.ToString() ?? string.Empty;
                                    if (string.IsNullOrWhiteSpace(sortMember))
                                        return;

                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        try
                                        {
                                            if (!TryGetSortAscending(sortedColumn, out var asc))
                                                return;

                                            var currentDir = vm.Item?.Object as Jaya.Shared.Models.DirectoryModel;
                                            if (currentDir != null)
                                            {
                                                vm.SaveDirectorySort(currentDir.Path, sortMember, asc);
                                                Logger.Debug("Details sort changed: Path={Path} Member={Member} Ascending={Ascending}",
                                                    currentDir.Path,
                                                    sortMember,
                                                    asc);
                                            }
                                        }
                                        catch { }
                                    });
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
                    // Subscribe to Tree drop requests (from NavigationView) and delegate to ExplorerViewModel.HandleDropAsync
                    var _treeDropRequested = eventAggregator.Subscribe<Jaya.Ui.TreeDropRequestedEventArgs>(args =>
                    {
                        Dispatcher.UIThread.Post(async () =>
                        {
                            try
                            {
                                var vm = DataContext as ExplorerViewModel;
                                if (vm == null)
                                    return;

                                if (args?.SourcePaths != null && args.SourcePaths.Length > 0)
                                {
                                    await vm.HandleDropAsync(args.SourcePaths, args.TargetDirectory, args.Effect);
                                }
                            }
                            catch { }
                        });
                    });

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
                                    if (vm != null && vm.DisplayedItems != null)
                                    {
                                        var editing = vm.DisplayedItems.FirstOrDefault(c => c.IsEditing);
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
                    _copyPathRequested = eventAggregator.Subscribe<CopyPathRequestedEventArgs>(args =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Logger.Debug("CopyPathRequestedEventArgs received");

                                var vm = DataContext as ExplorerViewModel;
                                if (vm == null)
                                {
                                    Logger.Warning("CopyPath handler: DataContext is not ExplorerViewModel");
                                    return;
                                }

                                var selectedItems = GetSelectedItems();
                                if (selectedItems == null || selectedItems.Count == 0)
                                {
                                    Logger.Debug("CopyPath handler: no selected item found");
                                    return;
                                }

                                var paths = selectedItems
                                    .Where(s => s?.Object is Jaya.Shared.Models.FileSystemObjectModel)
                                    .Select(s => (s.Object as Jaya.Shared.Models.FileSystemObjectModel)?.Path)
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .ToArray();

                                if (paths.Length == 0)
                                {
                                    Logger.Debug("CopyPath handler: selected item(s) have empty paths or are not file system objects");
                                    return;
                                }

                                var payload = paths.Length == 1 ? paths[0] : string.Join(System.Environment.NewLine, paths);
                                // Use async clipboard call to avoid blocking the UI thread.
                                Dispatcher.UIThread.Post(async () =>
                                {
                                    try
                                    {
                                        if (string.IsNullOrEmpty(payload))
                                        {
                                            Logger.Debug("CopyPath: payload is empty, aborting clipboard call");
                                            return;
                                        }

                                        await ClipboardService.CopyTextAsync(payload);
                                        Logger.Debug("Copied path(s) to clipboard: Count={Count}", paths.Length);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warning(ex, "CopyPath failed");
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning(ex, "Exception in CopyPath request handler");
                            }
                        });
                    });

                    // Register top-level selection commands so they fire regardless of SelectItemsRequested
                    _selectAllRequested = eventAggregator.Subscribe<SelectAllRequestedEventArgs>(args =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Logger.Debug("SelectAllRequestedEventArgs received");
                                PerformSelectAll();
                            }
                            catch (Exception ex) { Logger.Warning(ex, "PerformSelectAll failed"); }
                        });
                    });

                    _selectNoneRequested = eventAggregator.Subscribe<SelectNoneRequestedEventArgs>(args =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Logger.Debug("SelectNoneRequestedEventArgs received");
                                PerformSelectNone();
                            }
                            catch (Exception ex) { Logger.Warning(ex, "PerformSelectNone failed"); }
                        });
                    });

                    _invertSelectionRequested = eventAggregator.Subscribe<InvertSelectionRequestedEventArgs>(args =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Logger.Debug("InvertSelectionRequestedEventArgs received");
                                PerformInvertSelection();
                            }
                            catch (Exception ex) { Logger.Warning(ex, "PerformInvertSelection failed"); }
                        });
                    });

                    DetachedFromVisualTree += ExplorerView_DetachedFromVisualTree;
                }
                // Restore directory sort when ViewModel.Item changes
                this.DataContextChanged += (s, e) =>
                {
                    AttachViewModel(this.DataContext as ExplorerViewModel);
                };
            }

        }

        void ExplorerView_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
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

        void AttachViewModel(ExplorerViewModel? vm)
        {
            if (_viewModel != null && _viewModelPropertyChanged != null)
                _viewModel.PropertyChanged -= _viewModelPropertyChanged;

            _viewModel = vm;
            if (_viewModel == null)
            {
                Logger.Information("ExplorerView DataContext cleared.");
                return;
            }

            _viewModelPropertyChanged = (vs, ve) =>
            {
                try
                {
                    if (ve.PropertyName == nameof(ExplorerViewModel.Item))
                    {
                        Logger.Information("ExplorerViewModel.Item changed; applying saved details sort.");
                        ApplySavedDetailsSort(_viewModel);
                    }
                }
                catch { }
            };
            _viewModel.PropertyChanged += _viewModelPropertyChanged;
            Logger.Information("ExplorerView DataContext attached: {Type}", _viewModel.GetType().Name);
            ApplySavedDetailsSort(_viewModel);
        }

        void ApplySavedDetailsSort(ExplorerViewModel vm)
        {
            if (vm == null)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                var grid = _detailsGrid ?? this.FindControl<DataGrid>("DetailsDataGrid");
                var dir = vm.Item?.Object as Jaya.Shared.Models.DirectoryModel;
                if (grid == null)
                {
                    Logger.Information("ApplySavedDetailsSort skipped: DetailsDataGrid not found.");
                    return;
                }
                // Instruct SelectionBehavior to suppress updates while applying saved sort
                try { Jaya.Ui.Behaviors.SelectionBehavior.SetSuppressWhile(grid, true); } catch { }
                if (dir == null)
                {
                    Logger.Debug("ApplySavedDetailsSort skipped: current directory is null.");
                    return;
                }

                var sort = vm.GetDirectorySort(dir.Path);
                if (!sort.HasValue)
                {
                    Logger.Debug("No saved details sort to apply: Path={Path}", dir.Path);
                    return;
                }

                try
                {
                    foreach (var col in grid.Columns)
                    {
                        try
                        {
                            var dgCol = col as Avalonia.Controls.DataGridColumn;
                            var sortMember = dgCol?.SortMemberPath ?? col.Header?.ToString();
                            if (!string.IsNullOrWhiteSpace(sortMember) && string.Equals(sortMember, sort.Value.sortMember, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Debug("Applying details sort: Path={Path} Member={Member} Ascending={Ascending}",
                                    dir.Path,
                                    sort.Value.sortMember,
                                    sort.Value.ascending);
                                foreach (var other in grid.Columns)
                                {
                                    if (!ReferenceEquals(other, col))
                                        (other as Avalonia.Controls.DataGridColumn)?.ClearSort();
                                }

                                dgCol?.Sort(sort.Value.ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);

                                // After the DataGrid processes its internal deferred refresh and currency changes,
                                // set a safe selected item (first visible non-hidden item) or leave null.
                                Dispatcher.UIThread.Post(async () =>
                                {
                                    await System.Threading.Tasks.Task.Delay(200);
                                    // Post the UI work back onto the UI thread to avoid cross-thread access to Avalonia objects
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        try
                                        {
                                            if (grid == null)
                                                return;

                                            // Find a safe first item to select, if any
                                            var items = (grid.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().OfType<Models.ExplorerItemModel>().ToList() ?? new List<Models.ExplorerItemModel>();
                                            var safe = items.FirstOrDefault();
                                            if (safe != null)
                                            {
                                                try
                                                {
                                                    grid.SelectedItems?.Clear();
                                                    grid.SelectedItems?.Add(safe);
                                                    grid.SelectedItem = safe;
                                                    Logger.Debug("Set safe selected item after sort: {Item}", safe.DisplayName ?? safe.Label);
                                                    ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(1);
                                                }
                                                catch (Exception ex)
                                                {
                                                    Logger.Debug(ex, "Failed to set safe selected item after sort");
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.Warning(ex, "Error while setting safe selection after details sort");
                                        }
                                        finally
                                        {
                                            // Allow selection handling again after the delayed safe-selection attempt
                                            try { Jaya.Ui.Behaviors.SelectionBehavior.SetSuppressWhile(grid, false); } catch { }
                                        }
                                    });
                                });
                                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() => { try { Jaya.Ui.Behaviors.SelectionBehavior.SetSuppressWhile(grid, false); } catch { } }));
                                break; 
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    Logger.Debug("Details sort apply finished: Path={Path}", dir.Path);
                }
            });
        }

        void AttachDetailsGrid(DataGrid dataGrid)
        {
            if (ReferenceEquals(_detailsGrid, dataGrid) && _detailsGridPropertyChanged != null)
                return;

            if (_detailsGrid != null && _detailsGridPropertyChanged != null)
                _detailsGrid.PropertyChanged -= _detailsGridPropertyChanged;

            _detailsGrid = dataGrid;
            // Selection behavior takes care of SelectedItems updates; keep grid property handling below.
            _detailsGridPropertyChanged = (s, e) =>
            {
                try
                {
                    if (e.Property == DataGrid.ItemsSourceProperty)
                    {
                        Logger.Debug("DetailsDataGrid.ItemsSource changed.");
                        if (_viewModel == null)
                        {
                            var vm = this.DataContext as ExplorerViewModel;
                            if (vm != null)
                                AttachViewModel(vm);
                        }
                        if (_viewModel != null)
                            ApplySavedDetailsSort(_viewModel);
                        else
                            Logger.Information("DetailsDataGrid.ItemsSource changed but ExplorerViewModel is not attached yet.");
                    }
                }
                catch { }
            };
            _detailsGrid.PropertyChanged += _detailsGridPropertyChanged;
        }
 
        static bool TryGetSortAscending(Avalonia.Controls.DataGridColumn column, out bool ascending)
        {
            ascending = true;
            if (column == null)
                return false;

            try
            {
                var colType = column.GetType();
                var dirProp = colType.GetProperty("SortDirection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (dirProp != null)
                {
                    var dirValue = dirProp.GetValue(column);
                    if (TryParseSortDirection(dirValue, out ascending))
                        return true;
                }

                var getSortDescription = colType.GetMethod("GetSortDescription", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getSortDescription == null)
                    return false;

                var sortDescription = getSortDescription.Invoke(column, null);
                return TryParseSortDirection(sortDescription, out ascending);
            }
            catch
            {
                return false;
            }
        }

        static bool TryParseSortDirection(object? value, out bool ascending)
        {
            ascending = true;
            if (value == null)
                return false;

            try
            {
                var type = value.GetType();
                var dirProp = type.GetProperty("Direction") ?? type.GetProperty("SortDirection");
                if (dirProp != null)
                {
                    var dirValue = dirProp.GetValue(value);
                    if (dirValue != null)
                    {
                        var dirText = dirValue.ToString() ?? string.Empty;
                        ascending = dirText.IndexOf("Desc", StringComparison.OrdinalIgnoreCase) < 0;
                        return true;
                    }
                }

                var text = value.ToString() ?? string.Empty;
                if (text.Length == 0)
                    return false;

                ascending = text.IndexOf("Desc", StringComparison.OrdinalIgnoreCase) < 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        void ExplorerView_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e == null)
                return;

            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed)
                Jaya.Ui.Behaviors.ContextMenuBehavior.CloseAllContextMenus(this);
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
                ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(_detailsGrid?.SelectedItems?.Count ?? 0);
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

                if (_dragStartArgs == null)
                {
                    Logger.Debug("StartDragOperation: missing drag start args");
                    return;
                }

                // New drag & drop API: IDataTransfer/DataTransfer + DataTransferItem
                // Serialize paths to bytes for a stable payload.
                var payload = JsonSerializer.SerializeToUtf8Bytes(paths);
                var dataTransfer = new DataTransfer();
                dataTransfer.Add(DataTransferItem.Create(JayaPathsFormat, payload));

                await DragDrop.DoDragDropAsync(_dragStartArgs, dataTransfer, DragDropEffects.Move | DragDropEffects.Copy);

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
            if (_detailsGrid != null && _detailsGridPropertyChanged != null)
                _detailsGrid.PropertyChanged -= _detailsGridPropertyChanged;
            _detailsGridPropertyChanged = null;
            try
            {
                // Disable selection behavior on known item controls
                if (_detailsGrid != null) Jaya.Ui.Behaviors.SelectionBehavior.SetIsEnabled(_detailsGrid, false);
                var list = this.FindControl<ListBox>("ListListBox"); if (list != null) Jaya.Ui.Behaviors.SelectionBehavior.SetIsEnabled(list, false);
                var icons = this.FindControl<ListBox>("IconsListBox"); if (icons != null) Jaya.Ui.Behaviors.SelectionBehavior.SetIsEnabled(icons, false);
                var tiles = this.FindControl<ListBox>("TilesListBox"); if (tiles != null) Jaya.Ui.Behaviors.SelectionBehavior.SetIsEnabled(tiles, false);
                var content = this.FindControl<ListBox>("ContentListBox"); if (content != null) Jaya.Ui.Behaviors.SelectionBehavior.SetIsEnabled(content, false);

                _detailsGrid = null;
            }
            catch { }

        }

        void Root_DragOver(object? sender, DragEventArgs e)
        {
            try
            {
                // New drag & drop API: use DataTransfer
                var dt = e.DataTransfer;
                if (dt != null && dt.Contains(JayaPathsFormat))
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
                var dt = e.DataTransfer;
                if (dt == null || !dt.Contains(JayaPathsFormat))
                {
                    Logger.Debug("Drop rejected: no Jaya.Paths in data");
                    return;
                }

                var payload = dt.TryGetValue(JayaPathsFormat);
                var obj = payload != null ? (JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>()) : Array.Empty<string>();

                if (obj.Length == 0)
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

        IEnumerable<(string name, Control? control)> EnumerateItemControls()
        {
            yield return ("DetailsDataGrid", _detailsGrid ?? this.FindControl<DataGrid>("DetailsDataGrid"));
            yield return ("ListListBox", ListListBox ?? this.FindControl<ListBox>("ListListBox"));
            yield return ("IconsListBox", IconsListBox ?? this.FindControl<ListBox>("IconsListBox"));
            yield return ("TilesListBox", TilesListBox ?? this.FindControl<ListBox>("TilesListBox"));
            yield return ("ContentListBox", ContentListBox ?? this.FindControl<ListBox>("ContentListBox"));
        }

        static IReadOnlyList<Models.ExplorerItemModel> GetSelectedItemsFromControl(Control? control)
        {
            if (control is DataGrid dg)
                return GetSelectedItems(dg.SelectedItems, dg.SelectedItem);
            if (control is ListBox lb)
                return GetSelectedItems(lb.SelectedItems, lb.SelectedItem);

            return new List<Models.ExplorerItemModel>();
        }

        static bool TryGetItemsEnumerable(object control, out IEnumerable items, out string itemsSourceUsed)
        {
            items = Array.Empty<object>();
            itemsSourceUsed = "Items";

            if (control is DataGrid dg)
            {
                items = dg.ItemsSource as IEnumerable ?? Array.Empty<object>();
                itemsSourceUsed = "ItemsSource";

                if (!(items ?? Array.Empty<object>()).Cast<object?>().Any() && dg.DataContext is ExplorerViewModel evm)
                {
                    items = evm.DisplayedItems as IEnumerable ?? Array.Empty<object>();
                    itemsSourceUsed = "ViewModel.DisplayedItems";
                }

                return true;
            }

            if (control is ListBox lb)
            {
                items = lb.Items as IEnumerable ?? Array.Empty<object>();
                itemsSourceUsed = "Items";
                return true;
            }

            return false;
        }

        static bool ControlHasItems(object control)
        {
            if (!TryGetItemsEnumerable(control, out var items, out _))
                return false;

            return (items ?? Array.Empty<object>()).Cast<object?>().Any();
        }

        static bool SelectInControl(Control? control, HashSet<string> pathSet)
        {
            if (control is DataGrid dg)
                return SelectInDataGrid(dg, pathSet);
            if (control is ListBox lb)
                return SelectInListBox(lb, pathSet);

            return false;
        }

        static void ClearSelection(Control? control)
        {
            if (control is DataGrid dg)
            {
                dg.SelectedItems?.Clear();
                dg.SelectedItem = null;
                return;
            }

            if (control is ListBox lb)
            {
                lb.SelectedItems?.Clear();
                lb.SelectedItem = null;
            }
        }

        IReadOnlyList<Models.ExplorerItemModel> GetSelectedItems()
        {
            foreach (var entry in EnumerateItemControls())
            {
                if (entry.control?.IsVisible != true)
                    continue;

                var sel = GetSelectedItemsFromControl(entry.control);
                Logger.Debug("GetSelectedItems: using {Name} selectionCount={Count}", entry.name, sel.Count);
                try
                {
                    ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(sel.Count);
                    Logger.Debug("SharedService.UpdateSelectionAvailability called with count={Count}", sel.Count);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed calling UpdateSelectionAvailability from {Name} path", entry.name);
                }
                return sel;
            }

            foreach (var entry in EnumerateItemControls())
            {
                if (entry.control == null)
                    continue;

                var sel = GetSelectedItemsFromControl(entry.control);
                Logger.Debug("GetSelectedItems fallback: {Name} selectionCount={Count}", entry.name, sel.Count);
                if (sel.Count > 0)
                {
                    try { ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(sel.Count); } catch { }
                    return sel;
                }
            }

            var empty = new List<Models.ExplorerItemModel>();
            try
            {
                var shared = ServiceLocator.Instance.GetService<SharedService>();
                shared?.UpdateSelectionAvailability(0);
            }
            catch { }
            return empty;
        }

        void PerformSelectAll()
        {
            try
            {
                Logger.Debug("PerformSelectAll invoked");
                var visible = EnumerateItemControls().FirstOrDefault(entry => entry.control?.IsVisible == true);
                if (visible.control != null)
                {
                    Logger.Debug("PerformSelectAll: using {Name}", visible.name);
                    SelectAllInItemsControl(visible.control);
                    return;
                }

                var fallback = EnumerateItemControls().FirstOrDefault(entry => entry.control != null && ControlHasItems(entry.control));
                if (fallback.control != null)
                {
                    Logger.Debug("PerformSelectAll fallback: using {Name}", fallback.name);
                    SelectAllInItemsControl(fallback.control);
                    try
                    {
                        var sel = GetSelectedItems();
                        ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(sel.Count);
                    }
                    catch { }
                }
            }
            catch { }
        }

        void PerformSelectNone()
        {
            try
            {
                Logger.Debug("PerformSelectNone invoked");
                var visible = EnumerateItemControls().FirstOrDefault(entry => entry.control?.IsVisible == true);
                if (visible.control != null)
                {
                    Logger.Debug("PerformSelectNone: clearing {Name}", visible.name);
                    ClearSelection(visible.control);
                    try { ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(0); } catch { }
                    return;
                }

                var fallback = EnumerateItemControls().FirstOrDefault(entry => entry.control != null && ControlHasItems(entry.control));
                if (fallback.control != null)
                {
                    Logger.Debug("PerformSelectNone fallback: clearing {Name}", fallback.name);
                    ClearSelection(fallback.control);
                    try { ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(0); } catch { }
                }
            }
            catch { }
        }

        void PerformInvertSelection()
        {
            try
            {
                Logger.Debug("PerformInvertSelection invoked");
                var visible = EnumerateItemControls().FirstOrDefault(entry => entry.control?.IsVisible == true);
                if (visible.control != null)
                {
                    Logger.Debug("PerformInvertSelection: using {Name}", visible.name);
                    InvertSelectionInItemsControl(visible.control);
                    return;
                }

                var fallback = EnumerateItemControls().FirstOrDefault(entry => entry.control != null && ControlHasItems(entry.control));
                if (fallback.control != null)
                {
                    Logger.Debug("PerformInvertSelection fallback: using {Name}", fallback.name);
                    InvertSelectionInItemsControl(fallback.control);
                }
            }
            catch { }
        }

        static void SelectAllInItemsControl(object control)
        {
            if (control == null)
                return;

            if (control is DataGrid dg)
            {
                if (!TryGetItemsEnumerable(dg, out var items, out var itemsSourceUsed))
                    return;

                Logger.Debug("SelectAllInItemsControl: DataGrid.{Source} type={Type} isEmpty={IsEmpty}", itemsSourceUsed, items?.GetType().FullName ?? "(null)", !(items ?? Array.Empty<object>()).Cast<object?>().Any());
                var idx = 0;
                foreach (var it in (items ?? Array.Empty<object>()).Cast<object?>().Take(5))
                {
                    if (it is Models.ExplorerItemModel em)
                    {
                        var identity = (em.Object as Jaya.Shared.Models.FileSystemObjectModel)?.Path ?? em.DisplayName ?? "(unknown)";
                        Logger.Debug("SelectAllInItemsControl: DataGrid item[{Index}] Id={Id}", idx, identity);
                    }
                    else
                        Logger.Debug("SelectAllInItemsControl: DataGrid item[{Index}] Type={Type}", idx, it?.GetType().FullName ?? "(null)");
                    idx++;
                }
                if (dg.SelectedItems != null)
                {
                    var sidx = 0;
                    foreach (var sit in (dg.SelectedItems ?? Array.Empty<object>()).Cast<object?>().Take(5))
                    {
                        if (sit is Models.ExplorerItemModel sem)
                        {
                            var sidentity = (sem.Object as Jaya.Shared.Models.FileSystemObjectModel)?.Path ?? sem.DisplayName ?? "(unknown)";
                            Logger.Debug("SelectAllInItemsControl: DataGrid.Selected[{Index}] Id={Id}", sidx, sidentity);
                        }
                        else
                            Logger.Debug("SelectAllInItemsControl: DataGrid.Selected[{Index}] Type={Type}", sidx, sit?.GetType().FullName ?? "(null)");
                        sidx++;
                    }
                }
                var total = (items ?? Array.Empty<object>()).Cast<object?>().Count();
                var before = dg.SelectedItems?.Count ?? 0;
                Logger.Debug("SelectAllInItemsControl: DataGrid totalItems={Total} selectedBefore={Before}", total, before);
                dg.SelectedItems?.Clear();
                foreach (var it in items ?? Array.Empty<object>())
                {
                    if (it is Models.ExplorerItemModel m)
                        dg.SelectedItems?.Add(m);
                }
                var after = dg.SelectedItems?.Count ?? 0;
                Logger.Debug("SelectAllInItemsControl: DataGrid selectedAfter={After}", after);
                var first = (items ?? Array.Empty<object>()).Cast<object?>().FirstOrDefault();
                if (first is Models.ExplorerItemModel fm)
                    dg.SelectedItem = fm;
            }
            else if (control is ListBox lb)
            {
                if (!TryGetItemsEnumerable(lb, out var items, out _))
                    return;

                Logger.Debug("SelectAllInItemsControl: ListBox.Items type={Type} isEmpty={IsEmpty}", items?.GetType().FullName ?? "(null)", !(items ?? Array.Empty<object>()).Cast<object?>().Any());
                var idx = 0;
                foreach (var it in (items ?? Array.Empty<object>()).Cast<object?>().Take(5))
                {
                    if (it is Models.ExplorerItemModel em)
                    {
                        var identity = (em.Object as Jaya.Shared.Models.FileSystemObjectModel)?.Path ?? em.DisplayName ?? "(unknown)";
                        Logger.Debug("SelectAllInItemsControl: ListBox item[{Index}] Id={Id}", idx, identity);
                    }
                    else
                        Logger.Debug("SelectAllInItemsControl: ListBox item[{Index}] Type={Type}", idx, it?.GetType().FullName ?? "(null)");
                    idx++;
                }
                if (lb.SelectedItems != null)
                {
                    var sidx = 0;
                    foreach (var sit in (lb.SelectedItems ?? Array.Empty<object>()).Cast<object?>().Take(5))
                    {
                        if (sit is Models.ExplorerItemModel sem)
                        {
                            var sidentity = (sem.Object as Jaya.Shared.Models.FileSystemObjectModel)?.Path ?? sem.DisplayName ?? "(unknown)";
                            Logger.Debug("SelectAllInItemsControl: ListBox.Selected[{Index}] Id={Id}", sidx, sidentity);
                        }
                        else
                            Logger.Debug("SelectAllInItemsControl: ListBox.Selected[{Index}] Type={Type}", sidx, sit?.GetType().FullName ?? "(null)");
                        sidx++;
                    }
                }
                var total = (items ?? Array.Empty<object>()).Cast<object?>().Count();
                var before = lb.SelectedItems?.Count ?? 0;
                Logger.Debug("SelectAllInItemsControl: ListBox totalItems={Total} selectedBefore={Before}", total, before);
                lb.SelectedItems?.Clear();
                foreach (var it in items ?? Array.Empty<object>())
                {
                    if (it is Models.ExplorerItemModel m)
                        lb.SelectedItems?.Add(m);
                }
                var after = lb.SelectedItems?.Count ?? 0;
                Logger.Debug("SelectAllInItemsControl: ListBox selectedAfter={After}", after);
                var first = (items ?? Array.Empty<object>()).Cast<object?>().FirstOrDefault();
                if (first is Models.ExplorerItemModel fm)
                    lb.SelectedItem = fm;
            }
        }

        static void InvertSelectionInItemsControl(object control)
        {
            if (control == null)
                return;

            if (control is DataGrid dg)
            {
                if (!TryGetItemsEnumerable(dg, out var items, out var itemsSourceUsed))
                    return;

                var total = (items ?? Array.Empty<object>()).Cast<object?>().Count();
                var before = dg.SelectedItems?.Count ?? 0;
                var toSelect = new List<Models.ExplorerItemModel>();
                foreach (var it in items ?? Array.Empty<object>())
                {
                    if (it is Models.ExplorerItemModel m)
                    {
                        if (!(dg.SelectedItems != null && dg.SelectedItems.Contains(m)))
                            toSelect.Add(m);
                    }
                }
                dg.SelectedItems?.Clear();
                foreach (var m in toSelect)
                    dg.SelectedItems?.Add(m);
                var after = dg.SelectedItems?.Count ?? 0;
                Logger.Debug("InvertSelectionInItemsControl: DataGrid.{Source} total={Total} selectedBefore={Before} selectedAfter={After}", itemsSourceUsed, total, before, after);
                var first = toSelect.FirstOrDefault();
                if (first != null)
                    dg.SelectedItem = first;
            }
            else if (control is ListBox lb)
            {
                if (!TryGetItemsEnumerable(lb, out var items, out _))
                    return;

                var total = (items ?? Array.Empty<object>()).Cast<object?>().Count();
                var before = lb.SelectedItems?.Count ?? 0;
                var toSelect = new List<Models.ExplorerItemModel>();
                foreach (var it in items ?? Array.Empty<object>())
                {
                    if (it is Models.ExplorerItemModel m)
                    {
                        if (!(lb.SelectedItems != null && lb.SelectedItems.Contains(m)))
                            toSelect.Add(m);
                    }
                }
                lb.SelectedItems?.Clear();
                foreach (var m in toSelect)
                    lb.SelectedItems?.Add(m);
                var after = lb.SelectedItems?.Count ?? 0;
                Logger.Debug("InvertSelectionInItemsControl: ListBox total={Total} selectedBefore={Before} selectedAfter={After}", total, before, after);
                var first = toSelect.FirstOrDefault();
                if (first != null)
                    lb.SelectedItem = first;
            }
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

            foreach (var entry in EnumerateItemControls())
            {
                if (entry.control?.IsVisible == true && SelectInControl(entry.control, pathSet))
                    return;
            }

            foreach (var entry in EnumerateItemControls())
            {
                if (SelectInControl(entry.control, pathSet))
                    return;
            }
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
