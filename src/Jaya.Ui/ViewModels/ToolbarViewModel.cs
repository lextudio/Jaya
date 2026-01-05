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
    public class ToolbarViewModel : ViewModelBase
    {
        readonly SharedService? _shared;
        ICommand? _openWindow;
        ICommand? _openTerminal;

        public ToolbarViewModel()
        {
            _shared = GetService<SharedService>();
            if (_shared != null)
                _shared.PropertyChanged += Shared_PropertyChanged;
        }

        void Shared_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.PropertyName)) return;
            if (e.PropertyName == nameof(SharedService.IsPasteEnabled))
            {
                Invoke(() => RaisePropertyChanged(nameof(IsPasteEnabled)));
            }
        }

        public bool IsPasteEnabled => _shared?.IsPasteEnabled ?? false;

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

        public ICommand OpenTerminalCommand
        {
            get
            {
                if (_openTerminal == null)
                    _openTerminal = new RelayCommand(OpenTerminalAction);

                return _openTerminal!;
            }
        }

        void OpenTerminalAction()
        {
            try
            {
                EventAggregator?.Publish(new OpenTerminalRequestedEventArgs());
            }
            catch { }
        }
    }
}
