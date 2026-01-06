using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Serilog;
using Avalonia;
using System.Linq;
using Avalonia.Controls.Primitives;
using System.Collections.Generic;

namespace Jaya.Ui.Behaviors
{
    public static class ContextMenuBehavior
    {
        static readonly ILogger Logger = Log.ForContext(typeof(ContextMenuBehavior)).ForContext("SourceContext", "Behaviors");

        static readonly object OpenMenusGate = new();
        static readonly HashSet<ContextMenu> OpenMenus = new();

        static void TrackMenu(ContextMenu menu)
        {
            try
            {
                if (menu == null) return;
                menu.Opened -= Menu_Opened;
                menu.Closed -= Menu_Closed;
                menu.Opened += Menu_Opened;
                menu.Closed += Menu_Closed;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "ContextMenuBehavior: failed to track menu");
            }
        }

        static void Menu_Opened(object? sender, EventArgs e)
        {
            if (sender is not ContextMenu cm)
                return;
            lock (OpenMenusGate)
                OpenMenus.Add(cm);
            Logger.Debug("ContextMenuBehavior: menu opened IsOpen={IsOpen} PlacementTarget={Target}", cm.IsOpen, cm.PlacementTarget);
        }

        static void Menu_Closed(object? sender, EventArgs e)
        {
            if (sender is not ContextMenu cm)
                return;
            lock (OpenMenusGate)
                OpenMenus.Remove(cm);
            Logger.Debug("ContextMenuBehavior: menu closed");
        }

