//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using System;
using System.Linq;
using Avalonia.Media;

namespace Jaya.Ui.Views
{
    public partial class AddressbarView : UserControl
    {
        public AddressbarView()
        {
            this.InitializeComponent();
            this.DataContextChanged += AddressbarView_DataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void AddressBox_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var vm = DataContext as ViewModels.AddressbarViewModel;
            if (vm == null)
                return;

            if (vm.EnterEditModeCommand?.CanExecute(null) == true)
                vm.EnterEditModeCommand.Execute(null);

            var combo = this.FindControl<ComboBox>("PART_Combo");
            var breadcrumbs = this.FindControl<ItemsControl>("PART_Breadcrumbs");

            var addrBorder = sender as Control;
            Console.WriteLine($"[Addressbar] AddressBox.Bounds (local): {addrBorder?.Bounds}");
            try
            {
                var posOnAddr = e.GetPosition(addrBorder);
                Console.WriteLine($"[Addressbar] Pointer position relative to AddressBox: {posOnAddr}");
            }
            catch { }
            if (breadcrumbs != null)
            {
                Console.WriteLine($"[Addressbar] Breadcrumbs.Bounds (local): {breadcrumbs.Bounds}");
                var p = breadcrumbs.TranslatePoint(new Point(0, 0), this.GetVisualRoot() as Visual);
                Console.WriteLine($"[Addressbar] Breadcrumbs.TopLeft (window): {p}");
                try
                {
                    var posOnBc = e.GetPosition(breadcrumbs);
                    Console.WriteLine($"[Addressbar] Pointer position relative to Breadcrumbs: {posOnBc}");
                }
                catch { }
            }

            if (combo != null)
            {
                // Hide breadcrumbs so the combo fully occludes them
                if (breadcrumbs != null)
                    breadcrumbs.IsVisible = false;

                combo.IsVisible = true;
                combo.Background = Brushes.White;
                combo.Opacity = 1.0;

                // Align combo to exactly match breadcrumbs bounds (size and position)
                try
                {
                    if (breadcrumbs != null && addrBorder != null)
                    {
                        // Copy breadcrumbs' position (margin)
                        combo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                        combo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                        combo.Margin = new Thickness(breadcrumbs.Bounds.X, breadcrumbs.Bounds.Y, 0, 0);
                        
                        // Expand width to fill right side, leaving ~44px for refresh button
                        var expandedWidth = addrBorder.Bounds.Width - breadcrumbs.Bounds.X - 50;
                        combo.Width = expandedWidth > 0 ? expandedWidth : breadcrumbs.Bounds.Width;
                        
                        // Set height to match breadcrumb minus 10px to account for control padding
                        var calculatedHeight = Math.Max(20, breadcrumbs.Bounds.Height);
                        combo.Height = calculatedHeight;
                        Console.WriteLine($"[Addressbar] Breadcrumbs height: {breadcrumbs.Bounds.Height}, Calculated combo height: {calculatedHeight}");
                    }
                }
                catch { }

                // Wait for layout to update, then log accurate bounds and focus
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // Adjust height after layout is complete
                        if (breadcrumbs != null && combo != null)
                        {
                            var adjustedHeight = Math.Max(20, breadcrumbs.Bounds.Height - 8);
                            combo.Height = adjustedHeight;
                            Console.WriteLine($"[Addressbar] Post-layout height adjustment: {adjustedHeight}");
                        }
                        
                        Console.WriteLine($"[Addressbar] Combo.Bounds (local-after-layout): {combo.Bounds}");
                        var p2 = combo.TranslatePoint(new Point(0, 0), this.GetVisualRoot() as Visual);
                        Console.WriteLine($"[Addressbar] Combo.TopLeft (window-after-layout): {p2}");
                        try
                        {
                            var posOnCombo = e.GetPosition(combo);
                            Console.WriteLine($"[Addressbar] Pointer position relative to Combo: {posOnCombo}");
                        }
                        catch { }
                        // Also log pointer position relative to window
                        try
                        {
                            var root = this.GetVisualRoot() as Visual;
                            var posOnRoot = e.GetPosition(root);
                            Console.WriteLine($"[Addressbar] Pointer position relative to Window: {posOnRoot}");
                        }
                        catch { }
                        combo.Focus();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Addressbar] Post-layout logging failed: {ex}");
                    }
                }, Avalonia.Threading.DispatcherPriority.Render);
            }
        }

        private void AddressbarView_DataContextChanged(object? sender, EventArgs e)
        {
            var vm = DataContext as ViewModels.AddressbarViewModel;
            if (vm == null)
                return;

            vm.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(ViewModels.AddressbarViewModel.IsInEditMode))
                {
                    var breadcrumbs = this.FindControl<ItemsControl>("PART_Breadcrumbs");
                    var combo = this.FindControl<ComboBox>("PART_Combo");
                    var val = vm.IsInEditMode;
                    // update visuals on UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (breadcrumbs != null)
                            breadcrumbs.IsVisible = !val;
                        if (combo != null)
                        {
                            combo.IsVisible = val;
                            if (val)
                                combo.Focus();
                        }
                    }, Avalonia.Threading.DispatcherPriority.Render);
                }
            };
        }

        private void PART_Combo_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            var vm = DataContext as ViewModels.AddressbarViewModel;
            if (vm == null)
                return;

            if (e.Key == Avalonia.Input.Key.Enter)
            {
                if (vm.CommitAddressCommand?.CanExecute(null) == true)
                    vm.CommitAddressCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                if (vm.CancelEditCommand?.CanExecute(null) == true)
                    vm.CancelEditCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
