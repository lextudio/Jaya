using Jaya.Shared.Models;
using System.Text.Json.Serialization;

namespace Jaya.Provider.S3.Models
{
    public class AccountModel: AccountModelBase
    {
        public AccountModel(string id, string name): base(id, name)
        {
            
        }

            [JsonPropertyName("email")]
        public string Email
        {
            get => Get<string>();
            set => Set(value);
        }
    }
}
