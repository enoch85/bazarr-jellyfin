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

    // Track in-flight search requests to avoid duplicate Bazarr API calls, keyed by search cache key
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SubtitleOption>>>> _inFlightSearches = new();

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

        // If still no match, try normalized matching (handles leetspeak like PLUR1BUS vs Pluribus)
        if (show == null)
        {
            var normalizedSearch = NormalizeTitle(seriesTitle);
            show = seriesList.FirstOrDefault(s =>
                string.Equals(NormalizeTitle(s.Title), normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        // Try alternative titles (includes translated titles from Sonarr/TVDB)
        if (show == null)
        {
            show = seriesList.FirstOrDefault(s =>
                s.AlternativeTitles?.Any(alt =>
                    string.Equals(alt, seriesTitle, StringComparison.OrdinalIgnoreCase)) == true);
        }

        // Try partial match on alternative titles
        if (show == null)
        {
            show = seriesList.FirstOrDefault(s =>
                s.AlternativeTitles?.Any(alt =>
                    alt.Contains(seriesTitle, StringComparison.OrdinalIgnoreCase) ||
                    seriesTitle.Contains(alt, StringComparison.OrdinalIgnoreCase)) == true);
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
    public Task<SubtitleSearchResult> SearchMovieSubtitlesAsync(int radarrId, string language, int timeoutSeconds = 0, CancellationToken cancellationToken = default)
        => SearchAsync(
            $"{MovieSearchCacheKeyPrefix}{radarrId}",
            $"movie {radarrId}",
            // CancellationToken.None so the search survives the Jellyfin request being cancelled
            () => SearchMovieSubtitlesInternalAsync(radarrId, language, CancellationToken.None),
            timeoutSeconds,
            cancellationToken);

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
    public Task<SubtitleSearchResult> SearchEpisodeSubtitlesAsync(int sonarrEpisodeId, int sonarrSeriesId, string language, int timeoutSeconds = 0, CancellationToken cancellationToken = default)
        => SearchAsync(
            $"{EpisodeSearchCacheKeyPrefix}{sonarrEpisodeId}",
            $"episode {sonarrEpisodeId}",
            // CancellationToken.None so the search survives the Jellyfin request being cancelled
            () => SearchEpisodeSubtitlesInternalAsync(sonarrEpisodeId, language, CancellationToken.None),
            timeoutSeconds,
            cancellationToken);

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
    /// Serves a subtitle search from cache, joins an identical in-flight search, or starts one.
    /// Callers that hit <paramref name="timeoutSeconds"/> get an in-progress result while the
    /// search keeps running - joining a search must never block longer than starting one.
    /// </summary>
    private async Task<SubtitleSearchResult> SearchAsync(
        string cacheKey,
        string itemDescription,
        Func<Task<IReadOnlyList<SubtitleOption>>> search,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubtitleOption>? cached) && cached != null)
        {
            _logger.LogInformation("Returning cached subtitle search results for {Item} ({Count} subtitles)", itemDescription, cached.Count);
            return new SubtitleSearchResult { Subtitles = cached, FromCache = true };
        }

        // Lazy guarantees the Bazarr search starts exactly once even when callers race here.
        var searchTask = _inFlightSearches.GetOrAdd(
            cacheKey,
            key => new Lazy<Task<IReadOnlyList<SubtitleOption>>>(() => RunSearchAsync(search, key, itemDescription))).Value;

        if (timeoutSeconds > 0)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            if (await Task.WhenAny(searchTask, timeoutTask).ConfigureAwait(false) != searchTask)
            {
                _logger.LogInformation(
                    "Search timeout ({Timeout}s) reached for {Item}. Search continues in background.",
                    timeoutSeconds,
                    itemDescription);

                return new SubtitleSearchResult { Subtitles = [], SearchInProgress = true };
            }
        }

        return new SubtitleSearchResult { Subtitles = await searchTask.ConfigureAwait(false) };
    }

    /// <summary>
    /// Owns a single in-flight search: caches the result and always releases the in-flight slot,
    /// whether the caller is still waiting or has already timed out.
    /// </summary>
    private async Task<IReadOnlyList<SubtitleOption>> RunSearchAsync(
        Func<Task<IReadOnlyList<SubtitleOption>>> search,
        string cacheKey,
        string itemDescription)
    {
        try
        {
            var result = await search().ConfigureAwait(false);
            _cache.Set(cacheKey, result, SearchResultCacheDuration);
            _logger.LogInformation(
                "Search completed for {Item}. Found {Count} subtitles, cached for {Duration} minutes.",
                itemDescription,
                result.Count,
                SearchResultCacheDuration.TotalMinutes);

            return result;
        }
        catch (Exception ex)
        {
            // Failures are deliberately not cached so the next search retries.
            _logger.LogError(ex, "Search failed for {Item}", itemDescription);
            throw;
        }
        finally
        {
            _inFlightSearches.TryRemove(cacheKey, out _);
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
    /// Normalizes a title for fuzzy matching by replacing common leetspeak substitutions.
    /// </summary>
    /// <param name="title">The title to normalize.</param>
    /// <returns>Normalized title with leetspeak characters replaced.</returns>
    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return title;
        }

        // Replace common leetspeak substitutions
        return title
            .Replace("1", "I", StringComparison.OrdinalIgnoreCase)
            .Replace("0", "O", StringComparison.OrdinalIgnoreCase)
            .Replace("3", "E", StringComparison.OrdinalIgnoreCase)
            .Replace("4", "A", StringComparison.OrdinalIgnoreCase)
            .Replace("5", "S", StringComparison.OrdinalIgnoreCase)
            .Replace("7", "T", StringComparison.OrdinalIgnoreCase)
            .Replace("8", "B", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates HTTP response and ensures it contains JSON, not HTML or redirects.
    /// </summary>
    /// <param name="response">The HTTP response to validate.</param>
    /// <param name="endpoint">The endpoint that was called (for logging).</param>
    /// <exception cref="InvalidOperationException">Thrown when response is invalid (HTML, redirect, etc).</exception>
    /// <exception cref="HttpRequestException">Thrown when Bazarr returns an error status, carrying its own message.</exception>
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
                "- Proxy or authentication layer intercepting the request (if using auth proxy, bypass it for /api/* or use internal URL)\n" +
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

        // Now check for HTTP errors. Bazarr explains the failure in a JSON-encoded string body
        // (e.g. "All providers are throttled"), so surface that instead of a bare status code.
        if (!response.IsSuccessStatusCode)
        {
            var body = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim().Trim('"');
            var detail = body.Length switch
            {
                0 => response.ReasonPhrase,
                > 200 => body[..200],
                _ => body
            };

            _logger.LogError("Bazarr returned {StatusCode} for {Endpoint}: {Detail}", (int)response.StatusCode, endpoint, detail);

            throw new HttpRequestException(
                $"Bazarr returned {(int)response.StatusCode} for {endpoint}: {detail}",
                null,
                response.StatusCode);
        }
    }
}
