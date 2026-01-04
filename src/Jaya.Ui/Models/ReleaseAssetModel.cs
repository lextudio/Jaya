//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using System.Text.Json.Serialization;

namespace Jaya.Ui.Models
{
    public class ReleaseAssetModel
    {
        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? Url { get; set; }

        public override string ToString()
        {
            return Url ?? string.Empty;
        }
    }
}
