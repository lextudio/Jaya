//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Base;
using System.Text.Json.Serialization;
using System;
using System.IO;

namespace Jaya.Ui.Models
{
    public class UpdateConfigModel : ConfigModelBase
    {
        [JsonPropertyName("checked")]
        public DateTime Checked
        {
            get => Get<DateTime>();
            set => Set(value);
        }

        [JsonPropertyName("update")]
        public ReleaseModel Update
        {
            get => Get<ReleaseModel>();
            set => Set(value);
        }

        [JsonPropertyName("downloadDirectory")]
        public string DownloadDirectory
        {
            get => Get<string>();
            set => Set(value);
        }

        protected override ConfigModelBase Empty()
        {
            return new UpdateConfigModel
            {
                Checked = DateTime.MinValue,
                DownloadDirectory = Path.Combine(Constants.DATA_DIRECTORY, "Download")
            };
        }
    }
}
