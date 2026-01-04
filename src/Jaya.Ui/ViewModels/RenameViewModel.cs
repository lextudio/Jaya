using Jaya.Shared.Base;
using Jaya.Shared;
using System;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Jaya.Ui.ViewModels
{
    public class RenameViewModel : ViewModelBase
    {
        ICommand? _okCommand;
        ICommand? _cancelCommand;

        public string? OriginalName { get; set; }

        public string? Name
        {
            get => Get<string?>();
            set => Set(value);
        }

        public bool? DialogResult { get; private set; }

        public ICommand OkCommand
        {
            get
            {
                if (_okCommand == null)
                    _okCommand = new RelayCommand<object>(OnOk);
                return _okCommand;
            }
        }

        public ICommand CancelCommand
        {
            get
            {
                if (_cancelCommand == null)
                    _cancelCommand = new RelayCommand<object>(OnCancel);
                return _cancelCommand;
            }
        }

        void OnOk(object? _)
        {
            DialogResult = true;
        }

        void OnCancel(object? _)
        {
            DialogResult = false;
        }
    }
}
