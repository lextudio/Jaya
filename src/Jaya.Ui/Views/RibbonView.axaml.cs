//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Jaya.Ui.Views
{
    public partial class RibbonView : UserControl
    {
        public static readonly StyledProperty<bool> IsRibbonExpandedProperty = AvaloniaProperty.Register<RibbonView, bool>(nameof(IsRibbonExpanded), true);

        public RibbonView()
        {
            this.InitializeComponent();
        }

        public bool IsRibbonExpanded
        {
            get => GetValue(IsRibbonExpandedProperty);
            set => SetValue(IsRibbonExpandedProperty, value);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
