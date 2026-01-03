//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using System.Text.Json.Serialization;
using System;

namespace Jaya.Ui.Models
{
    public class ReleaseModel
    {
        [JsonPropertyName("name")]
        public Version Version { get; set; }

        [JsonPropertyName("prerelease")]
        public bool IsPrerelease { get; set; }

        [JsonPropertyName("draft")]
        public bool IsDraft { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime Date { get; set; }

        [JsonPropertyName("body")]
        public string Notes { get; set; }

        [JsonPropertyName("assets")]
        public ReleaseAssetModel[] Downloads { get; set; }

        public override string ToString()
        {
            if (Version == null)
                return null;

            return string.Format("{0}.{1}.{2}.{3}", Version.Major, Version.Minor, Version.Build, Version.Revision);
        }
    }
}
