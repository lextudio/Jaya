//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.VisualTree;
using Jaya.Shared.Models;
using Jaya.Shared;
using Jaya.Ui.Services;
using Jaya.Shared.Services;


namespace Jaya.Ui.Views
{
    public partial class NavigationView : UserControl
    {
        public NavigationView()
        {
            this.InitializeComponent();
            this.AttachedToVisualTree += NavigationView_AttachedToVisualTree;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void NavigationView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            try
            {
                this.AddHandler(DragDrop.DragOverEvent, Navigation_DragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                this.AddHandler(DragDrop.DropEvent, Navigation_Drop, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                this.AddHandler(DragDrop.DragLeaveEvent, Navigation_DragLeave, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
            catch { }
        }

        void Navigation_DragLeave(object? sender, DragEventArgs e)
        {
            try
            {
                UpdateDropTargetHighlight(null);
            }
            catch { }
        }

        static readonly DataFormat<byte[]> JayaPathsFormat = DataFormat.CreateBytesApplicationFormat("Jaya.Paths");

        void Navigation_DragOver(object? sender, DragEventArgs e)
        {
            try
            {
                var dt = e.DataTransfer;
                if (dt != null && dt.Contains(JayaPathsFormat))
                {
                    e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;
                    e.Handled = true;
                    // Highlight target TreeViewItem
                    var pt = e.GetPosition(this);
                    var hit = this.InputHitTest(pt) as Avalonia.Visual;
                    var tvi = FindTreeViewItem(hit);
                    UpdateDropTargetHighlight(tvi);
                }
            }
            catch { }
        }

        async void Navigation_Drop(object? sender, DragEventArgs e)
        {
            try
            {
                var dt = e.DataTransfer;
                if (dt == null)
                    return;

                byte[]? payload = null;
                if (dt.Contains(JayaPathsFormat))
                {
                    payload = dt.TryGetValue(JayaPathsFormat);
                }

                if (payload == null)
                    return;

                string[] paths = System.Text.Json.JsonSerializer.Deserialize<string[]>(payload) ?? System.Array.Empty<string>();
                if (paths.Length == 0)
                    return;

                // Find drop target node via input hit test
                var pt = e.GetPosition(this);
                var visual = this.InputHitTest(pt) as Visual;
                var node = FindTreeNodeModel(visual);
                var targetDir = node?.FileSystemObject as DirectoryModel;
                
                // Reject drop if target is not a directory
                if (targetDir == null)
                {
                    e.Handled = true;
                    UpdateDropTargetHighlight(null);
                    return;
                }
                
                var effect = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;

                // Ask user to confirm the drop
                try
                {
                    // Remove highlight
                    var pt2 = e.GetPosition(this);
                    var hit2 = this.InputHitTest(pt2) as Avalonia.Visual;
                    var tviRemove = FindTreeViewItem(hit2);
                    UpdateDropTargetHighlight(null);

                    var confirm = new Jaya.Ui.Views.ConfirmDropView();
                    var targetPath = targetDir?.Path ?? "(root)";
                    var verb = effect == DragDropEffects.Copy ? "copy" : "move";
                    confirm.Message = $"Do you want to {verb} the dropped item(s) into '{targetPath}'?";
                    var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                    if (owner != null)
                    {
                        var result = await confirm.ShowDialog<bool?>(owner);
                        if (result == true)
                        {
                            var ea = ServiceLocator.Instance.GetService<ICommandService>()?.EventAggregator;
                            if (ea != null)
                            {
                                ea.Publish(new Jaya.Ui.TreeDropRequestedEventArgs(paths, targetDir, effect));
                                e.Handled = true;
                            }
                        }
                        else
                        {
                            // if canceled, ensure highlight cleared
                            UpdateDropTargetHighlight(null);
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        static Avalonia.Controls.TreeViewItem? FindTreeViewItem(Avalonia.Visual? visual)
        {
            var depth = 0;
            while (visual != null && depth < 60)
            {
                if (visual is Avalonia.Controls.TreeViewItem tvi)
                    return tvi;
                visual = visual.GetVisualParent() as Avalonia.Visual;
                depth++;
            }
            return null;
        }

        Avalonia.Controls.TreeViewItem? _currentDropTarget;
        void UpdateDropTargetHighlight(Avalonia.Controls.TreeViewItem? tvi)
        {
            try
            {
                if (!ReferenceEquals(_currentDropTarget, tvi))
                {
                    if (_currentDropTarget != null)
                        _currentDropTarget.Classes.Remove("drop-target");
                    _currentDropTarget = tvi;
                    if (_currentDropTarget != null)
                        _currentDropTarget.Classes.Add("drop-target");
                }
            }
            catch { }
        }

        static Jaya.Ui.Models.TreeNodeModel? FindTreeNodeModel(Visual? visual)
        {
            int depth = 0;
            while (visual != null && depth < 50)
            {
                if (visual.DataContext is Jaya.Ui.Models.TreeNodeModel tm)
                    return tm;

                visual = visual.GetVisualParent() as Visual;
                depth++;
            }

            return null;
        }
    }
}
