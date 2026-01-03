//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
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
            }
        }

        void UpdateNativeMenu()
        {
            if (!OperatingSystem.IsMacOS() || TitleMenu == null || TitleMenu.MenuControl == null)
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
                NativeMenu.SetMenu(this, NativeMenuHelper.BuildNativeMenu(TitleMenu.MenuControl));
            }
            else
            {
                NativeMenu.SetMenu(this, null);
            }
        }
    }
}
