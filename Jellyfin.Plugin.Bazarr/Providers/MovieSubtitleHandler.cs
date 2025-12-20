using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bazarr.Services;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bazarr.Providers;

/// <summary>
/// Handles movie subtitle search operations.
/// </summary>
public class MovieSubtitleHandler
{
    private readonly ILogger<MovieSubtitleHandler> _logger;
    private readonly IBazarrService _bazarrService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieSubtitleHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="bazarrService">The Bazarr service.</param>
    public MovieSubtitleHandler(
        ILogger<MovieSubtitleHandler> logger,
        IBazarrService bazarrService)
    {
        _logger = logger;
        _bazarrService = bazarrService;
    }

    /// <summary>
    /// Searches for movie subtitles.
    /// </summary>
    /// <param name="providerIds">The provider IDs (TMDB, IMDB, etc.).</param>
    /// <param name="name">The movie name.</param>
    /// <param name="language">The full language string.</param>
    /// <param name="twoLetterISOLanguageName">The two-letter ISO language code.</param>
    /// <param name="timeoutSeconds">The search timeout in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of remote subtitle info.</returns>
    public async Task<IEnumerable<RemoteSubtitleInfo>> SearchAsync(
        Dictionary<string, string> providerIds,
        string? name,
        string? language,
        string? twoLetterISOLanguageName,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        // Try to find the movie in Bazarr by TMDB ID
        int? radarrId = null;

        if (providerIds.TryGetValue("Tmdb", out var tmdbIdStr) &&
            int.TryParse(tmdbIdStr, out var tmdbId))
        {
            radarrId = await _bazarrService.FindRadarrIdByTmdbAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        }

        if (radarrId == null && providerIds.TryGetValue("Imdb", out var imdbId))
        {
            radarrId = await _bazarrService.FindRadarrIdByImdbAsync(imdbId, cancellationToken).ConfigureAwait(false);
        }

        if (radarrId == null)
        {
            _logger.LogWarning("Movie not found in Bazarr: {Name}", name);
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        var languageCode = SubtitleLanguageHelper.GetLanguageCode(language, twoLetterISOLanguageName);

        var searchResult = await _bazarrService.SearchMovieSubtitlesAsync(radarrId.Value, languageCode, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        // If search is still in progress, return a placeholder to inform the user
        if (searchResult.SearchInProgress)
        {
            _logger.LogInformation("Movie search in progress - returning placeholder to user");
            return new[]
            {
                new RemoteSubtitleInfo
                {
                    Id = "search_in_progress",
                    Name = "Search in progress - results typically ready in 5-15 minutes",
                    ProviderName = "Bazarr",
                    Comment = "Bazarr is searching multiple providers in the background. Click 'Search' again later to see cached results."
                }
            };
        }

        var subtitles = searchResult.Subtitles;

        // Filter subtitles by requested language
        var filteredSubtitles = SubtitleLanguageHelper.FilterByLanguage(subtitles, languageCode).ToList();

        _logger.LogInformation(
            "Movie subtitle search: {Total} total subtitles, {Filtered} after filtering for language '{Language}'{CacheInfo}",
            subtitles.Count,
            filteredSubtitles.Count,
            languageCode,
            searchResult.FromCache ? " (from cache)" : string.Empty);

        return filteredSubtitles.Select(s => new RemoteSubtitleInfo
        {
            // Encode the subtitle info in the ID - we use | as separator since : may appear in subtitle data
            Id = $"movie|{radarrId}|{s.Provider}|{s.HearingImpaired ?? "False"}|{s.Forced ?? "False"}|{Uri.EscapeDataString(s.Subtitle)}",
            Name = s.Release,
            ProviderName = "Bazarr",
            Format = SubtitleLanguageHelper.GetSubtitleFormat(s.OriginalFormat),
            ThreeLetterISOLanguageName = s.Language,
            Comment = $"{s.Provider} - Score: {s.Score}",
            IsHashMatch = s.Matches?.Contains("hash") ?? false
        });
    }
}
