//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Ui;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public class RibbonViewModel: ViewModelBase
    {
        readonly SharedService? _shared;
        ICommand? _openWindow;
        ICommand? _toggleRibbon;
        ICommand? _openTerminal;
        ICommand? _openVsCode;
        ICommand? _openInFinder;
        ICommand? _simpleCommand;

        public RibbonViewModel()
        {
            _shared = GetService<SharedService>();
            if (_shared != null)
                _shared.PropertyChanged += Shared_PropertyChanged;
        }

        void Shared_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.PropertyName)) return;
            if (e.PropertyName == nameof(SharedService.IsPasteEnabled))
                Invoke(() => RaisePropertyChanged(nameof(IsPasteEnabled)));
            if (e.PropertyName == nameof(SharedService.IsCutEnabled))
                Invoke(() => RaisePropertyChanged(nameof(IsCutEnabled)));
            if (e.PropertyName == nameof(SharedService.IsCopyPathEnabled))
                Invoke(() => RaisePropertyChanged(nameof(IsCopyPathEnabled)));
            if (e.PropertyName == nameof(SharedService.IsCopyEnabled))
                Invoke(() => RaisePropertyChanged(nameof(IsCopyEnabled)));
        }

        public bool IsPasteEnabled => _shared?.IsPasteEnabled ?? false;

        public bool IsCutEnabled => _shared?.IsCutEnabled ?? false;

        public bool IsCopyPathEnabled => _shared?.IsCopyPathEnabled ?? false;

        public bool IsCopyEnabled => _shared?.IsCopyEnabled ?? false;

        public ToolbarConfigModel ToolbarConfig => _shared?.ToolbarConfiguration ?? new ToolbarConfigModel();

        public PaneConfigModel PaneConfig => _shared?.PaneConfiguration ?? new PaneConfigModel();

        public ApplicationConfigModel ApplicationConfig => _shared?.ApplicationConfiguration ?? new ApplicationConfigModel();

        public ICommand OpenWindowCommand
        {
            get
            {
                if (_openWindow == null)
                    _openWindow = GetService<NavigationService>()?.OpenWindowCommand;

                return _openWindow!;
            }
        }

        public ICommand ToggleRibbonCommand
        {
            get
            {
                if (_toggleRibbon == null)
                    _toggleRibbon = new RelayCommand(ToggleRibbonAction);

                return _toggleRibbon!;
            }
        }

        public ICommand OpenTerminalCommand
        {
            get
            {
                if (_openTerminal == null)
                    _openTerminal = new RelayCommand(OpenTerminalAction);

                return _openTerminal!;
            }
        }

        public ICommand OpenVsCodeCommand
        {
            get
            {
                if (_openVsCode == null)
                    _openVsCode = new RelayCommand(OpenVsCodeAction);

                return _openVsCode!;
            }
        }

        public ICommand OpenInFinderCommand
        {
            get
            {
                if (_openInFinder == null)
                    _openInFinder = new RelayCommand(OpenInFinderAction);

                return _openInFinder!;
            }
        }

        public new ICommand SimpleCommand
        {
            get
            {
                if (_simpleCommand == null)
                    _simpleCommand = new RelayCommand<CommandType>(SimpleCommandAction);

                return _simpleCommand!;
            }
        }

        void SimpleCommandAction(CommandType type)
        {
            try
            {
                EventAggregator?.Publish((byte)type);
            }
            catch { }
        }

        void ToggleRibbonAction()
        {
            PaneConfig.IsRibbonCollapsed = !PaneConfig.IsRibbonCollapsed;
        }

        void OpenTerminalAction()
        {
            try
            {
                EventAggregator?.Publish(new OpenTerminalRequestedEventArgs());
            }
            catch { }
        }

        void OpenVsCodeAction()
        {
            try
            {
                EventAggregator?.Publish(new OpenVsCodeRequestedEventArgs());
            }
            catch { }
        }

        void OpenInFinderAction()
        {
            try
            {
                EventAggregator?.Publish(new OpenInFinderRequestedEventArgs());
            }
            catch { }
        }
    }
}
