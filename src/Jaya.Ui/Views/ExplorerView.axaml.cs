//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Jaya.Shared.Base;
using System.Linq;
using System.Diagnostics;
using Jaya.Ui.ViewModels;
using Jaya.Shared;
using Jaya.Shared.Services;
using Avalonia.Threading;

namespace Jaya.Ui.Views
{
    public partial class ExplorerView : UserControl
    {
        public ExplorerView()
        {
            this.InitializeComponent();
            if (!Design.IsDesignMode)
            {
                var vm = this.DataContext as ExplorerViewModel;
                var eventAggregator = ServiceLocator.Instance.GetService<ICommandService>().EventAggregator;
                eventAggregator.Subscribe<OpenRequestedEventArgs>(args =>
                {
                    // Attempt to open the selected item(s) by invoking ViewModel command on UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            // Prefer selected item from details grid, then listboxes
                            var selected = DetailsDataGrid?.SelectedItem as Models.ExplorerItemModel
                                           ?? ListListBox?.SelectedItem as Models.ExplorerItemModel
                                           ?? IconsListBox?.SelectedItem as Models.ExplorerItemModel
                                           ?? TilesListBox?.SelectedItem as Models.ExplorerItemModel
                                           ?? ContentListBox?.SelectedItem as Models.ExplorerItemModel;

                            if (selected != null)
                                vm?.InvokeObjectCommand.Execute(selected);
                        }
                        catch { }
                    });
                });
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
