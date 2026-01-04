//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jaya.Shared;
using Jaya.Ui.Services;
using Jaya.Ui.Views.Windows;

namespace Jaya.Ui
{
    public partial class App : Application
    {
        SharedService? _shared;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                _shared = ServiceLocator.Instance.GetService<SharedService>();
                _shared?.LoadConfigurations();

                lifetime.Exit += OnExit;
                lifetime.MainWindow = new MainView();
            }

            base.OnFrameworkInitializationCompleted();
        }

        void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            _shared?.SaveConfigurations();
            Lifetime.Exit -= OnExit;
        }

        internal static IClassicDesktopStyleApplicationLifetime Lifetime => (IClassicDesktopStyleApplicationLifetime)Current! .ApplicationLifetime!;
    }
}
