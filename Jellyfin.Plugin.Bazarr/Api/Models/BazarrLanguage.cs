using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents a language from Bazarr API.
/// </summary>
public class BazarrLanguage
{
    /// <summary>
    /// Gets or sets the two-letter language code.
    /// </summary>
    [JsonPropertyName("code2")]
    public string Code2 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the three-letter language code.
    /// </summary>
    [JsonPropertyName("code3")]
    public string Code3 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the language name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the language is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}
