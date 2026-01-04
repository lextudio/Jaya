using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Jaya.Ui.ViewModels;
using Jaya.Shared.Controls;

namespace Jaya.Ui.Views
{
    public partial class ConflictPromptView : StyledWindow
    {
        public ConflictPromptView()
        {
            InitializeComponent();
        }

        public ConflictPromptViewModel? ViewModel => DataContext as ConflictPromptViewModel;

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
