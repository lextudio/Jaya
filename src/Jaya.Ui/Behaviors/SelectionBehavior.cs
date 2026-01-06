using System;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Serilog;
using Jaya.Shared;
using Jaya.Ui.Services;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Jaya.Ui.Behaviors
{
    public static class SelectionBehavior
    {
        static readonly ILogger Logger = Log.ForContext(typeof(SelectionBehavior)).ForContext("SourceContext", "Behaviors");

        public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(SelectionBehavior));
        public static readonly AttachedProperty<bool> SuppressWhileProperty = AvaloniaProperty.RegisterAttached<Control, bool>("SuppressWhile", typeof(SelectionBehavior));

        public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Control control, bool value)
        {
            control.SetValue(IsEnabledProperty, value);
            if (value)
                Attach(control);
            else
                Detach(control);
        }

        public static bool GetSuppressWhile(Control control) => control.GetValue(SuppressWhileProperty);
        public static void SetSuppressWhile(Control control, bool value) => control.SetValue(SuppressWhileProperty, value);

        static readonly Dictionary<Control, EventHandler<Avalonia.Controls.SelectionChangedEventArgs>> _selectionChangedMap = new();
        static readonly Dictionary<Control, NotifyCollectionChangedEventHandler> _collectionChangedMap = new();

        static void Attach(Control control)
        {
            try
            {
                if (control is DataGrid dg)
                {
                    EventHandler<Avalonia.Controls.SelectionChangedEventArgs> handler = (s, e) =>
                    {
                        try
                        {
                            if (GetSuppressWhile(control)) return;
                            var count = dg.SelectedItems?.Count ?? 0;
                            ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(count);
                        }
                        catch (Exception ex) { Logger.Warning(ex, "Selection handler (DataGrid) failed"); }
                    };
                    if (_selectionChangedMap.ContainsKey(control))
                        dg.SelectionChanged -= _selectionChangedMap[control];
                    _selectionChangedMap[control] = handler;
                    dg.SelectionChanged += handler;

                    if (dg.SelectedItems is INotifyCollectionChanged incc)
                    {
                        NotifyCollectionChangedEventHandler ch = (s, e) => SelectedCollectionChanged(s, e);
                        if (_collectionChangedMap.ContainsKey(control))
                            incc.CollectionChanged -= _collectionChangedMap[control];
                        _collectionChangedMap[control] = ch;
                        incc.CollectionChanged += ch;
                    }
                }
                else if (control is ListBox lb)
                {
                    EventHandler<Avalonia.Controls.SelectionChangedEventArgs> handler = (s, e) =>
                    {
                        try
                        {
                            if (GetSuppressWhile(control)) return;
                            var count = lb.SelectedItems?.Count ?? (lb.SelectedItem != null ? 1 : 0);
                            ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(count);
                        }
                        catch (Exception ex) { Logger.Warning(ex, "Selection handler (ListBox) failed"); }
                    };
                    if (_selectionChangedMap.ContainsKey(control))
                        lb.SelectionChanged -= _selectionChangedMap[control];
                    _selectionChangedMap[control] = handler;
                    lb.SelectionChanged += handler;

                    if (lb.SelectedItems is INotifyCollectionChanged incc)
                    {
                        NotifyCollectionChangedEventHandler ch = (s, e) => SelectedCollectionChanged(s, e);
                        if (_collectionChangedMap.ContainsKey(control))
                            incc.CollectionChanged -= _collectionChangedMap[control];
                        _collectionChangedMap[control] = ch;
                        incc.CollectionChanged += ch;
                    }
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Attach selection handlers failed"); }
        }

        static void Detach(Control control)
        {
            try
            {
                if (_selectionChangedMap.TryGetValue(control, out var handler))
                {
                    if (control is DataGrid dg)
                        dg.SelectionChanged -= handler;
                    else if (control is ListBox lb)
                        lb.SelectionChanged -= handler;
                    _selectionChangedMap.Remove(control);
                }

                if (_collectionChangedMap.TryGetValue(control, out var ch))
                {
                    if (control is DataGrid dg && dg.SelectedItems is INotifyCollectionChanged ding)
                        ding.CollectionChanged -= ch;
                    else if (control is ListBox lb && lb.SelectedItems is INotifyCollectionChanged ling)
                        ling.CollectionChanged -= ch;
                    _collectionChangedMap.Remove(control);
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "Detach selection handlers failed"); }
        }

        static void SelectedCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            try
            {
                // sender is the SelectedItems collection; try to find owning control by walking visual tree is not possible here.
                // Instead, find TopLevel and broadcast selection count via SharedService.
                // This is a best-effort: we count items in the collection if possible.
                if (sender is System.Collections.ICollection col)
                {
                    var count = col.Count;
                    ServiceLocator.Instance.GetService<SharedService>()?.UpdateSelectionAvailability(count);
                    Logger.Debug("SelectionBehavior: collection changed, count={Count}", count);
                }
            }
            catch (Exception ex) { Logger.Warning(ex, "SelectedCollectionChanged error"); }
        }
    }
}
