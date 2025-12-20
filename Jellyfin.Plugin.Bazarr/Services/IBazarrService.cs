using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bazarr.Api.Models;

namespace Jellyfin.Plugin.Bazarr.Services;

/// <summary>
/// Interface for Bazarr API communication.
/// </summary>
public interface IBazarrService
{
    /// <summary>
    /// Gets all movies from Bazarr.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of movies.</returns>
    Task<IReadOnlyList<BazarrMovie>> GetMoviesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all series from Bazarr.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of series.</returns>
    Task<IReadOnlyList<BazarrSeries>> GetSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets episodes for a series from Bazarr.
    /// </summary>
    /// <param name="sonarrSeriesId">The Sonarr series ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of episodes.</returns>
    Task<IReadOnlyList<BazarrEpisode>> GetEpisodesAsync(int sonarrSeriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Radarr ID by TMDB ID.
    /// </summary>
    /// <param name="tmdbId">The TMDB ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Radarr ID if found, null otherwise.</returns>
    Task<int?> FindRadarrIdByTmdbAsync(int tmdbId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Radarr ID by IMDB ID.
    /// </summary>
    /// <param name="imdbId">The IMDB ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Radarr ID if found, null otherwise.</returns>
    Task<int?> FindRadarrIdByImdbAsync(string imdbId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Sonarr Episode ID by TVDB ID, season and episode number.
    /// </summary>
    /// <param name="tvdbId">The TVDB ID of the series.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Sonarr Episode ID if found, null otherwise.</returns>
    Task<int?> FindSonarrEpisodeIdAsync(int tvdbId, int season, int episode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Sonarr Episode ID by series IMDB ID, season and episode number.
    /// </summary>
    /// <param name="imdbId">The IMDB ID of the series.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Sonarr Episode ID if found, null otherwise.</returns>
    Task<int?> FindSonarrEpisodeIdByImdbAsync(string imdbId, int season, int episode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Sonarr Episode ID by series title, season and episode number.
    /// </summary>
    /// <param name="seriesTitle">The series title.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Sonarr Episode ID if found, null otherwise.</returns>
    Task<int?> FindSonarrEpisodeIdByTitleAsync(string seriesTitle, int season, int episode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for movie subtitles.
    /// </summary>
    /// <param name="radarrId">The Radarr ID.</param>
    /// <param name="language">The language code.</param>
    /// <param name="timeoutSeconds">Optional timeout in seconds. If exceeded, returns placeholder and continues in background.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search result with subtitles or in-progress indicator.</returns>
    Task<SubtitleSearchResult> SearchMovieSubtitlesAsync(int radarrId, string language, int timeoutSeconds = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for episode subtitles.
    /// </summary>
    /// <param name="sonarrEpisodeId">The Sonarr Episode ID.</param>
    /// <param name="sonarrSeriesId">The Sonarr Series ID (needed for download).</param>
    /// <param name="language">The language code.</param>
    /// <param name="timeoutSeconds">Optional timeout in seconds. If exceeded, returns placeholder and continues in background.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search result with subtitles or in-progress indicator.</returns>
    Task<SubtitleSearchResult> SearchEpisodeSubtitlesAsync(int sonarrEpisodeId, int sonarrSeriesId, string language, int timeoutSeconds = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a movie subtitle.
    /// </summary>
    /// <param name="request">The download request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> DownloadMovieSubtitleAsync(DownloadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an episode subtitle.
    /// </summary>
    /// <param name="request">The download request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> DownloadEpisodeSubtitleAsync(DownloadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available languages from Bazarr.
    /// </summary>
    /// <returns>List of languages.</returns>
    Task<IReadOnlyList<BazarrLanguage>> GetLanguagesAsync();

    /// <summary>
    /// Gets the Sonarr Series ID for an episode.
    /// </summary>
    /// <param name="sonarrEpisodeId">The Sonarr Episode ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Sonarr Series ID.</returns>
    Task<int> GetSeriesIdByEpisodeIdAsync(int sonarrEpisodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a movie by its Radarr ID.
    /// </summary>
    /// <param name="radarrId">The Radarr ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The movie, or null if not found.</returns>
    Task<BazarrMovie?> GetMovieByRadarrIdAsync(int radarrId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an episode by its Sonarr Episode ID.
    /// </summary>
    /// <param name="sonarrEpisodeId">The Sonarr Episode ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The episode, or null if not found.</returns>
    Task<BazarrEpisode?> GetEpisodeBySonarrIdAsync(int sonarrEpisodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to Bazarr.
    /// </summary>
    /// <returns>Connection test result.</returns>
    Task<ConnectionTestResult> TestConnectionAsync();
}
