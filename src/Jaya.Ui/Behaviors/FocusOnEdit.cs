using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using System;

namespace Jaya.Ui.Behaviors
{
    public class FocusOnEdit
    {
        public static readonly Avalonia.StyledProperty<bool> IsEnabledProperty =
            Avalonia.AvaloniaProperty.RegisterAttached<FocusOnEdit, Control, bool>("IsEnabled");

        public static void SetIsEnabled(Control element, bool value)
        {
            element.SetValue(IsEnabledProperty, value);
            if (value)
            {
                element.PropertyChanged += Element_PropertyChanged;
            }
            else
            {
                element.PropertyChanged -= Element_PropertyChanged;
            }
        }

        static void Element_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is Control control && e.Property.Name == "IsVisible")
            {
                if (control.IsVisible)
                {
                    Dispatcher.UIThread.Post(() => control.Focus(), DispatcherPriority.Background);
                }
            }
        }
    }
}
