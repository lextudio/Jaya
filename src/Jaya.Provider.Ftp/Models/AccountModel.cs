//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Models;
using System.Text.Json.Serialization;

namespace Jaya.Provider.Ftp.Models
{
    public class AccountModel: AccountModelBase
    {
        public AccountModel(string id, string name): base(id, name)
        {
            
        }

        [JsonPropertyName("host")]
        public string Host
        {
            get => Get<string>();
            set
            {
                if (Set(value))
                    UpdateIdAndName();
            }
        }

        [JsonPropertyName("port")]
        public int Port
        {
            get => Get<int>();
            set
            {
                if (Set(value))
                    UpdateIdAndName();
            }
        }

        [JsonPropertyName("isAnonymous")]
        public bool IsAnonymous
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("userName")]
        public string UserName
        {
            get => Get<string>();
            set => Set(value);
        }

        [JsonPropertyName("password")]
        public string Password
        {
            get => Get<string>();
            set => Set(value);
        }

        void UpdateIdAndName()
        {
            Name = Id = string.Format("{0}:{1}", Host, Port);
        }

        public static AccountModel Empty()
        {
            var account = new AccountModel(string.Empty, string.Empty)
            {
                Port = 21
            };
            return account;
        }
    }
}
