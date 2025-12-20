using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents a series from Bazarr API.
/// </summary>
public class BazarrSeries
{
    /// <summary>
    /// Gets or sets the Sonarr Series ID.
    /// </summary>
    [JsonPropertyName("sonarrSeriesId")]
    public int SonarrSeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TVDB ID.
    /// </summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>
    /// Gets or sets the IMDB ID.
    /// </summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the folder path.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
