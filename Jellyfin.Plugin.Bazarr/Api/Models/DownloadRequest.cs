using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents a subtitle download request (legacy, for backwards compatibility).
/// </summary>
public class DownloadRequest
{
    /// <summary>
    /// Gets or sets the Radarr ID (for movies).
    /// </summary>
    [JsonPropertyName("radarrid")]
    public int? RadarrId { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr Episode ID (for episodes).
    /// </summary>
    [JsonPropertyName("sonarrepisodeid")]
    public int? SonarrEpisodeId { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr Series ID (for episodes).
    /// </summary>
    [JsonPropertyName("seriesid")]
    public int? SonarrSeriesId { get; set; }

    /// <summary>
    /// Gets or sets the subtitle provider.
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pickled subtitle object (from search results).
    /// </summary>
    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to use hearing impaired subtitles.
    /// </summary>
    [JsonPropertyName("hi")]
    public string Hi { get; set; } = "False";

    /// <summary>
    /// Gets or sets whether to use forced subtitles.
    /// </summary>
    [JsonPropertyName("forced")]
    public string Forced { get; set; } = "False";

    /// <summary>
    /// Gets or sets whether to use original subtitle format.
    /// </summary>
    [JsonPropertyName("original_format")]
    public string OriginalFormat { get; set; } = "False";
}
