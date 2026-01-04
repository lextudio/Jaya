//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Ui.Models;
using Jaya.Ui.Services;
using Serilog;
using System.Collections.Generic;

namespace Jaya.Ui.ViewModels
{
    public class OptionsViewModel : ViewModelBase
    {
        readonly SharedService? _shared;
        static readonly ILogger Logger = Log.ForContext<OptionsViewModel>();
        bool _prevIsRibbonVisible;
        bool _prevIsStatusBarVisible;
        bool _prevIsMenuHeaderVisible;
        bool _prevToolbarVisible;

        public OptionsViewModel()
        {
            _shared = GetService<SharedService>();
            EnsureThemeLoaded();
            // snapshot current values to detect changes when committing
            if (_shared != null)
            {
                var pane = _shared.PaneConfiguration;
                var toolbar = _shared.ToolbarConfiguration;
                if (pane != null)
                {
                    _prevIsRibbonVisible = pane.IsRibbonVisible;
                    _prevIsStatusBarVisible = pane.IsStatusBarVisible;
                    _prevIsMenuHeaderVisible = pane.IsMenuHeaderVisible;
                }

                if (toolbar != null)
                    _prevToolbarVisible = toolbar.IsVisible;
            }
        }

        public IEnumerable<ThemeModel> Themes => ThemeManager.Instance.Themes;

        public ApplicationConfigModel ApplicationConfig => _shared!.ApplicationConfiguration;

        public PaneConfigModel PaneConfig => _shared!.PaneConfiguration;

        public ThemeModel SelectedThemePreview
        {
            get => Get<ThemeModel>();
            set => Set(value);
        }

        void EnsureThemeLoaded()
        {
            if (_shared?.ApplicationConfiguration == null)
                return;

            var currentTheme = _shared.ApplicationConfiguration.Theme;
            if (currentTheme == null)
            {
                currentTheme = ThemeManager.Instance.SelectedTheme;
                _shared.ApplicationConfiguration.Theme = currentTheme;
            }

            SelectedThemePreview = currentTheme ?? ThemeManager.Instance.SelectedTheme;
        }

        public void LogOpenState()
        {
            try
            {
                var pane = _shared?.PaneConfiguration;
                var toolbar = _shared?.ToolbarConfiguration;
                Logger.Information("Options opened: IsRibbonVisible={Ribbon}, IsStatusBarVisible={Status}, IsMenuHeaderVisible={Menu}, Toolbar.IsVisible={Toolbar}",
                    pane?.IsRibbonVisible, pane?.IsStatusBarVisible, pane?.IsMenuHeaderVisible, toolbar?.IsVisible);
            }
            catch (System.Exception ex)
            {
                Logger.Warning(ex, "Failed to log options open state");
            }
        }

        public void LogAfterCommit()
        {
            try
            {
                var pane = _shared?.PaneConfiguration;
                var toolbar = _shared?.ToolbarConfiguration;
                Logger.Information("Options committed: IsRibbonVisible={Ribbon}, IsStatusBarVisible={Status}, IsMenuHeaderVisible={Menu}, Toolbar.IsVisible={Toolbar}",
                    pane?.IsRibbonVisible, pane?.IsStatusBarVisible, pane?.IsMenuHeaderVisible, toolbar?.IsVisible);
            }
            catch (System.Exception ex)
            {
                Logger.Warning(ex, "Failed to log options committed state");
            }
        }

        public void LogDiscard()
        {
            try
            {
                var pane = _shared?.PaneConfiguration;
                var toolbar = _shared?.ToolbarConfiguration;
                Logger.Information("Options discarded: IsRibbonVisible={Ribbon}, IsStatusBarVisible={Status}, IsMenuHeaderVisible={Menu}, Toolbar.IsVisible={Toolbar}",
                    pane?.IsRibbonVisible, pane?.IsStatusBarVisible, pane?.IsMenuHeaderVisible, toolbar?.IsVisible);
            }
            catch { }
        }

