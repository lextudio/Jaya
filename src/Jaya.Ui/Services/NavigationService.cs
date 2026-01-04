//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Serilog;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Jaya.Ui.ViewModels.Windows;
using Jaya.Ui.Views.Windows;
using System;
using System.Collections.Generic;

namespace Jaya.Ui.Services
{
    public sealed class NavigationService : IService
    {
        static readonly ILogger Logger = Log.ForContext<NavigationService>();

        readonly CommandService _commandService;
        readonly Stack<SelectionChangedEventArgs> _backwardStack, _forwardStack;
        readonly Subscription<SelectionChangedEventArgs> _onSelectionChanged;
        RelayCommand _navigateBack, _navigateForward;
        RelayCommand<WindowOptionsModel> _openWindow;
        SelectionChangedEventArgs _directoryChangedArgs;

        public NavigationService(ICommandService commandService)
        {
            _commandService = commandService as CommandService;

            _backwardStack = new Stack<SelectionChangedEventArgs>();
            _forwardStack = new Stack<SelectionChangedEventArgs>();
            _onSelectionChanged = _commandService.EventAggregator.Subscribe<SelectionChangedEventArgs>(SelectionChanged);
        }

        ~NavigationService()
        {
            _commandService.EventAggregator.UnSubscribe(_onSelectionChanged);
        }

        #region properties

        public RelayCommand<WindowOptionsModel> OpenWindowCommand
        {
            get
            {
                if (_openWindow == null)
                    _openWindow = new RelayCommand<WindowOptionsModel>(OpenWindowCommandAction);

                return _openWindow;
            }
        }

        public RelayCommand NavigateBackCommand
        {
            get
            {
                if (_navigateBack == null)
                    _navigateBack = new RelayCommand(NavigateBack, false);

                return _navigateBack;
            }
        }

        public RelayCommand NavigateForwardCommand
        {
            get
            {
                if (_navigateForward == null)
                    _navigateForward = new RelayCommand(NavigateForward, false);

                return _navigateForward;
            }
        }

        #endregion

        async void OpenWindowCommandAction(WindowOptionsModel option)
        {
            var window = new HostView();

            var viewModel = window.DataContext as HostViewModel;
            viewModel.Option = option;

            window.Content = Activator.CreateInstance(option.ContentType);

            await window.ShowDialog(App.Lifetime.MainWindow);
        }

        void NavigateBack()
        {
            if (_backwardStack.Count <= 1)
            {
                Logger.Information("NavigateBack invoked but no previous selection available. BackCount={BackCount}", _backwardStack.Count);
                return;
            }

            // Pop current selection and move it to forward stack
            var current = _backwardStack.Pop();
            _forwardStack.Push(current);

            // The new top of backward stack is the previous selection we should navigate to
            var target = _backwardStack.Peek();

            NavigateBackCommand.IsEnabled = _backwardStack.Count > 1;
            NavigateForwardCommand.IsEnabled = true;

            Logger.Information("NavigateBack: target Service={Service}, Account={Account}, Directory={Directory}. BackCount={BackCount}, ForwardCount={ForwardCount}",
                target.Service?.Name,
                target.Account?.Name,
                target.Directory?.Path ?? target.Directory?.Name,
                _backwardStack.Count,
                _forwardStack.Count);

            var args = target.Clone(NavigationDirection.Backward);
            _commandService.EventAggregator.Publish(args);
        }

        void NavigateForward()
        {
            if (_forwardStack.Count == 0)
            {
                Logger.Information("NavigateForward invoked but forward stack is empty.");
                return;
            }

            // Pop the next item to navigate to
            var next = _forwardStack.Pop();
            _backwardStack.Push(next);

            NavigateBackCommand.IsEnabled = true;
            NavigateForwardCommand.IsEnabled = _forwardStack.Count > 0;

            Logger.Information("NavigateForward: target Service={Service}, Account={Account}, Directory={Directory}. BackCount={BackCount}, ForwardCount={ForwardCount}",
                next.Service?.Name,
                next.Account?.Name,
                next.Directory?.Path ?? next.Directory?.Name,
                _backwardStack.Count,
                _forwardStack.Count);

            var args = next.Clone(NavigationDirection.Forward);
            _commandService.EventAggregator.Publish(args);
        }

        void SelectionChanged(SelectionChangedEventArgs args)
        {
            Logger.Information("NavigationService.SelectionChanged: Direction={Direction}, Service={Service}, Account={Account}, Directory={Directory}",
                args.Direction,
                args.Service?.Name,
                args.Account?.Name,
                args.Directory?.Path ?? args.Directory?.Name);

            if (args.Direction == NavigationDirection.Unknown)
            {
                _backwardStack.Push(args);
                NavigateBackCommand.IsEnabled = true;
                Logger.Debug("Selection pushed to back stack. NewBackCount={Count}", _backwardStack.Count);
            }

            _directoryChangedArgs = args;
        }
    }
}
