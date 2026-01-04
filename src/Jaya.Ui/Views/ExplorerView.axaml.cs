//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Jaya.Shared.Base;
using System.Collections;
using System.Collections.Generic;
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
                eventAggregator.Subscribe<DeleteRequestedEventArgs>(args =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var selectedItems = GetSelectedItems();
                            if (selectedItems.Count > 0)
                                vm?.DeleteItemsCommand.Execute(selectedItems);
                        }
                        catch { }
                    });
                });
            }
        }

        IReadOnlyList<Models.ExplorerItemModel> GetSelectedItems()
        {
            if (DetailsDataGrid?.IsVisible == true)
                return GetSelectedItems(DetailsDataGrid.SelectedItems, DetailsDataGrid.SelectedItem);

            if (ListListBox?.IsVisible == true)
                return GetSelectedItems(ListListBox.SelectedItems, ListListBox.SelectedItem);

            if (IconsListBox?.IsVisible == true)
                return GetSelectedItems(IconsListBox.SelectedItems, IconsListBox.SelectedItem);

            if (TilesListBox?.IsVisible == true)
                return GetSelectedItems(TilesListBox.SelectedItems, TilesListBox.SelectedItem);

            if (ContentListBox?.IsVisible == true)
                return GetSelectedItems(ContentListBox.SelectedItems, ContentListBox.SelectedItem);

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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
