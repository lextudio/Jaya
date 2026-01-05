//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Services;
using Jaya.Ui;
using Jaya.Ui.Models;
using Serilog;
using System.Collections.Generic;

namespace Jaya.Ui.Services
{
    public sealed class SharedService : IService, System.ComponentModel.INotifyPropertyChanged
    {
        bool _isPasteEnabled = false;
        readonly Subscription<byte> _onSimpleCommand;
        readonly Subscription<KeyValuePair<byte, object>> _onParameterizedCommand;

        readonly ICommandService _commandService;
        readonly IConfigurationService _configService;
        static readonly ILogger Logger = Log.ForContext(typeof(SharedService)).ForContext("SourceContext", "Settings");

        public SharedService(
            ICommandService commandService,
            IConfigurationService configService)
        {
            _commandService = commandService;
            _configService = configService;

            _onSimpleCommand = _commandService.EventAggregator.Subscribe<byte>(SimpleCommandAction);
            _onParameterizedCommand = _commandService.EventAggregator.Subscribe<KeyValuePair<byte, object>>(ParameterizedCommandAction);

            LoadConfigurations();
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public bool IsPasteEnabled
        {
            get => _isPasteEnabled;
            private set
            {
                if (_isPasteEnabled == value) return;
                _isPasteEnabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsPasteEnabled)));
            }
        }

        public void UpdatePasteAvailability(System.Collections.IEnumerable? clipboardItems)
        {
            // Only consider file system objects as valid clipboard items for paste
            bool anyFiles = false;
            if (clipboardItems != null)
            {
                foreach (var item in clipboardItems)
                {
                    if (item is Jaya.Shared.Models.FileSystemObjectModel)
                    {
                        anyFiles = true;
                        break;
                    }
                }
            }

            IsPasteEnabled = anyFiles;
        }

        ~SharedService()
        {
            if (_commandService != null)
            {
                if (_onSimpleCommand != null)
                    _commandService.EventAggregator.UnSubscribe(_onSimpleCommand);

                if (_onParameterizedCommand != null)
                    _commandService.EventAggregator.UnSubscribe(_onParameterizedCommand);
            }

            if (ApplicationConfiguration != null)
                ApplicationConfiguration.PropertyChanged -= ApplicationConfiguration_PropertyChanged;
        }

        #region properties

        public ApplicationConfigModel ApplicationConfiguration { get; private set; } = new ApplicationConfigModel();

        public ToolbarConfigModel ToolbarConfiguration { get; private set; } = new ToolbarConfigModel();

        public PaneConfigModel PaneConfiguration { get; private set; } = new PaneConfigModel();

        public UpdateConfigModel UpdateConfiguration { get; private set; } = new UpdateConfigModel();

        #endregion

        internal void LoadConfigurations()
        {
            ApplicationConfiguration = _configService.GetOrDefault<ApplicationConfigModel>() ?? new ApplicationConfigModel();
            ToolbarConfiguration = _configService.GetOrDefault<ToolbarConfigModel>() ?? new ToolbarConfigModel();
            PaneConfiguration = _configService.GetOrDefault<PaneConfigModel>() ?? new PaneConfigModel();
            UpdateConfiguration = _configService.GetOrDefault<UpdateConfigModel>() ?? new UpdateConfigModel();
            // Log changes to interesting configuration properties so UI binding issues can be diagnosed.
            if (ApplicationConfiguration != null)
            {
                ApplicationConfiguration.PropertyChanged += ApplicationConfiguration_PropertyChanged;
                Logger.Debug("Initial ApplicationConfiguration.IsFileNameExtensionVisible={IsFileNameExtensionVisible}", ApplicationConfiguration.IsFileNameExtensionVisible);
                Logger.Debug("Initial ApplicationConfiguration.IsHiddenItemVisible={IsHiddenItemVisible}", ApplicationConfiguration.IsHiddenItemVisible);
                Logger.Information("Initial ApplicationConfiguration.DetailsViewSortSettings count={Count}",
                    ApplicationConfiguration.DetailsViewSortSettings?.Count ?? 0);
            }
        }

        internal void SaveConfigurations()
        {
            _configService.Set(ApplicationConfiguration);
            _configService.Set(ToolbarConfiguration);
            _configService.Set(PaneConfiguration);
            _configService.Set(UpdateConfiguration);
            Logger.Information("Saved configuration snapshot: DetailsViewSortSettings count={Count}",
                ApplicationConfiguration?.DetailsViewSortSettings?.Count ?? 0);
        }

        public void SimpleCommandAction(byte type)
        {
            var command = (CommandType)type;
            Logger.Debug("Executing toolbar command {Command}", command);
            var persistToolbar = false;
            switch (command)
            {
                case CommandType.ToggleItemCheckBoxes:
                    ApplicationConfiguration.IsItemCheckBoxVisible = !ApplicationConfiguration.IsItemCheckBoxVisible;
                    break;

                case CommandType.ToggleFileNameExtensions:
                    ApplicationConfiguration.IsFileNameExtensionVisible = !ApplicationConfiguration.IsFileNameExtensionVisible;
                    break;

                case CommandType.ToggleHiddenItems:
                    ApplicationConfiguration.IsHiddenItemVisible = !ApplicationConfiguration.IsHiddenItemVisible;
                    break;

                case CommandType.ToggleToolbars:
                    ToolbarConfiguration.IsVisible = !ToolbarConfiguration.IsVisible;
                    persistToolbar = true;
                    break;

                case CommandType.ToggleToolbarFile:
                    ToolbarConfiguration.IsFileVisible = !ToolbarConfiguration.IsFileVisible;
                    persistToolbar = true;
                    break;

                case CommandType.ToggleToolbarEdit:
                    ToolbarConfiguration.IsEditVisible = !ToolbarConfiguration.IsEditVisible;
                    persistToolbar = true;
                    break;

                case CommandType.ToggleToolbarView:
                    ToolbarConfiguration.IsViewVisible = !ToolbarConfiguration.IsViewVisible;
                    persistToolbar = true;
                    break;

                case CommandType.ToggleToolbarHelp:
                    ToolbarConfiguration.IsHelpVisible = !ToolbarConfiguration.IsHelpVisible;
                    persistToolbar = true;
                    break;

                case CommandType.TogglePaneNavigation:
                    PaneConfiguration.IsNavigationPaneVisible = !PaneConfiguration.IsNavigationPaneVisible;
                    break;

                case CommandType.TogglePanePreview:
                    PaneConfiguration.IsPreviewPaneVisible = !PaneConfiguration.IsPreviewPaneVisible;
                    break;

                case CommandType.TogglePaneDetails:
                    PaneConfiguration.IsDetailsPaneVisible = !PaneConfiguration.IsDetailsPaneVisible;
                    break;

                case CommandType.Exit:
                    App.Lifetime.Shutdown();
                    break;

                case CommandType.Open:
                    // Publish an open-requested event so interested view-models (ExplorerViewModel)
                    // or services can handle opening the selected item(s).
                    try
                    {
                        _commandService.EventAggregator.Publish(new OpenRequestedEventArgs());
                    }
                    catch { }
                    break;
                case CommandType.Cut:
                    try
                    {
                        _commandService.EventAggregator.Publish(new CutRequestedEventArgs());
                    }
                    catch { }
                    break;
                case CommandType.Copy:
                    try
                    {
                        _commandService.EventAggregator.Publish(new CopyRequestedEventArgs());
                    }
                    catch { }
                    break;
                case CommandType.Paste:
                    try
                    {
                        _commandService.EventAggregator.Publish(new PasteRequestedEventArgs());
                    }
                    catch { }
                    break;
                case CommandType.NewFolder:
                    try
                    {
                        _commandService.EventAggregator.Publish(new NewFolderRequestedEventArgs());
                    }
                    catch { }
                    break;
                case CommandType.Delete:
                    try
                    {
                        _commandService.EventAggregator.Publish(new DeleteRequestedEventArgs());
                    }
                    catch { }
                    break;
            }
            if (persistToolbar)
            {
                try
                {
                    _configService.Set(ToolbarConfiguration);
                }
                catch (System.Exception ex)
                {
                    Logger.Warning(ex, "Failed to persist toolbar configuration immediately");
                }
            }

            Logger.Debug("PaneConfig states: Ribbon={Ribbon}, MenuHeader={MenuHeader}, ToolbarVisible={Toolbar}",
                PaneConfiguration.IsRibbonVisible,
                PaneConfiguration.IsMenuHeaderVisible,
                ToolbarConfiguration.IsVisible);
            LogPaneState(command.ToString());
        }

        void ParameterizedCommandAction(KeyValuePair<byte, object> parameter)
        {
            var command = (CommandType)parameter.Key;
        }

        void ApplicationConfiguration_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.PropertyName))
                return;

            if (e.PropertyName == nameof(ApplicationConfigModel.IsFileNameExtensionVisible))
            {
                Logger.Information("ApplicationConfiguration.{Property} changed to {Value}", e.PropertyName, ApplicationConfiguration?.IsFileNameExtensionVisible);
            }
        }

        void LogPaneState(string source)
        {
            if (PaneConfiguration == null || ToolbarConfiguration == null)
                return;

            Logger.Information("Pane state ({Source}): RibbonVisible={Ribbon}, MenuHeader={Menu}, Toolbar.IsVisible={Toolbar}, StatusBarVisible={Status}",
                source,
                PaneConfiguration.IsRibbonVisible,
                PaneConfiguration.IsMenuHeaderVisible,
                ToolbarConfiguration.IsVisible,
                PaneConfiguration.IsStatusBarVisible);
        }
    }
}
