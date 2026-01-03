//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using System;
using System.Windows.Input;

namespace Jaya.Shared.Controls
{
    /// <summary>
    /// Refer https://www.powerworld.com/WebHelp/Content/MainDocumentation_HTML/Ribbons.htm for details.
    /// </summary>
    public class Ribbon : TabControl
    {
        public static readonly DirectProperty<Ribbon, bool> IsExpandedProperty;
        public static readonly DirectProperty<Ribbon, ICommand> HelpButtonCommandProperty;

        Button _toggleButton;
        ICommand _helpCommand;
        bool _isExpanded;
        ISelectable _selectedTab;

        static Ribbon()
        {
            IsExpandedProperty = AvaloniaProperty.RegisterDirect<Ribbon, bool>(nameof(IsExpanded), o => o.IsExpanded, (o, v) => o.IsExpanded = v, true);
            HelpButtonCommandProperty = AvaloniaProperty.RegisterDirect<Ribbon, ICommand>(nameof(HelpButtonCommand), o => o.HelpButtonCommand, (o, v) => o.HelpButtonCommand = v);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            internal set => SetAndRaise(IsExpandedProperty, ref _isExpanded, value);
        }

        public ICommand HelpButtonCommand
        {
            get => _helpCommand;
            set => SetAndRaise(HelpButtonCommandProperty, ref _helpCommand, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == SelectedItemProperty)
            {
                object newValue = e.NewValue;
                if (newValue == null || IsExpanded)
                {
                    // no-op
                }
                else
                {
                    IsExpanded = true;
                }
            }

            base.OnPropertyChanged(e);
        }

        protected override Type StyleKeyOverride => typeof(Ribbon);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _toggleButton = this.FindControl<Button>("PART_ToggleButton");
            _toggleButton.Click += delegate
            {
                IsExpanded = !IsExpanded;

                if (IsExpanded)
                {
                    if (_selectedTab != null)
                    {
                        _selectedTab.IsSelected = true;
                        _selectedTab = null;
                    }
                }
                else
                {
                    if (SelectedItem != null)
                    {
                        _selectedTab = SelectedItem as ISelectable;
                        _selectedTab.IsSelected = false;
                    }
                }
            };
        }
    }
}
