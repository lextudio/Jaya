using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Serilog;
using Avalonia;
using System.Linq;
using Avalonia.Controls.Primitives;

namespace Jaya.Ui.Behaviors
{
    public static class ContextMenuBehavior
    {
        static readonly ILogger Logger = Log.ForContext(typeof(ContextMenuBehavior)).ForContext("SourceContext", "Behaviors");

        public static readonly AttachedProperty<string?> EmptySpaceMenuResourceKeyProperty = AvaloniaProperty.RegisterAttached<Control, string?>("EmptySpaceMenuResourceKey", typeof(ContextMenuBehavior));

        // New: accept a direct ContextMenu instance for empty-space menu
        public static readonly AttachedProperty<ContextMenu?> EmptySpaceContextMenuProperty = AvaloniaProperty.RegisterAttached<Control, ContextMenu?>("EmptySpaceContextMenu", typeof(ContextMenuBehavior));

        // New: allow consumers to supply a factory to create/choose a ContextMenu for a row/item DataContext
        public static readonly AttachedProperty<Func<object?, ContextMenu?>?> RowContextMenuFactoryProperty = AvaloniaProperty.RegisterAttached<Control, Func<object?, ContextMenu?>?>("RowContextMenuFactory", typeof(ContextMenuBehavior));

        // Private attached flag to avoid double-attaching handlers
        static readonly AttachedProperty<bool> IsHookedProperty = AvaloniaProperty.RegisterAttached<Control, bool>("IsHooked", typeof(ContextMenuBehavior));

        public static string? GetEmptySpaceMenuResourceKey(Control control) => control.GetValue(EmptySpaceMenuResourceKeyProperty);
        public static void SetEmptySpaceMenuResourceKey(Control control, string? value)
        {
            control.SetValue(EmptySpaceMenuResourceKeyProperty, value);
            EnsureHandlers(control);
        }

        public static ContextMenu? GetEmptySpaceContextMenu(Control control) => control.GetValue(EmptySpaceContextMenuProperty);
        public static void SetEmptySpaceContextMenu(Control control, ContextMenu? value)
        {
            control.SetValue(EmptySpaceContextMenuProperty, value);
            EnsureHandlers(control);
        }

        public static Func<object?, ContextMenu?>? GetRowContextMenuFactory(Control control) => control.GetValue(RowContextMenuFactoryProperty);
        public static void SetRowContextMenuFactory(Control control, Func<object?, ContextMenu?>? value)
        {
            control.SetValue(RowContextMenuFactoryProperty, value);
            EnsureHandlers(control);
        }

        static void EnsureHandlers(Control control)
        {
            try
            {
                if (control == null) return;
                if (control.GetValue(IsHookedProperty))
                    return;
                control.SetValue(IsHookedProperty, true);
                control.AddHandler(Avalonia.Controls.Control.ContextRequestedEvent, OnContextRequested, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
            catch (Exception ex) { Logger.Warning(ex, "Failed to ensure handlers"); }
        }

        static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var control = sender as Control;
                if (e == null) return;
                var point = e.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed)
                {
                    // Close any open context menus (mirrors previous behavior)
                    CloseOpenContextMenus(control);
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Error in ContextMenuBehavior.OnPointerPressed"); }
        }

        static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            try
            {
                var control = sender as Control;
                if (control == null || e == null) return;

                // Determine whether click is within an item visual
                var source = e.Source as Visual;
                var withinItem = IsWithinItem(source);

                // If the click was inside an item/row, prefer the RowContextMenuFactory if provided
                if (withinItem)
                {
                    var factory = GetRowContextMenuFactory(control);
                    if (factory != null)
                    {
                        // find the nearest data context for the item (DataGridRow/ListBoxItem)
                        var itemVisual = FindItemContainer(source);
                        var dataContext = (itemVisual as Control)?.DataContext;
                        try
                        {
                            var rowMenu = factory(dataContext);
                            if (rowMenu != null)
                            {
                                if (e.TryGetPosition(control, out var rpt))
                                {
                                    rowMenu.PlacementTarget = control;
                                    rowMenu.PlacementRect = new Rect(rpt, new Size(1, 1));
                                }
                                rowMenu.Open(control);
                                e.Handled = true;
                                return;
                            }
                        }
                        catch (Exception ex) { Logger.Warning(ex, "RowContextMenuFactory threw"); }
                    }

                    // No factory handled it — do not open empty-space menu; allow row's own context menu to handle.
                    return;
                }

                // Not within an item — prefer direct ContextMenu instance, then resource key
                var directMenu = GetEmptySpaceContextMenu(control);
                if (directMenu != null)
                {
                    if (e.TryGetPosition(control, out var pt))
                    {
                        directMenu.PlacementTarget = control;
                        directMenu.PlacementRect = new Rect(pt, new Size(1, 1));
                    }
                    directMenu.Open(control);
                    e.Handled = true;
                    return;
                }

                var resKey = GetEmptySpaceMenuResourceKey(control);
                if (!string.IsNullOrWhiteSpace(resKey) && control.FindControl<ContextMenu>(resKey) is ContextMenu menu)
                {
                    if (e.TryGetPosition(control, out var pt))
                    {
                        menu.PlacementTarget = control;
                        menu.PlacementRect = new Rect(pt, new Size(1, 1));
                    }
                    menu.Open(control);
                    e.Handled = true;
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Error in ContextMenuBehavior.OnContextRequested"); }
        }

        // Helper: close context menus found under the control's visual tree (best-effort)
        static void CloseOpenContextMenus(Control? root)
        {
            try
            {
                if (root == null) return;
                foreach (var cm in root.GetVisualDescendants().OfType<ContextMenu>())
                {
                    try { cm.Close(); } catch { }
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Error closing context menus"); }
        }

        // Public helper for callers to close all context menus within a control
        public static void CloseAllContextMenus(Control? root)
        {
            CloseOpenContextMenus(root);
        }

        static bool IsWithinItem(Visual? visual)
        {
            while (visual != null)
            {
                if (visual is Avalonia.Controls.DataGridRow || visual is ListBoxItem)
                    return true;
                visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual) as Visual;
            }
            return false;
        }

        static Visual? FindItemContainer(Visual? visual)
        {
            while (visual != null)
            {
                if (visual is Avalonia.Controls.DataGridRow || visual is ListBoxItem)
                    return visual;
                // also accept any control that has a DataContext set (best-effort)
                if (visual is Control ic && ic.DataContext != null)
                    return visual;
                visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual) as Visual;
            }

            return null;
        }
    }
}
