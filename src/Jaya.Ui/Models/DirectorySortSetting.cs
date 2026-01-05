using System.Text.Json.Serialization;

namespace Jaya.Ui.Models
{
    public sealed class DirectorySortSetting
    {
        [JsonPropertyName("sortMember")]
        public string SortMember { get; set; } = string.Empty;

        [JsonPropertyName("ascending")]
        public bool Ascending { get; set; }
    }
}
