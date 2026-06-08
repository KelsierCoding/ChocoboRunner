using System.Text.Json.Serialization;

namespace ChocoboRunner.Models.GithubModels;

public class ReleaseModel
{
    [JsonPropertyName("assets")] public List<Asset> Assets { get; init; } = [];
    
    public class Asset
    {
        [JsonPropertyName("digest")]
        public string Digest { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }  = string.Empty;
    }
}