//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;
using Jaya.Shared.Controls;
using Jaya.Ui.Helpers;
using Jaya.Ui.Models;
using Jaya.Ui.Views;
using Jaya.Ui.ViewModels.Windows;
using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using Avalonia.VisualTree;

namespace Jaya.Ui.Views.Windows
{
    public partial class MainView : StyledWindow
    {
        MainViewModel? _viewModel;
        MenuView? _inlineMenu;

        public MainView()
        {
            InitializeComponent();
#if DEBUG
#endif
            this.AttachDevTools();
            DataContextChanged += OnDataContextChanged;
            Opened += OnOpened;
            Closed += OnClosed;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _inlineMenu = this.FindControl<MenuView>("InlineMenu");
        }

        void OnDataContextChanged(object? sender, EventArgs e)
        {
            AttachToViewModel();
        }

        void OnOpened(object? sender, EventArgs e)
        {
            UpdateNativeMenu();
            UpdateHeaderContent();
            UpdateHeaderVisibility();
            UpdateInlineMenuVisibility();
            // Runtime diagnostics: log ribbon attachment and bounds to help debug layout/overlap
            try
            {
                // Look up the named RibbonView and inspect its first visual child (the actual Ribbon control)
                var ribbonView = this.FindControl<UserControl>("RibbonView");
                if (ribbonView == null)
                {
                    Console.WriteLine("[MainView] Named RibbonView not found");
                }
                else
                {
                    var firstChild = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(ribbonView).FirstOrDefault();
                    if (firstChild == null)
                    {
                        Console.WriteLine("[MainView] RibbonView has no visual children");
                    }
                    else
                    {
                        Console.WriteLine($"[MainView] Ribbon visual type: {firstChild.GetType().FullName}");
                        if (firstChild is Control ctrl)
                        {
                            Console.WriteLine($"[MainView] Ribbon.IsVisible: {ctrl.IsVisible}");
                            Console.WriteLine($"[MainView] Ribbon.Bounds: {ctrl.Bounds}");
                            Console.WriteLine($"[MainView] Ribbon.Parent: {ctrl.Parent?.GetType().FullName ?? "(null)"}");
                        }
                    }
                }
                // Check toolbar and addressbar positions to detect overlap
                try
                {
                    var tb = this.FindControl<UserControl>("ToolbarView");
                    var ab = this.FindControl<UserControl>("AddressbarView");
                    if (tb is Control tctrl)
                        Console.WriteLine($"[MainView] Toolbar.Bounds: {tctrl.Bounds}");
                    else
                        Console.WriteLine("[MainView] ToolbarView not found or not a Control");

                    if (ab is Control actrl)
                        Console.WriteLine($"[MainView] Addressbar.Bounds: {actrl.Bounds}");
                    else
                        Console.WriteLine("[MainView] AddressbarView not found or not a Control");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[MainView] Toolbar/Addressbar diagnostics error: " + ex);
                }

                // Additional: locate the search TextBox inside AddressbarView and print positions
                try
                {
                    var addressCtrl = this.FindControl<UserControl>("AddressbarView") as Control;
                    if (addressCtrl != null)
                    {
                        var searchBox = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(addressCtrl)
                            .OfType<Control>()
                            .FirstOrDefault(c => c.GetType().Name == "TextBox" && c.Classes.Contains("SearchBox"));
                        if (searchBox == null)
                        {
                            Console.WriteLine("[MainView] SearchBox not found inside AddressbarView");
                        }
                        else
                        {
                            Console.WriteLine($"[MainView] SearchBox.Bounds (local): {searchBox.Bounds}");
                            var ptToWindow = searchBox.TranslatePoint(new Avalonia.Point(0,0), this);
                            Console.WriteLine($"[MainView] SearchBox.TopLeft relative to window: {ptToWindow}");

                            // Compare against ribbon's first child ctrl if available
                            var ribbonView2 = this.FindControl<UserControl>("RibbonView");
                            var ribbonChild = ribbonView2 != null ? Avalonia.VisualTree.VisualExtensions.GetVisualChildren(ribbonView2).FirstOrDefault() as Control : null;
                            if (ribbonChild != null)
                            {
                                var ribbonBottom = ribbonChild.Bounds.Bottom;
                                Console.WriteLine($"[MainView] Ribbon bottom (local to RibbonView): {ribbonBottom}");
                                var ribbonLocToWindow = ribbonChild.TranslatePoint(new Avalonia.Point(0, ribbonBottom), this);
                                Console.WriteLine($"[MainView] Ribbon bottom relative to window: {ribbonLocToWindow}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[MainView] SearchBox diagnostics error: " + ex);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MainView] Ribbon diagnostics error: " + ex);
            }
        }

        void AttachToViewModel()
        {
            if (_viewModel?.PaneConfig != null)
                _viewModel.PaneConfig.PropertyChanged -= PaneConfig_PropertyChanged;

            _viewModel = DataContext as MainViewModel;

            if (_viewModel?.PaneConfig != null)
                _viewModel.PaneConfig.PropertyChanged += PaneConfig_PropertyChanged;

            UpdateNativeMenu();
            UpdateHeaderContent();
            UpdateInlineMenuVisibility();
        }

        void DetachFromViewModel()
        {
            if (_viewModel?.PaneConfig != null)
                _viewModel.PaneConfig.PropertyChanged -= PaneConfig_PropertyChanged;

            _viewModel = null;
        }

        void PaneConfig_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PaneConfigModel.IsMenuHeaderVisible) ||
                e.PropertyName == nameof(PaneConfigModel.IsRibbonVisible))
            {
                UpdateNativeMenu();
                UpdateHeaderContent();
                UpdateHeaderVisibility();
                UpdateInlineMenuVisibility();
            }
        }

