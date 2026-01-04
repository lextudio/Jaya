//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Services;
using Jaya.Ui.Models;
using Serilog;
using System.Collections.Generic;

namespace Jaya.Ui.Services
{
    public sealed class SharedService : IService
    {
        readonly Subscription<byte> _onSimpleCommand;
        readonly Subscription<KeyValuePair<byte, object>> _onParameterizedCommand;

        readonly ICommandService _commandService;
        readonly IConfigurationService _configService;
        static readonly ILogger Logger = Log.ForContext<SharedService>();

        public SharedService(
            ICommandService commandService,
            IConfigurationService configService)
        {
            _commandService = commandService;
            _configService = configService;

            _onSimpleCommand = _commandService.EventAggregator.Subscribe<byte>(SimpleCommandAction);
            _onParameterizedCommand = _commandService.EventAggregator.Subscribe<KeyValuePair<byte, object>>(ParameterizedCommandAction);
        }

        ~SharedService()
        {
            _commandService.EventAggregator.UnSubscribe(_onSimpleCommand);
            _commandService.EventAggregator.UnSubscribe(_onParameterizedCommand);
        }

        #region properties

        public ApplicationConfigModel ApplicationConfiguration { get; private set; }

        public ToolbarConfigModel ToolbarConfiguration { get; private set; }

        public PaneConfigModel PaneConfiguration { get; private set; }

        public UpdateConfigModel UpdateConfiguration { get; private set; }

        #endregion

        internal void LoadConfigurations()
        {
            ApplicationConfiguration = _configService.GetOrDefault<ApplicationConfigModel>();
            ToolbarConfiguration = _configService.GetOrDefault<ToolbarConfigModel>();
            PaneConfiguration = _configService.GetOrDefault<PaneConfigModel>();
            UpdateConfiguration = _configService.GetOrDefault<UpdateConfigModel>();
        }

        internal void SaveConfigurations()
        {
            _configService.Set(ApplicationConfiguration);
            _configService.Set(ToolbarConfiguration);
            _configService.Set(PaneConfiguration);
            _configService.Set(UpdateConfiguration);
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
