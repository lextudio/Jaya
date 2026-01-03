//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace Jaya.Ui.Views
{
    public partial class MenuView : UserControl
    {
        public MenuView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _rootMenu = this.FindControl<Menu>("RootMenu");
        }

        Menu _rootMenu;

        public Menu MenuControl => _rootMenu;
    }
}
