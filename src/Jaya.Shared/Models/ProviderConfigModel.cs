using Jaya.Shared.Base;

namespace Jaya.Shared.Models
{
    public class ProviderConfigModel : ConfigModelBase
    {
        // Plugins disabled by default for security.
        public bool IsEnabled { get; set; } = false;

        protected override ConfigModelBase Empty()
        {
            return new ProviderConfigModel { IsEnabled = false };
        }
    }
}
