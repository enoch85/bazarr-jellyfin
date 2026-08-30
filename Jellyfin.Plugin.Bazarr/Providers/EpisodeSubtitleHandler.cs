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
    /// <param name="providerIds">The provider IDs (TVDB, IMDB, etc.) - typically episode-level.</param>
    /// <param name="seriesName">The series name (may be localized).</param>
    /// <param name="seasonNumber">The season number.</param>
    /// <param name="episodeNumber">The episode number.</param>
    /// <param name="language">The full language string.</param>
    /// <param name="twoLetterISOLanguageName">The two-letter ISO language code.</param>
    /// <param name="timeoutSeconds">The search timeout in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="seriesProviderIds">Optional series-level provider IDs (TVDB, IMDB at series level).</param>
    /// <param name="originalSeriesName">Optional original (non-localized) series name.</param>
    /// <returns>A list of remote subtitle info.</returns>
    public async Task<IEnumerable<RemoteSubtitleInfo>> SearchAsync(
        Dictionary<string, string> providerIds,
        string? seriesName,
        int? seasonNumber,
        int? episodeNumber,
        string? language,
        string? twoLetterISOLanguageName,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        Dictionary<string, string>? seriesProviderIds = null,
        string? originalSeriesName = null)
    {
        // Try to find the episode in Bazarr
        int? sonarrEpisodeId = null;

        // Log available provider IDs for debugging
        _logger.LogDebug(
            "Episode search request - Series: {Series} (Original: {OriginalSeries}), S{Season}E{Episode}, EpisodeProviderIds: {ProviderIds}, SeriesProviderIds: {SeriesProviderIds}",
            seriesName,
            originalSeriesName ?? "(not available)",
            seasonNumber,
            episodeNumber,
            string.Join(", ", providerIds.Select(kv => $"{kv.Key}={kv.Value}")),
            seriesProviderIds != null ? string.Join(", ", seriesProviderIds.Select(kv => $"{kv.Key}={kv.Value}")) : "(not available)");

        // PRIORITY 1: Try series-level TVDB ID (most reliable when available)
        if (sonarrEpisodeId == null &&
            seriesProviderIds?.TryGetValue("Tvdb", out var seriesTvdbIdStr) == true &&
            int.TryParse(seriesTvdbIdStr, out var seriesTvdbId) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogDebug(
                "Trying series-level TVDB ID {TvdbId}, S{Season}E{Episode}",
                seriesTvdbId,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdAsync(
                seriesTvdbId,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // PRIORITY 2: Try series-level IMDB ID
        if (sonarrEpisodeId == null &&
            seriesProviderIds?.TryGetValue("Imdb", out var seriesImdbId) == true &&
            !string.IsNullOrEmpty(seriesImdbId) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogDebug(
                "Trying series-level IMDB ID {ImdbId}, S{Season}E{Episode}",
                seriesImdbId,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdByImdbAsync(
                seriesImdbId,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // PRIORITY 3: Try episode-level TVDB ID (rarely works - episode ID ≠ series ID)
        if (sonarrEpisodeId == null &&
            providerIds.TryGetValue("Tvdb", out var tvdbIdStr) &&
            int.TryParse(tvdbIdStr, out var tvdbId) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogDebug(
                "Trying episode-level TVDB ID {TvdbId} (note: this is the episode's TVDB ID), S{Season}E{Episode}",
                tvdbId,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdAsync(
                tvdbId,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // PRIORITY 4: Try episode-level IMDB ID
        if (sonarrEpisodeId == null &&
            providerIds.TryGetValue("Imdb", out var imdbId) &&
            !string.IsNullOrEmpty(imdbId) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogDebug(
                "Trying episode-level IMDB ID {ImdbId}, S{Season}E{Episode}",
                imdbId,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdByImdbAsync(
                imdbId,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // PRIORITY 5: Try original series name first (non-localized)
        if (sonarrEpisodeId == null &&
            !string.IsNullOrEmpty(originalSeriesName) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogInformation(
                "ID lookups failed, trying original title match for '{Series}' S{Season}E{Episode}",
                originalSeriesName,
                seasonNumber.Value,
                episodeNumber.Value);

            sonarrEpisodeId = await _bazarrService.FindSonarrEpisodeIdByTitleAsync(
                originalSeriesName,
                seasonNumber.Value,
                episodeNumber.Value,
                cancellationToken).ConfigureAwait(false);
        }

        // PRIORITY 6: Fallback to localized series name (may match via alternativeTitles)
        if (sonarrEpisodeId == null &&
            !string.IsNullOrEmpty(seriesName) &&
            seasonNumber.HasValue &&
            episodeNumber.HasValue)
        {
            _logger.LogInformation(
                "Trying localized title match for '{Series}' S{Season}E{Episode}",
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
            return SubtitlePlaceholder.SearchInProgress();
        }

        var subtitles = searchResult.Subtitles;

        // Log subtitle languages for debugging
        if (subtitles.Count > 0)
        {
            var languageCodes = string.Join(", ", subtitles.Select(s => $"'{s.Language}'").Distinct());
            _logger.LogInformation(
                "Bazarr returned subtitles with language codes: {Languages}. Filtering for requested language: '{RequestedLanguage}'",
                languageCodes,
                languageCode);
        }

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
            Comment = SubtitleLanguageHelper.FormatSubtitleComment(s),
            IsHashMatch = s.Matches?.Contains("hash") ?? false
        });
    }
}
