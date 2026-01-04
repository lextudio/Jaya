using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Jaya.Ui.ViewModels;
using Jaya.Shared.Controls;
using System;

namespace Jaya.Ui.Views
{
    public partial class RenameView : StyledWindow
    {
        public RenameView()
        {
            InitializeComponent();
        }

        public RenameViewModel? ViewModel => DataContext as RenameViewModel;

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
