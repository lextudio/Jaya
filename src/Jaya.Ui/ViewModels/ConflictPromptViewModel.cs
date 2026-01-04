using Jaya.Shared.Base;
using Jaya.Shared;
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public enum ConflictResult
    {
        Overwrite,
        KeepBoth,
        Cancel
    }

    public class ConflictPromptViewModel : ViewModelBase
    {
        ICommand? _overwriteCommand;
        ICommand? _keepBothCommand;
        ICommand? _cancelCommand;

        public ConflictResult? Result { get; private set; }

        public bool ApplyToAll
        {
            get => Get<bool>();
            set => Set(value);
        }

        public ICommand OverwriteCommand => _overwriteCommand ??= new RelayCommand<object>(_ => { Result = ConflictResult.Overwrite; });
        public ICommand KeepBothCommand => _keepBothCommand ??= new RelayCommand<object>(_ => { Result = ConflictResult.KeepBoth; });
        public ICommand CancelCommand => _cancelCommand ??= new RelayCommand<object>(_ => { Result = ConflictResult.Cancel; });
    }
}
