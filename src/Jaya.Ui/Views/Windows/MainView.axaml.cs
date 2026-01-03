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
using Jaya.Ui.ViewModels.Windows;
using System;
using System.ComponentModel;

namespace Jaya.Ui.Views.Windows
{
    public partial class MainView : StyledWindow
    {
        MainViewModel _viewModel;

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
        }

        void OnDataContextChanged(object sender, EventArgs e)
        {
            AttachToViewModel();
        }

        void OnOpened(object sender, EventArgs e)
        {
            UpdateNativeMenu();
            UpdateHeaderContent();
            UpdateHeaderVisibility();
        }

        void OnClosed(object sender, EventArgs e)
        {
            DetachFromViewModel();
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
        }

        void DetachFromViewModel()
        {
            if (_viewModel?.PaneConfig != null)
                _viewModel.PaneConfig.PropertyChanged -= PaneConfig_PropertyChanged;

            _viewModel = null;
        }

        void PaneConfig_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PaneConfigModel.IsMenuHeaderVisible) ||
                e.PropertyName == nameof(PaneConfigModel.IsRibbonVisible))
            {
                UpdateNativeMenu();
                UpdateHeaderContent();
                UpdateHeaderVisibility();
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
                    HeaderContent = null;
                    return;
                }

                // Find the TitleMenu control in the visual tree
                var titleMenu = this.FindControl<MenuView>("TitleMenu");

                if (titleMenu == null)
                {
                    HeaderContent = null;
                    return;
                }

                if (paneConfig.IsMenuHeaderVisible && !paneConfig.IsRibbonVisible)
                    HeaderContent = titleMenu;
                else
                    HeaderContent = null;
            }
            catch
            {
                // Swallow errors to avoid crashing window initialization
                HeaderContent = null;
            }
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
    }
}
