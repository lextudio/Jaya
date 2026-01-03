using Jaya.Shared.Base;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jaya.Provider.S3.Models
{
    public class ConfigModel : ConfigModelBase
    {
        public ConfigModel()
        {
            Accounts = new List<AccountModel>();
        }

        [JsonConstructor]
        public ConfigModel(IEnumerable<AccountModel> accounts): this()
        {
            if (accounts != null)
                Accounts = new List<AccountModel>(accounts);
        }

        [JsonPropertyName("pageSize")]
        public int PageSize
        {
            get => Get<int>();
            set => Set(value);
        }
        [JsonPropertyName("accounts")]
        public IList<AccountModel> Accounts { get; private set; }

        protected override ConfigModelBase Empty()
        {
            return new ConfigModel(null)
            {
                PageSize = 1000
            };
        }
    }
}