        void UpdateNativeMenu()
        {
            var titleMenuCtrl = this.FindControl<MenuView>("TitleMenu");
            if (!OperatingSystem.IsMacOS() || titleMenuCtrl == null || titleMenuCtrl.MenuControl == null)
            {
                NativeMenu.SetMenu(this, null);
                return;
            }

            var paneConfig = _viewModel?.PaneConfig;
            if (paneConfig == null)
            {
                NativeMenu.SetMenu(this, null);
                return;
            }

            if (!paneConfig.IsMenuHeaderVisible && !paneConfig.IsRibbonVisible)
            {
                NativeMenu.SetMenu(this, NativeMenuHelper.BuildNativeMenu(titleMenuCtrl.MenuControl));
            }
            else
            {
                NativeMenu.SetMenu(this, null);
            }
        }

        void UpdateHeaderContent()
        {
            // TitleMenu is declared in XAML and will be part of the visual tree.
            // We only want to set the HeaderContent when the custom header is enabled
            // and hide it otherwise so the system/title bar is shown.
            try
            {
                var paneConfig = _viewModel?.PaneConfig;
                if (paneConfig == null)
                {
                    HeaderContent = null!;
                    return;
                }

                var titleMenu = this.FindControl<MenuView>("TitleMenu");
                if (titleMenu == null)
                {
                    HeaderContent = null!;
                    return;
                }

                if (paneConfig.IsMenuHeaderVisible && !paneConfig.IsRibbonVisible)
                {
                    HeaderContent = titleMenu;
                }
                else
                {
                    HeaderContent = null!;
                }
            }
            catch
            {
                HeaderContent = null!;
            }
        }

        void OnClosed(object? sender, EventArgs e)
        {
            DetachFromViewModel();
        }

        void UpdateHeaderVisibility()
        {
            try
            {
                var paneConfig = _viewModel?.PaneConfig;
                if (paneConfig == null)
                    return;

                bool show = paneConfig.IsMenuHeaderVisible && !paneConfig.IsRibbonVisible;

                // Template parts in StyledWindow
                var icon = this.FindControl<Control>("PART_Icon");
                var titleBar = this.FindControl<Border>("PART_TitleBar");
                var minimize = this.FindControl<Button>("PART_Minimize");
                var maximize = this.FindControl<Button>("PART_Maximize");
                var close = this.FindControl<Button>("PART_Close");

                if (icon != null)
                    icon.IsVisible = show;

                if (titleBar != null)
                    titleBar.IsVisible = show;

                if (minimize != null)
                    minimize.IsVisible = show;

                if (maximize != null)
                    maximize.IsVisible = show;

                if (close != null)
                    close.IsVisible = show;
            }
            catch
            {
                // ignore, don't crash on visual tree timing issues
            }
        }

        void UpdateInlineMenuVisibility()
        {
            if (_inlineMenu == null)
                return;

            var paneConfig = _viewModel?.PaneConfig;
            var shouldShow = paneConfig != null && !paneConfig.IsRibbonVisible && !paneConfig.IsMenuHeaderVisible;
            _inlineMenu.IsVisible = shouldShow;
        }

        // Removed heavy visual traversal; use named control lookups instead to keep code simple and compatible.
    }
}