        static void CloseTrackedMenus(string reason, Control? scope)
        {
            ContextMenu[] menus;
            lock (OpenMenusGate)
                menus = OpenMenus.ToArray();

            Logger.Debug("ContextMenuBehavior: closing {Count} tracked menus. Reason={Reason} Scope={Scope}", menus.Length, reason, scope?.Name);

            foreach (var cm in menus)
            {
                try
                {
                    Logger.Debug("ContextMenuBehavior: closing menu IsOpen={IsOpen} PlacementTarget={Target}", cm.IsOpen, cm.PlacementTarget);
                    cm.Close();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "ContextMenuBehavior: failed closing tracked menu");
                }
            }
        }

        public static readonly AttachedProperty<string?> EmptySpaceMenuResourceKeyProperty = AvaloniaProperty.RegisterAttached<Control, string?>("EmptySpaceMenuResourceKey", typeof(ContextMenuBehavior));

        // New: accept a direct ContextMenu instance for empty-space menu
        public static readonly AttachedProperty<ContextMenu?> EmptySpaceContextMenuProperty = AvaloniaProperty.RegisterAttached<Control, ContextMenu?>("EmptySpaceContextMenu", typeof(ContextMenuBehavior));

        // New: allow consumers to supply a factory to create/choose a ContextMenu for a row/item DataContext
        public static readonly AttachedProperty<Func<object?, ContextMenu?>?> RowContextMenuFactoryProperty = AvaloniaProperty.RegisterAttached<Control, Func<object?, ContextMenu?>?>("RowContextMenuFactory", typeof(ContextMenuBehavior));

        // Private attached flag to avoid double-attaching handlers
        static readonly AttachedProperty<bool> IsHookedProperty = AvaloniaProperty.RegisterAttached<Control, bool>("IsHooked", typeof(ContextMenuBehavior));

        static readonly AttachedProperty<bool> IsTopLevelHookedProperty = AvaloniaProperty.RegisterAttached<Control, bool>("IsTopLevelHooked", typeof(ContextMenuBehavior));

        public static string? GetEmptySpaceMenuResourceKey(Control? control) => control == null ? null : control.GetValue(EmptySpaceMenuResourceKeyProperty);
        public static void SetEmptySpaceMenuResourceKey(Control? control, string? value)
        {
            if (control == null) return;
            control.SetValue(EmptySpaceMenuResourceKeyProperty, value);
            EnsureHandlers(control);
        }

        public static ContextMenu? GetEmptySpaceContextMenu(Control? control) => control == null ? null : control.GetValue(EmptySpaceContextMenuProperty);
        public static void SetEmptySpaceContextMenu(Control? control, ContextMenu? value)
        {
            if (control == null) return;
            control.SetValue(EmptySpaceContextMenuProperty, value);
            EnsureHandlers(control);
        }

        public static Func<object?, ContextMenu?>? GetRowContextMenuFactory(Control? control) => control == null ? null : control.GetValue(RowContextMenuFactoryProperty);
        public static void SetRowContextMenuFactory(Control? control, Func<object?, ContextMenu?>? value)
        {
            if (control == null) return;
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
                Logger.Debug("ContextMenuBehavior: handlers attached for control {Control}", control.Name);
                control.AddHandler(Avalonia.Controls.Control.ContextRequestedEvent, OnContextRequested, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);

                var top = TopLevel.GetTopLevel(control);
                if (top is Control topControl)
                {
                    if (!topControl.GetValue(IsTopLevelHookedProperty))
                    {
                        topControl.SetValue(IsTopLevelHookedProperty, true);
                        Logger.Debug("ContextMenuBehavior: attaching TopLevel pointer handler for {TopLevel}", topControl.GetType().Name);
                        topControl.AddHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                    }
                }

                // Track any ContextMenu instances already present on descendant controls so they are observed
                try
                {
                    var descendants = control.GetVisualDescendants().OfType<Control>();
                    foreach (var c in descendants)
                    {
                        try
                        {
                            var cm = c.ContextMenu;
                            if (cm != null)
                                TrackMenu(cm);
                        }
                        catch { }
                    }
                    // Also track the root control's ContextMenu if present
                    try { if (control.ContextMenu != null) TrackMenu(control.ContextMenu); } catch { }
                }
                catch { }
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
                    Logger.Debug("ContextMenuBehavior: pointer pressed (left) on {Control}, closing tracked context menus", control?.Name);
                    CloseTrackedMenus("Control.PointerPressed.Left", control);
                    // Best-effort fallback for any menus not tracked
                    CloseOpenContextMenus(control);
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Error in ContextMenuBehavior.OnPointerPressed"); }
        }

        static void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (e == null) return;
                // Any left click anywhere in the TopLevel closes tracked menus.
                var relativeTo = sender as Visual;
                var pt = e.GetCurrentPoint(relativeTo);
                if (pt.Properties.IsLeftButtonPressed)
                {
                    CloseTrackedMenus("TopLevel.PointerPressed.Left", sender as Control);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Error in ContextMenuBehavior.OnTopLevelPointerPressed");
            }
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
                Logger.Debug("ContextMenuBehavior: ContextRequested on {Control} (withinItem={Within}) source={Source}", control?.Name, withinItem, source?.GetType().Name);

                // If the click was inside an item/row, prefer the RowContextMenuFactory if provided.
                // If the factory is missing or returns null, fall back to opening/tracking the item container's own ContextMenu.
                if (withinItem)
                {
                    var itemVisual = FindItemContainer(source);
                    var itemControl = itemVisual as Control;
                    var dataContext = itemControl?.DataContext;

                    Logger.Debug(
                        "ContextMenuBehavior: within item. Source={SourceType} ItemContainer={ItemType} DataContextType={DataContextType} HasContainerMenu={HasMenu}",
                        source?.GetType().Name,
                        itemControl?.GetType().Name ?? "<null>",
                        dataContext?.GetType().Name ?? "<null>",
                        itemControl?.ContextMenu != null);

                    var factory = GetRowContextMenuFactory(control);
                    if (factory != null)
                    {
                        try
                        {
                            var rowMenu = factory(dataContext);
                            Logger.Debug("ContextMenuBehavior: RowContextMenuFactory result HasMenu={HasMenu} DataContextType={DataContext}", rowMenu != null, dataContext?.GetType().Name ?? "<null>");
                            if (rowMenu != null)
                            {
                                if (e.TryGetPosition(control, out var rpt))
                                {
                                    rowMenu.PlacementTarget = control;
                                    rowMenu.PlacementRect = new Rect(rpt, new Size(1, 1));
                                }
                                // Ensure the menu's DataContext is the item's DataContext so bindings like CommandParameter="{Binding}" work
                                rowMenu.DataContext = dataContext;
                                TrackMenu(rowMenu);
                                Logger.Debug("ContextMenuBehavior: Opening row context menu Menu={Menu} DataContextType={DataContext}", rowMenu, dataContext?.GetType().Name ?? "<null>");
                                rowMenu.Open(control);
                                Logger.Debug("ContextMenuBehavior: row menu Open called");
                                e.Handled = true;
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "RowContextMenuFactory threw");
                        }
                    }
                    else
                    {
                        Logger.Debug("ContextMenuBehavior: RowContextMenuFactory is null; will try container ContextMenu");
                    }

                    // Fallback: if the item container itself has a ContextMenu, open it and track it.
                    var containerMenu = itemControl?.ContextMenu;
                        if (containerMenu != null)
                    {
                        // Determine the actual control that owns this ContextMenu instance. It's possible
                        // the ContextMenu object comes from a shared resource or from a parent control's
                        // template, in which case opening it on the current itemControl will throw
                        // "Cannot show ContentMenu on a different control to the one it is attached to".
                        Control? ownerForMenu = FindOwnerControlForMenu(containerMenu, control) ?? itemControl;

                        if (e.TryGetPosition(ownerForMenu ?? itemControl, out var rpt))
                        {
                            containerMenu.PlacementTarget = ownerForMenu ?? itemControl;
                            containerMenu.PlacementRect = new Rect(rpt, new Size(1, 1));
                        }
                        // Ensure container menu DataContext is the item's DataContext so MenuItem bindings referencing relative DataContext work
                        try { containerMenu.DataContext = dataContext; } catch { }
                        TrackMenu(containerMenu);
                        Logger.Debug("ContextMenuBehavior: Opening container ContextMenu Menu={Menu} DataContextType={DataContext} Owner={Owner}", containerMenu, dataContext?.GetType().Name ?? "<null>", ownerForMenu?.Name ?? ownerForMenu?.GetType().Name ?? "<unknown>");
                        try
                        {
                            // Open the menu on the control that actually owns it (or the best-effort owner).
                            containerMenu.Open(ownerForMenu ?? itemControl);
                            Logger.Debug("ContextMenuBehavior: container menu Open called");
                            e.Handled = true;
                        }
                        catch (ArgumentException aex)
                        {
                            // Defensive fallback: if opening still fails, log and try opening on the original control.
                            Logger.Warning(aex, "ContextMenuBehavior: failed opening container menu on owner; falling back to itemControl");
                            try
                            {
                                containerMenu.Open(itemControl);
                                e.Handled = true;
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning(ex, "ContextMenuBehavior: fallback open also failed for container menu");
                            }
                        }
                    }

                    // Do not open empty-space menu when within an item.
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
                    TrackMenu(directMenu);
                    Logger.Debug("ContextMenuBehavior: Opening empty-space direct context menu at {Point}", pt);
                    directMenu.Open(control);
                    Logger.Debug("ContextMenuBehavior: empty-space direct menu Open called");
                    e.Handled = true;
                    return;
                }

                var resKey = GetEmptySpaceMenuResourceKey(control);
                if (!string.IsNullOrWhiteSpace(resKey))
                {
                    ContextMenu? menu = null;
                    try { menu = control.FindResource(resKey) as ContextMenu; } catch { menu = null; }

                    if (menu != null)
                    {
                        if (e.TryGetPosition(control, out var pt))
                        {
                            menu.PlacementTarget = control;
                            menu.PlacementRect = new Rect(pt, new Size(1, 1));
                        }
                        TrackMenu(menu);
                        Logger.Debug("ContextMenuBehavior: Opening empty-space resource menu {Key} at {Point}", resKey, pt);
                        menu.Open(control);
                        Logger.Debug("ContextMenuBehavior: empty-space resource menu Open called");
                        e.Handled = true;
                    }
                    else
                    {
                        Logger.Debug("ContextMenuBehavior: Empty-space resource menu not found for key={Key}", resKey);
                    }
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
                var menus = root.GetVisualDescendants().OfType<ContextMenu>().ToList();
                Logger.Debug("ContextMenuBehavior: CloseOpenContextMenus found {Count} menus under {Control}", menus.Count, root?.Name);
                foreach (var cm in menus)
                {
                    try
                    {
                        Logger.Debug("ContextMenuBehavior: Closing descendant menu IsOpen={IsOpen} PlacementTarget={Target}", cm.IsOpen, cm.PlacementTarget);
                        cm.Close();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed closing descendant menu");
                    }
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Error closing context menus"); }
        }

        // Public helper for callers to close all context menus within a control
        public static void CloseAllContextMenus(Control? root)
        {
            Logger.Debug("ContextMenuBehavior.CloseAllContextMenus called for {Control}", root?.Name);
            CloseTrackedMenus("CloseAllContextMenus", root);
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
            // First, prefer the actual item container types
            var cur = visual;
            while (cur != null)
            {
                if (cur is Avalonia.Controls.DataGridRow || cur is ListBoxItem)
                    return cur;
                // prefer a parent control that already defines a ContextMenu (the real container)
                if (cur is Control cc && cc.ContextMenu != null)
                    return cur;
                cur = Avalonia.VisualTree.VisualExtensions.GetVisualParent(cur) as Visual;
            }

            // Fallback: return the nearest control that has a DataContext (best-effort)
            cur = visual;
            while (cur != null)
            {
                if (cur is Control ic && ic.DataContext != null)
                    return cur;
                cur = Avalonia.VisualTree.VisualExtensions.GetVisualParent(cur) as Visual;
            }

            return null;
        }

        // Find the control under the provided root (or global top) whose ContextMenu property
        // references the supplied ContextMenu instance. Returns null if not found.
        static Control? FindOwnerControlForMenu(ContextMenu menu, Control? root)
        {
            try
            {
                IEnumerable<Control> candidates;
                if (root != null)
                {
                    candidates = root.GetVisualDescendants().OfType<Control>();
                }
                else
                {
                    var app = Avalonia.Application.Current;
                    if (app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        candidates = desktop.Windows.OfType<TopLevel>().SelectMany(t => t.GetVisualDescendants().OfType<Control>());
                    }
                    else
                    {
                        return null;
                    }
                }

                foreach (var c in candidates)
                {
                    try
                    {
                        if (object.ReferenceEquals(c.ContextMenu, menu))
                            return c;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "ContextMenuBehavior: error finding owner control for ContextMenu");
            }
            return null;
        }
    }
}
