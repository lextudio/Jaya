//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Jaya.Shared.Base;
using Jaya.Shared.Controls;
using System;
using System.Globalization;
using System.Reflection;

namespace Jaya.Shared
{
    public static class ViewModelLocator
    {
        public static readonly AvaloniaProperty AutoWireViewModelProperty;

        static ViewModelLocator()
        {
            AutoWireViewModelProperty = AvaloniaProperty.RegisterAttached<Control, bool>("AutoWireViewModel", typeof(ViewModelLocator), false);
            AutoWireViewModelProperty.Changed.Subscribe(args => { if (args != null) AutoWireViewModelChanged(args.Sender, args); });
        }

        public static bool GetAutoWireViewModel(AvaloniaObject control)
        {
            var val = control.GetValue(AutoWireViewModelProperty);
            return val is bool b && b;
        }

        public static void SetAutoWireViewModel(AvaloniaObject control, bool value)
        {
            control.SetValue(AutoWireViewModelProperty, value);
        }

        static void AutoWireViewModelChanged(Avalonia.AvaloniaObject control, AvaloniaPropertyChangedEventArgs e)
        {
            if (Design.IsDesignMode)
                return;

            if (!(e.NewValue is bool newVal) || !newVal)
                return;

            var view = control as Control;
            if (view == null)
                return;

            var viewType = control.GetType();
            var viewFullName = viewType.FullName ?? string.Empty;
            var viewName = viewFullName.Replace(".Views.", ".ViewModels.");
            var viewAssemblyName = viewType.GetTypeInfo().Assembly.FullName ?? string.Empty;

            var windowType = typeof(StyledWindow);
            if (windowType.IsAssignableFrom(viewType))
            {
                var styled = view as StyledWindow;
                if (styled != null)
                    ThemeManager.Instance.EnableTheme(styled);
            }

            var viewModelName = string.Format(CultureInfo.InvariantCulture, "{0}Model, {1}", viewName, viewAssemblyName);
            var viewModelType = Type.GetType(viewModelName);
            if (viewModelType == null)
                return;

            var viewModel = ServiceLocator.Instance.Container?.GetService(viewModelType) as ViewModelBase;
            if (viewModel != null)
            {
                view.DataContext = viewModel;
                viewModel.IsLoaded = true;
            }
        }
    }
}
