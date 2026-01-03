//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Reflection;
using System.Windows.Input;

namespace Jaya.Ui.Views
{
    public partial class AddressbarView : UserControl
    {
        public AddressbarView()
        {
            this.InitializeComponent();
            this.AttachedToVisualTree += AddressbarView_AttachedToVisualTree;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void AddressbarView_AttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            // Try to wire breadcrumb events to view model commands if present
            if (DataContext is null)
                return;

            var vm = DataContext;
            var breadcrumb = this.FindControl<Control>("Breadcrumb");
            if (breadcrumb is null)
                return;

            // Wire selection changed if control exposes SelectionChanged event
            try
            {
                // Try to observe SelectedItem property change using reflection
                var selectedProp = breadcrumb.GetType().GetProperty("SelectedItem", BindingFlags.Public | BindingFlags.Instance);
                if (selectedProp is not null)
                {
                    // Subscribe to Avalonia's property change via Observable pattern if available
                    // Fallback: poll on Loaded is not ideal, but try to hook to a "SelectedItemChanged" event first
                    var eventInfo = breadcrumb.GetType().GetEvent("SelectedItemChanged") ?? breadcrumb.GetType().GetEvent("SelectionChanged");
                    if (eventInfo is not null)
                    {
                        var handlerMethod = this.GetType().GetMethod(nameof(OnBreadcrumbSelectionChanged), BindingFlags.NonPublic | BindingFlags.Instance);
                        if (handlerMethod is not null)
                        {
                            var handlerDelegate = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, handlerMethod);
                            eventInfo.AddEventHandler(breadcrumb, handlerDelegate);
                        }
                    }
                }
            }
            catch { }
        }

        private void OnBreadcrumbSelectionChanged(object sender, EventArgs e)
        {
            try
            {
                var breadcrumb = this.FindControl<Control>("Breadcrumb");
                var selectedProp = breadcrumb?.GetType().GetProperty("SelectedItem", BindingFlags.Public | BindingFlags.Instance);
                var selected = selectedProp?.GetValue(breadcrumb);
                var vmObj = DataContext;
                if (vmObj is not null)
                {
                    var vmType = vmObj.GetType();
                    var onBreadcrumbProp = vmType.GetProperty("OnBreadcrumbSelected");
                    if (onBreadcrumbProp?.GetValue(vmObj) is Action<object> onBreadcrumb)
                    {
                        onBreadcrumb(selected);
                    }
                    else
                    {
                        var cmdProp = vmType.GetProperty("NavigateToPathCommand")?.GetValue(vmObj) as ICommand;
                        if (cmdProp is not null && cmdProp.CanExecute(selected))
                            cmdProp.Execute(selected);
                    }
                }
            }
            catch { }
        }
    }
}
