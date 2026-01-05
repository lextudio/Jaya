//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Jaya.Ui.ViewModels;
using Jaya.Shared.Controls;
using System;
using System.ComponentModel;

namespace Jaya.Ui.Views
{
    public partial class TransferProgressView : StyledWindow
    {
        TransferProgressViewModel? _viewModel;

        public TransferProgressView()
        {
            InitializeComponent();
            DataContextChanged += TransferProgressView_DataContextChanged;
            Closing += TransferProgressView_Closing;
        }

        void TransferProgressView_DataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.Finished -= ViewModel_Finished;
            }

            _viewModel = DataContext as TransferProgressViewModel;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                _viewModel.Finished += ViewModel_Finished;
            }
        }

        void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null || e == null)
                return;

            if (e.PropertyName == nameof(TransferProgressViewModel.IsCompleted) ||
                e.PropertyName == nameof(TransferProgressViewModel.IsCanceled))
            {
                if (_viewModel.IsCompleted || _viewModel.IsCanceled)
                {
                    Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
                }
            }
        }

        void ViewModel_Finished(object? sender, EventArgs e)
        {
            if (_viewModel == null)
                return;

            Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        }

        void TransferProgressView_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_viewModel == null)
                return;

            if (_viewModel.CanCancel && !_viewModel.IsCompleted && !_viewModel.IsCanceled)
            {
                _viewModel.CancelCommand.Execute(null);
            }
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
