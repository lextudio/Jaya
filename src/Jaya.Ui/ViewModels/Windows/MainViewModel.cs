//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using Serilog;
using System.ComponentModel;

namespace Jaya.Ui.ViewModels.Windows
{
    public class MainViewModel : ViewModelBase
    {
        readonly Subscription<SelectionChangedEventArgs>? _onDirectoryChanged;
        readonly SharedService? _shared;
        static readonly ILogger Logger = Log.ForContext<MainViewModel>();
        public MainViewModel()
        {
            WindowTitle = Constants.APP_NAME;

            _onDirectoryChanged = EventAggregator?.Subscribe<SelectionChangedEventArgs>(DirectoryChanged);

            _shared = GetService<SharedService>();
            if (_shared?.ToolbarConfiguration != null)
                _shared.ToolbarConfiguration.PropertyChanged += OnPropertyChanged;
            if (_shared?.PaneConfiguration != null)
                _shared.PaneConfiguration.PropertyChanged += OnPropertyChanged;

            if (_shared != null)
                SimpleCommand = new RelayCommand<byte>(_shared.SimpleCommandAction);

            LogRibbonVisibilityAtStartup();
        }

        ~MainViewModel()
        {
            if (_shared?.ToolbarConfiguration != null)
                _shared.ToolbarConfiguration.PropertyChanged -= OnPropertyChanged;
            if (_shared?.PaneConfiguration != null)
                _shared.PaneConfiguration.PropertyChanged -= OnPropertyChanged;

            if (_onDirectoryChanged != null)
                EventAggregator?.UnSubscribe(_onDirectoryChanged);
        }

        public ToolbarConfigModel ToolbarConfig => _shared?.ToolbarConfiguration ?? new ToolbarConfigModel();

        public PaneConfigModel PaneConfig => _shared?.PaneConfiguration ?? new PaneConfigModel();

        public ApplicationConfigModel ApplicationConfig => _shared?.ApplicationConfiguration ?? new ApplicationConfigModel();

        public bool IsToolbarVisible => ToolbarConfig.IsVisible && !PaneConfig.IsRibbonVisible;

        public bool IsMenuVisible => PaneConfig.IsMenuHeaderVisible && !PaneConfig.IsRibbonVisible;

        public bool IsInlineMenuVisible => !PaneConfig.IsMenuHeaderVisible && !PaneConfig.IsRibbonVisible;

        public string WindowTitle
        {
            get => Get<string>();
            private set => Set(value);
        }

        void DirectoryChanged(SelectionChangedEventArgs args)
        {
            if (args.Account == null)
                WindowTitle = Constants.APP_NAME;
            else if (args.Directory == null || string.IsNullOrEmpty(args.Directory.Path))
                WindowTitle = args.Account.Name;
            else
                WindowTitle = args.Directory.Name;
        }

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (PaneConfig == null)
                return;

            Logger.Debug("PaneConfig property changed: {Property} -> RibbonVisible={Ribbon}, MenuHeader={Menu}",
                e.PropertyName,
                PaneConfig.IsRibbonVisible,
                PaneConfig.IsMenuHeaderVisible);

            switch(e.PropertyName)
            {
                case nameof(PaneConfigModel.IsRibbonVisible):
                    RaisePropertyChanged(nameof(IsToolbarVisible));
                    RaisePropertyChanged(nameof(IsMenuVisible));
                    RaisePropertyChanged(nameof(IsInlineMenuVisible));
                    break;
                case nameof(PaneConfigModel.IsMenuHeaderVisible):
                    RaisePropertyChanged(nameof(IsMenuVisible));
                    RaisePropertyChanged(nameof(IsInlineMenuVisible));
                    break;
                case nameof(ToolbarConfigModel.IsVisible):
                    RaisePropertyChanged(nameof(IsToolbarVisible));
                    break;
            }
        }

        void LogRibbonVisibilityAtStartup()
        {
            if (PaneConfig == null || ToolbarConfig == null)
                return;

            Logger.Information("Startup layout: RibbonVisible={Ribbon}, RibbonCollapsed={Collapsed}, MenuHeaderVisible={MenuHeader}, ToolbarVisible={Toolbar}",
                PaneConfig.IsRibbonVisible,
                PaneConfig.IsRibbonCollapsed,
                PaneConfig.IsMenuHeaderVisible,
                ToolbarConfig.IsVisible);

            if (!PaneConfig.IsRibbonVisible)
            {
                var reason = PaneConfig.IsMenuHeaderVisible ? "menu header is enabled" : "ribbon was disabled";
                Logger.Information("Ribbon UI is hidden at startup ({Reason})", reason);
                return;
            }

            if (PaneConfig.IsMenuHeaderVisible)
            {
                Logger.Information("Ribbon UI replaced by inline menu because menu header choice is enabled.");
                return;
            }

            if (PaneConfig.IsRibbonCollapsed)
            {
                Logger.Information("Ribbon UI is collapsed at startup; toggle via the ribbon collapse button.");
            }
        }
    }
}
