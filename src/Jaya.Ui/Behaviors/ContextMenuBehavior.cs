using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Serilog;
using Avalonia;
using System.Linq;

namespace Jaya.Ui.Behaviors
{
    public static class ContextMenuBehavior
    {
        static readonly ILogger Logger = Log.ForContext(typeof(ContextMenuBehavior)).ForContext("SourceContext", "Behaviors");

        public static readonly AttachedProperty<string?> EmptySpaceMenuResourceKeyProperty = AvaloniaProperty.RegisterAttached<Control, string?>("EmptySpaceMenuResourceKey", typeof(ContextMenuBehavior));

        public static string? GetEmptySpaceMenuResourceKey(Control control) => control.GetValue(EmptySpaceMenuResourceKeyProperty);
        public static void SetEmptySpaceMenuResourceKey(Control control, string? value)
        {
            control.SetValue(EmptySpaceMenuResourceKeyProperty, value);
            if (!string.IsNullOrEmpty(value))
            {
                control.AddHandler(Avalonia.Controls.Control.ContextRequestedEvent, OnContextRequested, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
            else
            {
                control.RemoveHandler(Avalonia.Controls.Control.ContextRequestedEvent, OnContextRequested);
                control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            }
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
                if (IsWithinItem(source))
                    return; // let item row/context menus handle it

                var resKey = GetEmptySpaceMenuResourceKey(control);
                if (string.IsNullOrWhiteSpace(resKey))
                    return;

                if (control.FindControl<ContextMenu>(resKey) is ContextMenu menu)
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
    }
}
