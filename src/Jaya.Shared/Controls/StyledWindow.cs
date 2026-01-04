//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.IO;

namespace Jaya.Shared.Controls
{
    public class StyledWindow : Window
    {
        const string DEFAULT_ICON = "avares://Jaya.Shared/Assets/Logo.ico";

        public static readonly StyledProperty<object> HeaderContentProperty;
        public static readonly StyledProperty<bool> IsModalProperty;
        Button? _closeButton, _minimizeButton, _maximizeButton;
        bool _isTemplateApplied;

        static StyledWindow()
        {
            HeaderContentProperty = AvaloniaProperty.Register<StyledWindow, object>(nameof(HeaderContent));
            IsModalProperty = AvaloniaProperty.Register<StyledWindow, bool>(nameof(IsModal));
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            SetupSide("TopLeft", StandardCursorType.TopLeftCorner, WindowEdge.NorthWest);
            SetupSide("TopCenter", StandardCursorType.TopSide, WindowEdge.North);
            SetupSide("TopRight", StandardCursorType.TopRightCorner, WindowEdge.NorthEast);
            SetupSide("MiddleRight", StandardCursorType.RightSide, WindowEdge.East);
            SetupSide("BottomRight", StandardCursorType.BottomRightCorner, WindowEdge.SouthEast);
            SetupSide("BottomCenter", StandardCursorType.BottomSide, WindowEdge.South);
            SetupSide("BottomLeft", StandardCursorType.BottomLeftCorner, WindowEdge.SouthWest);
            SetupSide("MiddleLeft", StandardCursorType.LeftSide, WindowEdge.West);

            var titlebar = this.FindControl<Border>("PART_TitleBar");
            if (titlebar != null)
            {
                titlebar.PointerPressed += (sender, args) =>
                {
                    if (args.ClickCount == 1)
                        BeginMoveDrag(args);
                };
                titlebar.DoubleTapped += (sender, args) =>
                {
                    if (CanResize && (!IsModal))
                        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                };
            }

            _closeButton = this.FindControl<Button>("PART_Close");
            if (_closeButton != null)
                _closeButton.Click += (sender, arg) => Close();

            var isNotModal = !IsModal;

            _minimizeButton = this.FindControl<Button>("PART_Minimize");
            if (_minimizeButton != null)
            {
                _minimizeButton.IsVisible = isNotModal;
                _minimizeButton.Click += (sender, args) => WindowState = WindowState.Minimized;
            }

            _maximizeButton = this.FindControl<Button>("PART_Maximize");
            if (_maximizeButton != null)
            {
                _maximizeButton.IsVisible = isNotModal;
                _maximizeButton.Click += (sender, args) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }

            _isTemplateApplied = true;
        }

        public object HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        public bool IsModal
        {
            get => GetValue(IsModalProperty);
            set
            {
                SetValue(IsModalProperty, value);

                var inverseValue = !value;

                ShowInTaskbar = inverseValue;
                WindowStartupLocation = inverseValue ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;

                if (!_isTemplateApplied)
                    return;

                if (_minimizeButton != null)
                    _minimizeButton.IsVisible = inverseValue;
                if (_maximizeButton != null)
                    _maximizeButton.IsVisible = inverseValue;
            }
        }

        protected override Type StyleKeyOverride => typeof(StyledWindow);

        void SetupSide(string name, StandardCursorType cursor, WindowEdge edge)
        {
            var control = this.FindControl<Control>("PART_" + name + "Edge");
            if (control == null)
                return;

            control.Cursor = new Cursor(cursor);
            control.PointerPressed += (sender, ep) => BeginResizeDrag(edge, ep);
        }
    }
}
