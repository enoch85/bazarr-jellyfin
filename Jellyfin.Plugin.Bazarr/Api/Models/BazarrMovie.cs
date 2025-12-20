using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents a movie from Bazarr API.
/// </summary>
public class BazarrMovie
{
    /// <summary>
    /// Gets or sets the Radarr ID.
    /// </summary>
    [JsonPropertyName("radarrId")]
    public int RadarrId { get; set; }

    /// <summary>
    /// Gets or sets the movie title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB ID.
    /// </summary>
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the IMDB ID.
    /// </summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
