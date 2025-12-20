using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents a subtitle search result from Bazarr API.
/// </summary>
[SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "DTO for JSON deserialization")]
[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "DTO for JSON deserialization")]
public class SubtitleOption
{
    /// <summary>
    /// Gets or sets the subtitle provider.
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pickled subtitle object (used to download the subtitle).
    /// </summary>
    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release info.
    /// </summary>
    [JsonPropertyName("release_info")]
    public List<string>? ReleaseInfo { get; set; }

    /// <summary>
    /// Gets or sets the match score.
    /// </summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets the original score.
    /// </summary>
    [JsonPropertyName("orig_score")]
    public int OrigScore { get; set; }

    /// <summary>
    /// Gets or sets the uploader name.
    /// </summary>
    [JsonPropertyName("uploader")]
    public string? Uploader { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is for hearing impaired.
    /// </summary>
    [JsonPropertyName("hearing_impaired")]
    public string? HearingImpaired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is forced.
    /// </summary>
    [JsonPropertyName("forced")]
    public string? Forced { get; set; }

    /// <summary>
    /// Gets or sets the language code.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the subtitle matches (e.g., "hash", "title").
    /// </summary>
    [JsonPropertyName("matches")]
    public List<string>? Matches { get; set; }

    /// <summary>
    /// Gets or sets what the subtitle doesn't match.
    /// </summary>
    [JsonPropertyName("dont_matches")]
    public List<string>? DontMatches { get; set; }

    /// <summary>
    /// Gets or sets the URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the original format.
    /// </summary>
    [JsonPropertyName("original_format")]
    public string? OriginalFormat { get; set; }

    /// <summary>
    /// Gets the release string for display.
    /// </summary>
    [JsonIgnore]
    public string Release => ReleaseInfo != null && ReleaseInfo.Count > 0
        ? string.Join(", ", ReleaseInfo)
        : Provider;
}
