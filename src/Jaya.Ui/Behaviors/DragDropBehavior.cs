using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Jaya.Ui;
using Jaya.Shared.Models;
using System.Text.Json;
using Avalonia;
using Serilog;

namespace Jaya.Ui.Behaviors
{
    public static class DragDropBehavior
    {
        static readonly ILogger Logger = Log.ForContext(typeof(DragDropBehavior)).ForContext("SourceContext", "Behaviors");
        static readonly DataFormat<byte[]> JayaPathsFormat = DataFormat.CreateBytesApplicationFormat("Jaya.Paths");

        public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(DragDropBehavior));

        public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Control control, bool value)
        {
            control.SetValue(IsEnabledProperty, value);
            if (value)
            {
                control.AddHandler(DragDrop.DropEvent, OnDrop, Avalonia.Interactivity.RoutingStrategies.Bubble);
                control.AddHandler(DragDrop.DragOverEvent, OnDragOver, Avalonia.Interactivity.RoutingStrategies.Bubble);
            }
            else
            {
                control.RemoveHandler(DragDrop.DropEvent, OnDrop);
                control.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            }
        }

        static void OnDragOver(object? sender, DragEventArgs e)
        {
            try
            {
                var control = sender as Control;
                if (e.DataTransfer == null) return;
                if (!e.DataTransfer.Contains(JayaPathsFormat))
                {
                    e.DragEffects = DragDropEffects.None;
                    return;
                }
                e.DragEffects = DragDropEffects.Move | DragDropEffects.Copy;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Error in DragOver handler");
            }
        }

        static void OnDrop(object? sender, DragEventArgs e)
        {
            try
            {
                var dt = e.DataTransfer;
                if (dt == null || !dt.Contains(JayaPathsFormat))
                    return;

                var payload = dt.TryGetValue(JayaPathsFormat);
                var paths = payload != null ? (JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>()) : Array.Empty<string>();
                if (paths.Length == 0)
                    return;

                // Determine the target by visual under pointer if possible
                DirectoryModel? targetDir = null;
                var control = sender as Control;
                var viz = e.Source as Visual;
                if (viz != null)
                {
                    var model = FindExplorerItemModel(viz);
                    if (model != null && model.IsDirectory)
                        targetDir = model.Object as DirectoryModel;
                }

                var effect = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;

                // Publish a TreeDropRequestedEventArgs so existing subscribers (ExplorerView) can handle
                try
                {
                    var svc = Jaya.Shared.ServiceLocator.Instance.GetService<Jaya.Shared.Services.ICommandService>();
                    svc?.EventAggregator.Publish(new TreeDropRequestedEventArgs(paths, targetDir, effect));
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to publish TreeDropRequestedEventArgs");
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in DragDropBehavior.OnDrop");
            }
        }

        // Helper to walk visuals and find the ExplorerItemModel (copied from ExplorerView)
        static Models.ExplorerItemModel? FindExplorerItemModel(Visual? visual)
        {
            int depth = 0;
            while (visual != null && depth < 20)
            {
                if (visual.DataContext is Models.ExplorerItemModel model)
                    return model;
                visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual) as Visual;
                depth++;
            }
            return null;
        }
    }
}
