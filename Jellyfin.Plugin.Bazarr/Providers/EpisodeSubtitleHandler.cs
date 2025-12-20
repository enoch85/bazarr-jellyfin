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
/// Handles episode subtitle search operations.
/// </summary>
public class EpisodeSubtitleHandler
{
    private readonly ILogger<EpisodeSubtitleHandler> _logger;
    private readonly IBazarrService _bazarrService;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpisodeSubtitleHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="bazarrService">The Bazarr service.</param>
    public EpisodeSubtitleHandler(
        ILogger<EpisodeSubtitleHandler> logger,
        IBazarrService bazarrService)
    {
        _logger = logger;
        _bazarrService = bazarrService;
    }

    /// <summary>
    /// Searches for episode subtitles.
    /// </summary>
    /// <param name="providerIds">The provider IDs (TVDB, IMDB, etc.).</param>
    /// <param name="seriesName">The series name.</param>
    /// <param name="seasonNumber">The season number.</param>
    /// <param name="episodeNumber">The episode number.</param>
    /// <param name="language">The full language string.</param>
    /// <param name="twoLetterISOLanguageName">The two-letter ISO language code.</param>
    /// <param name="timeoutSeconds">The search timeout in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of remote subtitle info.</returns>
    public async Task<IEnumerable<RemoteSubtitleInfo>> SearchAsync(
        Dictionary<string, string> providerIds,
        string? seriesName,
        int? seasonNumber,
        int? episodeNumber,
        string? language,
        string? twoLetterISOLanguageName,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        // Try to find the episode in Bazarr
        int? sonarrEpisodeId = null;

        // Log available provider IDs for debugging
        _logger.LogDebug(
            "Episode search request - Series: {Series}, S{Season}E{Episode}, ProviderIds: {ProviderIds}",
            seriesName,
            seasonNumber,
            episodeNumber,
            string.Join(", ", providerIds.Select(kv => $"{kv.Key}={kv.Value}")));

        // First try by TVDB ID - note that ProviderIds contains EPISODE provider IDs
        // For episodes, the TVDB ID in ProviderIds is the EPISODE's TVDB ID, not the series!
        // This will only work if Bazarr happens to have a series with a matching TVDB ID
        if (providerIds.TryGetValue("Tvdb", out var tvdbIdStr) &&
            int.TryParse(tvdbIdStr, out var tvdbId) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogDebug(
                "Trying TVDB ID {TvdbId} (note: this is the episode's TVDB ID), S{Season}E{Episode}",
                tvdbId,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdAsync(
                tvdbId,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // Try IMDB ID - IMDB uses series-level IDs for episodes, so this is more reliable!
        if (sonarrEpisodeId == null &&
            providerIds.TryGetValue("Imdb", out var imdbId) &&
            !string.IsNullOrEmpty(imdbId) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogDebug(
                "Trying IMDB ID {ImdbId}, S{Season}E{Episode}",
                imdbId,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdByImdbAsync(
                imdbId,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // Fallback to series title matching if ID lookups failed
        if (sonarrEpisodeId == null &&
            !string.IsNullOrEmpty(seriesName) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogInformation(
                "ID lookups failed, trying title match for '{Series}' S{Season}E{Episode}",
                seriesName,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdByTitleAsync(
                seriesName,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        if (sonarrEpisodeId == null)
        {
            _logger.LogWarning(
                "Episode not found in Bazarr: {Series} S{Season}E{Episode}",
                seriesName,
                seasonNumber,
                episodeNumber);
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        var languageCode = SubtitleLanguageHelper.GetLanguageCode(language, twoLetterISOLanguageName);

        // Get the series ID for this episode
        var sonarrSeriesId = await _bazarrService.GetSeriesIdByEpisodeIdAsync(sonarrEpisodeId.Value, cancellationToken).ConfigureAwait(false);

        var searchResult = await _bazarrService.SearchEpisodeSubtitlesAsync(sonarrEpisodeId.Value, sonarrSeriesId, languageCode, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        // If search is still in progress, return a placeholder to inform the user
        if (searchResult.SearchInProgress)
        {
            _logger.LogInformation("Episode search in progress - returning placeholder to user");
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
            "Episode subtitle search: {Total} total subtitles, {Filtered} after filtering for language '{Language}'{CacheInfo}",
            subtitles.Count,
            filteredSubtitles.Count,
            languageCode,
            searchResult.FromCache ? " (from cache)" : string.Empty);

        return filteredSubtitles.Select(s => new RemoteSubtitleInfo
        {
            // Encode the subtitle info in the ID
            Id = $"episode|{sonarrEpisodeId}|{s.Provider}|{s.HearingImpaired ?? "False"}|{s.Forced ?? "False"}|{Uri.EscapeDataString(s.Subtitle)}",
            Name = s.Release,
            ProviderName = "Bazarr",
            Format = SubtitleLanguageHelper.GetSubtitleFormat(s.OriginalFormat),
            ThreeLetterISOLanguageName = s.Language,
            Comment = $"{s.Provider} - Score: {s.Score}",
            IsHashMatch = s.Matches?.Contains("hash") ?? false
        });
    }
}
