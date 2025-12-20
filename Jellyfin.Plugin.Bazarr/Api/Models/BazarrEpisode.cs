using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents an episode from Bazarr API.
/// </summary>
public class BazarrEpisode
{
    /// <summary>
    /// Gets or sets the Sonarr Episode ID.
    /// </summary>
    [JsonPropertyName("sonarrEpisodeId")]
    public int SonarrEpisodeId { get; set; }

    /// <summary>
    /// Gets or sets the Sonarr Series ID.
    /// </summary>
    [JsonPropertyName("sonarrSeriesId")]
    public int SonarrSeriesId { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    [JsonPropertyName("episode")]
    public int Episode { get; set; }

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
