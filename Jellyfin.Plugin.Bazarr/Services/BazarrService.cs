using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bazarr.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bazarr.Services;

/// <summary>
/// Service for communicating with Bazarr API.
/// </summary>
public class BazarrService : IBazarrService
{
    private const string MoviesCacheKey = "bazarr_movies";
    private const string SeriesCacheKey = "bazarr_series";
    private const string EpisodesCacheKeyPrefix = "bazarr_episodes_";
    private const string MovieSearchCacheKeyPrefix = "bazarr_movie_search_";
    private const string EpisodeSearchCacheKeyPrefix = "bazarr_episode_search_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SearchResultCacheDuration = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BazarrService> _logger;
    private readonly IBazarrConfigProvider _configProvider;

    // Track in-flight search requests to avoid duplicate Bazarr API calls
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<SubtitleOption>>> _inFlightSearches = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BazarrService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configProvider">The configuration provider.</param>
    public BazarrService(HttpClient httpClient, IMemoryCache cache, ILogger<BazarrService> logger, IBazarrConfigProvider configProvider)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _configProvider = configProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BazarrMovie>> GetMoviesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(MoviesCacheKey, out IReadOnlyList<BazarrMovie>? cached) && cached != null)
        {
            _logger.LogDebug("Returning cached movies list");
            return cached;
        }

        _logger.LogInformation("Fetching movies from Bazarr");
        var request = CreateRequest(HttpMethod.Get, "/api/movies");
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ValidateResponseAsync(response, "/api/movies").ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<BazarrResponse<BazarrMovie>>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var movies = result?.Data ?? (IReadOnlyList<BazarrMovie>)new List<BazarrMovie>();

        _cache.Set(MoviesCacheKey, movies, CacheDuration);
        _logger.LogInformation("Cached {Count} movies from Bazarr", movies.Count);

        return movies;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BazarrSeries>> GetSeriesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(SeriesCacheKey, out IReadOnlyList<BazarrSeries>? cached) && cached != null)
        {
            _logger.LogDebug("Returning cached series list");
            return cached;
        }

        _logger.LogInformation("Fetching series from Bazarr");
        var request = CreateRequest(HttpMethod.Get, "/api/series");
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ValidateResponseAsync(response, "/api/series").ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<BazarrResponse<BazarrSeries>>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var series = result?.Data ?? (IReadOnlyList<BazarrSeries>)new List<BazarrSeries>();

        _cache.Set(SeriesCacheKey, series, CacheDuration);
        _logger.LogInformation("Cached {Count} series from Bazarr", series.Count);

        return series;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BazarrEpisode>> GetEpisodesAsync(int sonarrSeriesId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{EpisodesCacheKeyPrefix}{sonarrSeriesId}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<BazarrEpisode>? cached) && cached != null)
        {
            _logger.LogDebug("Returning cached episodes for series {SeriesId}", sonarrSeriesId);
            return cached;
        }

        _logger.LogInformation("Fetching episodes for series {SeriesId} from Bazarr", sonarrSeriesId);
        var request = CreateRequest(HttpMethod.Get, $"/api/episodes?seriesid[]={sonarrSeriesId}");
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ValidateResponseAsync(response, $"/api/episodes?seriesid[]={sonarrSeriesId}").ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<BazarrResponse<BazarrEpisode>>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var episodes = result?.Data ?? (IReadOnlyList<BazarrEpisode>)new List<BazarrEpisode>();

        _cache.Set(cacheKey, episodes, CacheDuration);
        _logger.LogInformation("Cached {Count} episodes for series {SeriesId}", episodes.Count, sonarrSeriesId);

        return episodes;
    }

    /// <inheritdoc />
    public async Task<int?> FindRadarrIdByTmdbAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var movies = await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        var movie = movies.FirstOrDefault(m => m.TmdbId == tmdbId);

        if (movie != null)
        {
            _logger.LogDebug("Found Radarr ID {RadarrId} for TMDB ID {TmdbId}", movie.RadarrId, tmdbId);
        }
        else
        {
            _logger.LogDebug("No movie found in Bazarr for TMDB ID {TmdbId}", tmdbId);
        }

        return movie?.RadarrId;
    }

    /// <inheritdoc />
    public async Task<int?> FindRadarrIdByImdbAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        var movies = await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        var movie = movies.FirstOrDefault(m => string.Equals(m.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));

        if (movie != null)
        {
            _logger.LogDebug("Found Radarr ID {RadarrId} for IMDB ID {ImdbId}", movie.RadarrId, imdbId);
        }
        else
        {
            _logger.LogDebug("No movie found in Bazarr for IMDB ID {ImdbId}", imdbId);
        }

        return movie?.RadarrId;
    }

    /// <inheritdoc />
    public async Task<int?> FindSonarrEpisodeIdAsync(int tvdbId, int season, int episode, CancellationToken cancellationToken = default)
    {
        var series = await GetSeriesAsync(cancellationToken).ConfigureAwait(false);
        var seriesList = series.ToList();

        _logger.LogDebug(
            "Looking for series with TVDB ID {TvdbId} among {Count} series. Available TVDB IDs: {TvdbIds}",
            tvdbId,
            seriesList.Count,
            string.Join(", ", seriesList.Select(s => $"{s.Title}={s.TvdbId}")));

        var show = seriesList.FirstOrDefault(s => s.TvdbId == tvdbId);

        if (show == null)
        {
            _logger.LogWarning("No series found in Bazarr for TVDB ID {TvdbId}", tvdbId);
            return null;
        }

        _logger.LogDebug("Found series {Title} (SonarrSeriesId={SonarrSeriesId}) for TVDB ID {TvdbId}", show.Title, show.SonarrSeriesId, tvdbId);

        var episodes = await GetEpisodesAsync(show.SonarrSeriesId, cancellationToken).ConfigureAwait(false);
        var episodeList = episodes.ToList();

        _logger.LogDebug(
            "Found {Count} episodes for series {Title}. Looking for S{Season}E{Episode}. Available: {Episodes}",
            episodeList.Count,
            show.Title,
            season,
            episode,
            string.Join(", ", episodeList.Select(e => $"S{e.Season}E{e.Episode}={e.SonarrEpisodeId}")));

        var ep = episodeList.FirstOrDefault(e => e.Season == season && e.Episode == episode);

        if (ep != null)
        {
            _logger.LogDebug("Found Sonarr Episode ID {EpisodeId} for S{Season}E{Episode}", ep.SonarrEpisodeId, season, episode);
        }
        else
        {
            _logger.LogWarning("No episode found in Bazarr for {Title} S{Season}E{Episode}", show.Title, season, episode);
        }

        return ep?.SonarrEpisodeId;
    }

    /// <inheritdoc />
    public async Task<int?> FindSonarrEpisodeIdByImdbAsync(string imdbId, int season, int episode, CancellationToken cancellationToken = default)
    {
        var series = await GetSeriesAsync(cancellationToken).ConfigureAwait(false);
        var show = series.FirstOrDefault(s => string.Equals(s.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));

        if (show == null)
        {
            _logger.LogDebug("No series found in Bazarr for IMDB ID {ImdbId}", imdbId);
            return null;
        }

        _logger.LogDebug(
            "Found series '{Title}' (SonarrSeriesId={SonarrSeriesId}) for IMDB ID {ImdbId}",
            show.Title,
            show.SonarrSeriesId,
            imdbId);

        var episodes = await GetEpisodesAsync(show.SonarrSeriesId, cancellationToken).ConfigureAwait(false);
        var ep = episodes.FirstOrDefault(e => e.Season == season && e.Episode == episode);

        if (ep != null)
        {
            _logger.LogDebug("Found Sonarr Episode ID {EpisodeId} for S{Season}E{Episode}", ep.SonarrEpisodeId, season, episode);
        }
        else
        {
            _logger.LogWarning("No episode found in Bazarr for {Title} S{Season}E{Episode}", show.Title, season, episode);
        }

        return ep?.SonarrEpisodeId;
    }

    /// <inheritdoc />
    public async Task<int?> FindSonarrEpisodeIdByTitleAsync(string seriesTitle, int season, int episode, CancellationToken cancellationToken = default)
    {
        var series = await GetSeriesAsync(cancellationToken).ConfigureAwait(false);
        var seriesList = series.ToList();

        // Try exact match first
        var show = seriesList.FirstOrDefault(s =>
            string.Equals(s.Title, seriesTitle, StringComparison.OrdinalIgnoreCase));

        // If no exact match, try contains (for cases like "Landman" vs "Landman (2024)")
        if (show == null)
        {
            show = seriesList.FirstOrDefault(s =>
                s.Title.Contains(seriesTitle, StringComparison.OrdinalIgnoreCase) ||
                seriesTitle.Contains(s.Title, StringComparison.OrdinalIgnoreCase));
        }

        if (show == null)
        {
            _logger.LogWarning(
                "No series found in Bazarr matching title '{Title}'. Available series: {Series}",
                seriesTitle,
                string.Join(", ", seriesList.Select(s => s.Title)));
            return null;
        }

        _logger.LogInformation(
            "Found series '{BazarrTitle}' (SonarrSeriesId={SonarrSeriesId}) matching '{RequestedTitle}'",
            show.Title,
            show.SonarrSeriesId,
            seriesTitle);

        var episodes = await GetEpisodesAsync(show.SonarrSeriesId, cancellationToken).ConfigureAwait(false);
        var episodeList = episodes.ToList();

        var ep = episodeList.FirstOrDefault(e => e.Season == season && e.Episode == episode);

        if (ep != null)
        {
            _logger.LogDebug("Found Sonarr Episode ID {EpisodeId} for S{Season}E{Episode}", ep.SonarrEpisodeId, season, episode);
        }
        else
        {
            _logger.LogWarning("No episode found in Bazarr for {Title} S{Season}E{Episode}", show.Title, season, episode);
        }

        return ep?.SonarrEpisodeId;
    }

    /// <inheritdoc />
    public async Task<int> GetSeriesIdByEpisodeIdAsync(int sonarrEpisodeId, CancellationToken cancellationToken = default)
    {
        // We need to find which series this episode belongs to
        var series = await GetSeriesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var show in series)
        {
            var episodes = await GetEpisodesAsync(show.SonarrSeriesId, cancellationToken).ConfigureAwait(false);
            if (episodes.Any(e => e.SonarrEpisodeId == sonarrEpisodeId))
            {
                _logger.LogDebug(
                    "Found Series ID {SeriesId} for Episode ID {EpisodeId}",
                    show.SonarrSeriesId,
                    sonarrEpisodeId);
                return show.SonarrSeriesId;
            }
        }

        _logger.LogWarning("Could not find series for episode ID {EpisodeId}", sonarrEpisodeId);
        throw new InvalidOperationException($"Could not find series for episode ID {sonarrEpisodeId}");
    }

    /// <inheritdoc />
    public async Task<SubtitleSearchResult> SearchMovieSubtitlesAsync(int radarrId, string language, int timeoutSeconds = 0, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{MovieSearchCacheKeyPrefix}{radarrId}";

        // Check if we have cached results
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubtitleOption>? cached) && cached != null)
        {
            _logger.LogInformation("Returning cached subtitle search results for movie {RadarrId} ({Count} subtitles)", radarrId, cached.Count);
            return new SubtitleSearchResult { Subtitles = cached, FromCache = true };
        }

        // Use GetOrAdd to atomically check if there's an in-flight search or create a new one
        var searchKey = $"movie_{radarrId}";
        var isNewSearch = false;

        var searchTask = _inFlightSearches.GetOrAdd(searchKey, _ =>
        {
            isNewSearch = true;
            // Use CancellationToken.None so the search continues even if the HTTP request is cancelled
            return SearchMovieSubtitlesInternalAsync(radarrId, language, CancellationToken.None);
        });

        if (!isNewSearch)
        {
            _logger.LogInformation("Reusing in-flight search for movie {RadarrId}", radarrId);
        }

        try
        {
            // If timeout is enabled and this is a new search, use Task.WhenAny to return early
            if (timeoutSeconds > 0 && isNewSearch)
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
                var completedTask = await Task.WhenAny(searchTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    // Timeout reached - return placeholder, search continues in background
                    _logger.LogInformation(
                        "Search timeout ({Timeout}s) reached for movie {RadarrId}. Search continues in background.",
                        timeoutSeconds,
                        radarrId);

                    // Continue search in background
                    _ = ContinueSearchInBackgroundAsync(searchTask, cacheKey, searchKey, $"movie {radarrId}");

                    return new SubtitleSearchResult
                    {
                        Subtitles = [],
                        SearchInProgress = true
                    };
                }
            }

            var result = await searchTask.ConfigureAwait(false);

            // Only cache if this was our search (avoid double-caching)
            if (isNewSearch)
            {
                _cache.Set(cacheKey, result, SearchResultCacheDuration);
                _inFlightSearches.TryRemove(searchKey, out _);
            }

            return new SubtitleSearchResult { Subtitles = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching subtitles for movie {RadarrId}", radarrId);
            if (isNewSearch)
            {
                _inFlightSearches.TryRemove(searchKey, out _);
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<SubtitleOption>> SearchMovieSubtitlesInternalAsync(int radarrId, string language, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching subtitles for movie {RadarrId} in language {Language}. This may take a while...", radarrId, language);

        // Use GET /api/providers/movies?radarrid=<id> to search for subtitles
        // The API returns data wrapped in {"data": [...]}
        // NOTE: This is a slow operation as Bazarr queries multiple subtitle providers in real-time
        var request = CreateRequest(HttpMethod.Get, $"/api/providers/movies?radarrid={radarrId}");

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ValidateResponseAsync(response, $"/api/providers/movies?radarrid={radarrId}").ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<BazarrResponse<SubtitleOption>>(cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Found {Count} subtitles for movie {RadarrId}", result?.Data?.Count ?? 0, radarrId);
        return result?.Data ?? [];
    }

    /// <inheritdoc />
    public async Task<SubtitleSearchResult> SearchEpisodeSubtitlesAsync(int sonarrEpisodeId, int sonarrSeriesId, string language, int timeoutSeconds = 0, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{EpisodeSearchCacheKeyPrefix}{sonarrEpisodeId}";

        // Check if we have cached results
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubtitleOption>? cached) && cached != null)
        {
            _logger.LogInformation("Returning cached subtitle search results for episode {EpisodeId} ({Count} subtitles)", sonarrEpisodeId, cached.Count);
            return new SubtitleSearchResult { Subtitles = cached, FromCache = true };
        }

        // Use GetOrAdd to atomically check if there's an in-flight search or create a new one
        var searchKey = $"episode_{sonarrEpisodeId}";
        var isNewSearch = false;

        var searchTask = _inFlightSearches.GetOrAdd(searchKey, _ =>
        {
            isNewSearch = true;
            // Use CancellationToken.None so the search continues even if the HTTP request is cancelled
            return SearchEpisodeSubtitlesInternalAsync(sonarrEpisodeId, language, CancellationToken.None);
        });

        if (!isNewSearch)
        {
            _logger.LogInformation("Reusing in-flight search for episode {EpisodeId}", sonarrEpisodeId);
        }

        try
        {
            // If timeout is enabled and this is a new search, use Task.WhenAny to return early
            if (timeoutSeconds > 0 && isNewSearch)
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
                var completedTask = await Task.WhenAny(searchTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    // Timeout reached - return placeholder, search continues in background
                    _logger.LogInformation(
                        "Search timeout ({Timeout}s) reached for episode {EpisodeId}. Search continues in background.",
                        timeoutSeconds,
                        sonarrEpisodeId);

                    // Continue search in background
                    _ = ContinueSearchInBackgroundAsync(searchTask, cacheKey, searchKey, $"episode {sonarrEpisodeId}");

                    return new SubtitleSearchResult
                    {
                        Subtitles = [],
                        SearchInProgress = true
                    };
                }
            }

            var result = await searchTask.ConfigureAwait(false);

            // Only cache if this was our search (avoid double-caching)
            if (isNewSearch)
            {
                _cache.Set(cacheKey, result, SearchResultCacheDuration);
                _inFlightSearches.TryRemove(searchKey, out _);
            }

            return new SubtitleSearchResult { Subtitles = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching subtitles for episode {EpisodeId}", sonarrEpisodeId);
            if (isNewSearch)
            {
                _inFlightSearches.TryRemove(searchKey, out _);
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<SubtitleOption>> SearchEpisodeSubtitlesInternalAsync(int sonarrEpisodeId, string language, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching subtitles for episode {EpisodeId} in language {Language}. This may take a while...", sonarrEpisodeId, language);

        // Use GET /api/providers/episodes?episodeid=<id> to search for subtitles
        // The API returns data wrapped in {"data": [...]}
        // NOTE: This is a slow operation as Bazarr queries multiple subtitle providers in real-time
        var request = CreateRequest(HttpMethod.Get, $"/api/providers/episodes?episodeid={sonarrEpisodeId}");

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ValidateResponseAsync(response, $"/api/providers/episodes?episodeid={sonarrEpisodeId}").ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<BazarrResponse<SubtitleOption>>(cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Found {Count} subtitles for episode {EpisodeId}", result?.Data?.Count ?? 0, sonarrEpisodeId);
        return result?.Data ?? [];
    }

    /// <summary>
    /// Continues a search in the background after timeout, caching results when done.
    /// </summary>
    private async Task ContinueSearchInBackgroundAsync(
        Task<IReadOnlyList<SubtitleOption>> searchTask,
        string cacheKey,
        string searchKey,
        string itemDescription)
    {
        try
        {
            var result = await searchTask.ConfigureAwait(false);
            _cache.Set(cacheKey, result, SearchResultCacheDuration);
            _logger.LogInformation(
                "Background search completed for {Item}. Found {Count} subtitles. Results cached for {Duration} minutes.",
                itemDescription,
                result.Count,
                SearchResultCacheDuration.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background search failed for {Item}", itemDescription);
        }
        finally
        {
            _inFlightSearches.TryRemove(searchKey, out _);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DownloadMovieSubtitleAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Downloading subtitle for movie {RadarrId} from {Provider}", request.RadarrId, request.Provider);

        // POST to /api/providers/movies to download manually selected subtitle
        var httpRequest = CreateRequest(HttpMethod.Post, "/api/providers/movies");
        httpRequest.Content = JsonContent.Create(new
        {
            radarrid = request.RadarrId,
            provider = request.Provider,
            subtitle = request.Subtitle,
            hi = request.Hi,
            forced = request.Forced,
            original_format = request.OriginalFormat
        });

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var success = response.IsSuccessStatusCode;

        if (success)
        {
            _logger.LogInformation("Successfully downloaded subtitle for movie {RadarrId}", request.RadarrId);
        }
        else
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Failed to download subtitle for movie {RadarrId}: {StatusCode} - {Content}",
                request.RadarrId,
                response.StatusCode,
                content);
        }

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> DownloadEpisodeSubtitleAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Downloading subtitle for episode {EpisodeId} from {Provider}",
            request.SonarrEpisodeId,
            request.Provider);

        // POST to /api/providers/episodes to download manually selected subtitle
        var httpRequest = CreateRequest(HttpMethod.Post, "/api/providers/episodes");
        httpRequest.Content = JsonContent.Create(new
        {
            seriesid = request.SonarrSeriesId,
            episodeid = request.SonarrEpisodeId,
            provider = request.Provider,
            subtitle = request.Subtitle,
            hi = request.Hi,
            forced = request.Forced,
            original_format = request.OriginalFormat
        });

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var success = response.IsSuccessStatusCode;

        if (success)
        {
            _logger.LogInformation("Successfully downloaded subtitle for episode {EpisodeId}", request.SonarrEpisodeId);
        }
        else
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Failed to download subtitle for episode {EpisodeId}: {StatusCode} - {Content}",
                request.SonarrEpisodeId,
                response.StatusCode,
                content);
        }

        return success;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BazarrLanguage>> GetLanguagesAsync()
    {
        _logger.LogDebug("Fetching languages from Bazarr");

        var request = CreateRequest(HttpMethod.Get, "/api/system/languages");

        _logger.LogDebug("Sending request to {Uri}", request.RequestUri);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        _logger.LogDebug("Received response with status {StatusCode}", response.StatusCode);
        await ValidateResponseAsync(response, "/api/system/languages").ConfigureAwait(false);

        // Languages endpoint returns a direct array, not wrapped in { "data": [...] }
        var result = await response.Content.ReadFromJsonAsync<List<BazarrLanguage>>().ConfigureAwait(false);
        return result ?? (IReadOnlyList<BazarrLanguage>)new List<BazarrLanguage>();
    }

    /// <inheritdoc />
    public async Task<BazarrMovie?> GetMovieByRadarrIdAsync(int radarrId, CancellationToken cancellationToken = default)
    {
        var movies = await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        return movies.FirstOrDefault(m => m.RadarrId == radarrId);
    }

    /// <inheritdoc />
    public async Task<BazarrEpisode?> GetEpisodeBySonarrIdAsync(int sonarrEpisodeId, CancellationToken cancellationToken = default)
    {
        // First we need to find which series this episode belongs to
        var series = await GetSeriesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var s in series)
        {
            var episodes = await GetEpisodesAsync(s.SonarrSeriesId, cancellationToken).ConfigureAwait(false);
            var episode = episodes.FirstOrDefault(e => e.SonarrEpisodeId == sonarrEpisodeId);
            if (episode != null)
            {
                return episode;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestConnectionAsync()
    {
        var url = _configProvider.BazarrUrl ?? "(null)";
        var keyLength = (_configProvider.BazarrApiKey ?? string.Empty).Length;
        _logger.LogInformation(
            "TestConnectionAsync called - URL: {Url}, API Key Length: {KeyLength}",
            url,
            keyLength);

        try
        {
            var languages = await GetLanguagesAsync().ConfigureAwait(false);
            _logger.LogInformation("TestConnectionAsync succeeded with {Count} languages", languages.Count);
            return new ConnectionTestResult
            {
                Success = true,
                Message = $"Connected successfully. {languages.Count} languages available."
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect to Bazarr at {Url}: {Message}",
                _configProvider.BazarrUrl,
                ex.Message);
            return new ConnectionTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error testing Bazarr connection at {Url}: {Type} - {Message}",
                _configProvider.BazarrUrl,
                ex.GetType().Name,
                ex.Message);
            return new ConnectionTestResult
            {
                Success = false,
                Message = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var baseUrl = (_configProvider.BazarrUrl ?? string.Empty).TrimEnd('/');
        var apiKey = _configProvider.BazarrApiKey ?? string.Empty;

        _logger.LogDebug(
            "Creating request to {Url}{Endpoint} with API key length {KeyLength}",
            baseUrl,
            endpoint,
            apiKey.Length);

        var request = new HttpRequestMessage(method, $"{baseUrl}{endpoint}");
        request.Headers.Add("X-API-KEY", apiKey);
        return request;
    }

    /// <summary>
    /// Validates HTTP response and ensures it contains JSON, not HTML or redirects.
    /// </summary>
    /// <param name="response">The HTTP response to validate.</param>
    /// <param name="endpoint">The endpoint that was called (for logging).</param>
    /// <exception cref="InvalidOperationException">Thrown when response is invalid (HTML, redirect, etc).</exception>
    private async Task ValidateResponseAsync(HttpResponseMessage response, string endpoint)
    {
        // Check for redirect responses (301, 302, etc.)
        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
        {
            _logger.LogError(
                "Bazarr returned redirect status {StatusCode} for {Endpoint}",
                response.StatusCode,
                endpoint);
            throw new InvalidOperationException(
                $"Bazarr returned a redirect ({response.StatusCode}). This typically indicates the URL is incorrect " +
                "or there's an intermediary (like a reverse proxy or authentication layer) intercepting the request. " +
                "The plugin needs direct access to Bazarr's API. Check your Bazarr URL configuration.");
        }

        // Check if response is actually HTML (indicates wrong endpoint or intercepted request)
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
        {
            var preview = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var shortPreview = preview.Length > 300 ? string.Concat(preview.AsSpan(0, 300), "...") : preview;

            _logger.LogError(
                "Bazarr returned HTML instead of JSON for {Endpoint}. Content-Type: {ContentType}. " +
                "Response preview: {Preview}",
                endpoint,
                contentType,
                shortPreview);

            throw new InvalidOperationException(
                "Bazarr returned HTML instead of JSON. Possible causes:\n" +
                "- Incorrect Bazarr URL (use base URL like http://localhost:6767, not http://localhost:6767/api)\n" +
                "- Request being intercepted by a proxy or authentication layer\n" +
                "- API endpoint doesn't exist or Bazarr version incompatibility\n\n" +
                $"Response preview: {(preview.Length > 100 ? string.Concat(preview.AsSpan(0, 100), "...") : preview)}");
        }

        // Check for non-JSON content types
        if (contentType != null &&
            !contentType.Contains("json", StringComparison.OrdinalIgnoreCase) &&
            !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Unexpected content type {ContentType} for {Endpoint}",
                contentType,
                endpoint);
        }

        // Now check for HTTP errors
        response.EnsureSuccessStatusCode();
    }
}