        public void LogImmediateChange(string source)
        {
            try
            {
                var pane = _shared?.PaneConfiguration;
                var toolbar = _shared?.ToolbarConfiguration;
                Logger.Information("Options immediate change ({Source}): IsRibbonVisible={Ribbon}, IsStatusBarVisible={Status}, IsMenuHeaderVisible={Menu}, Toolbar.IsVisible={Toolbar}",
                    source, pane?.IsRibbonVisible, pane?.IsStatusBarVisible, pane?.IsMenuHeaderVisible, toolbar?.IsVisible);
                _shared?.SaveConfigurations();
            }
            catch (System.Exception ex)
            {
                Logger.Warning(ex, "Failed to log immediate options change");
            }
        }

        public void CommitChanges()
        {
            if (SelectedThemePreview == null)
                return;

            Logger.Information("Committing theme {Theme}", SelectedThemePreview.Name);
            _shared!.ApplicationConfiguration.Theme = SelectedThemePreview;
            ThemeManager.Instance.ApplyTheme(SelectedThemePreview);
            // Log any changes made in the options dialog for diagnostics
            try
            {
                var pane = _shared.PaneConfiguration;
                var toolbar = _shared.ToolbarConfiguration;

                if (pane != null)
                {
                    if (_prevIsRibbonVisible != pane.IsRibbonVisible)
                        Logger.Information("Options: IsRibbonVisible changed from {Old} to {New}", _prevIsRibbonVisible, pane.IsRibbonVisible);

                    if (_prevIsStatusBarVisible != pane.IsStatusBarVisible)
                        Logger.Information("Options: IsStatusBarVisible changed from {Old} to {New}", _prevIsStatusBarVisible, pane.IsStatusBarVisible);

                    if (_prevIsMenuHeaderVisible != pane.IsMenuHeaderVisible)
                        Logger.Information("Options: IsMenuHeaderVisible changed from {Old} to {New}", _prevIsMenuHeaderVisible, pane.IsMenuHeaderVisible);
                }

                if (toolbar != null)
                {
                    if (_prevToolbarVisible != toolbar.IsVisible)
                        Logger.Information("Options: Toolbar.IsVisible changed from {Old} to {New}", _prevToolbarVisible, toolbar.IsVisible);
                    // also log the toolbar sub-visibility flags for completeness
                    Logger.Debug("Options: Toolbar state File={File}, Edit={Edit}, View={View}, Help={Help}",
                        toolbar.IsFileVisible, toolbar.IsEditVisible, toolbar.IsViewVisible, toolbar.IsHelpVisible);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning(ex, "Failed to log options changes");
            }

            _shared!.SaveConfigurations();

            // update snapshot
            if (_shared != null)
            {
                var pane = _shared.PaneConfiguration;
                var toolbar = _shared.ToolbarConfiguration;
                if (pane != null)
                {
                    _prevIsRibbonVisible = pane.IsRibbonVisible;
                    _prevIsStatusBarVisible = pane.IsStatusBarVisible;
                    _prevIsMenuHeaderVisible = pane.IsMenuHeaderVisible;
                }

                if (toolbar != null)
                    _prevToolbarVisible = toolbar.IsVisible;
            }
        }

        public void DiscardChanges()
        {
            SelectedThemePreview = _shared!.ApplicationConfiguration.Theme ?? ThemeManager.Instance.SelectedTheme;
            Logger.Information("Discarding options dialog changes");
            // Log that changes were discarded and current effective state
            try
            {
                var pane = _shared!.PaneConfiguration;
                var toolbar = _shared.ToolbarConfiguration;
                if (pane != null)
                    Logger.Debug("Options discarded: IsRibbonVisible={Ribbon}, IsStatusBarVisible={Status}, IsMenuHeaderVisible={Menu}", pane.IsRibbonVisible, pane.IsStatusBarVisible, pane.IsMenuHeaderVisible);
                if (toolbar != null)
                    Logger.Debug("Options discarded: Toolbar.IsVisible={Toolbar}", toolbar.IsVisible);
            }
            catch { }
        }
    }
}
