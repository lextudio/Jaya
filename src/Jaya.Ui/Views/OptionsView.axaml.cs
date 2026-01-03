//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Jaya.Ui.ViewModels;

namespace Jaya.Ui.Views
{
    public partial class OptionsView : UserControl
    {
        public OptionsView()
        {
            this.InitializeComponent();
            this.AttachedToVisualTree += OptionsView_AttachedToVisualTree;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        void OnOkClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is OptionsViewModel vm)
            {
                vm.CommitChanges();
                vm.LogAfterCommit();
                CloseHostWindow();
            }
        }

        void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is OptionsViewModel vm)
            {
                vm.DiscardChanges();
                vm.LogDiscard();
                CloseHostWindow();
            }
        }

        void OnPaneOptionChanged(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is OptionsViewModel vm))
                return;

            // Delay saving so two-way bindings have time to update the PaneConfig model.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    vm.LogImmediateChange("PaneCheckbox");
                }
                catch { }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private void OptionsView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (DataContext is OptionsViewModel vm)
                vm.LogOpenState();
        }

        void CloseHostWindow()
        {
            if (VisualRoot is Window window)
                window.Close();
        }
    }
}
