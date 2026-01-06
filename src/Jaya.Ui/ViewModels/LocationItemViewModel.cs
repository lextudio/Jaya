using Jaya.Shared.Base;
using Jaya.Shared.Models;

namespace Jaya.Ui.ViewModels
{
    public enum LocationIconType
    {
        Folder,
        Download,
        Trash,
        Drive,
        Computer
    }

    public class LocationItemViewModel : ViewModelBase
    {
        DirectoryModel? _directory;
        string? _label;
        string? _imagePath;
        string? _imageResourceKey;
        LocationIconType _iconType = LocationIconType.Folder;
        ProviderServiceBase? _service;
        AccountModelBase? _account;

        public DirectoryModel? Directory
        {
            get => _directory;
            set => Set(ref _directory, value);
        }

        public string? Label
        {
            get => _label;
            set => Set(ref _label, value);
        }

        public string? ImagePath
        {
            get => _imagePath;
            set => Set(ref _imagePath, value);
        }

        public string? ImageResourceKey
        {
            get => _imageResourceKey;
            set => Set(ref _imageResourceKey, value);
        }

        public LocationIconType IconType
        {
            get => _iconType;
            set => Set(ref _iconType, value);
        }

        public ProviderServiceBase? Service
        {
            get => _service;
            set => Set(ref _service, value);
        }

        public AccountModelBase? Account
        {
            get => _account;
            set => Set(ref _account, value);
        }
    }
}
