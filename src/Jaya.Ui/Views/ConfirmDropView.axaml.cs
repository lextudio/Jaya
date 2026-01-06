using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Jaya.Ui.Views
{
    public partial class ConfirmDropView : Window
    {
        public ConfirmDropView()
        {
            InitializeComponent();
            OkButton.Click += OkButton_Click;
            CancelButton.Click += CancelButton_Click;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public string Message
        {
            get => MessageText.Text ?? string.Empty;
            set => MessageText.Text = value ?? string.Empty;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            this.Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            this.Close(false);
        }
    }
}
