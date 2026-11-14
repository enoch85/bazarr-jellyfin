using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bazarr.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bazarr.Providers;

/// <summary>
/// Bazarr subtitle provider for Jellyfin's native subtitle search.
/// Delegates to specialized handlers for movie and episode searches.
/// </summary>
public class BazarrSubtitleProvider : ISubtitleProvider
{
    private readonly ILogger<BazarrSubtitleProvider> _logger;
    private readonly IBazarrService _bazarrService;
    private readonly ILibraryManager _libraryManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFileSystem _fileSystem;
    private readonly MovieSubtitleHandler _movieHandler;
    private readonly EpisodeSubtitleHandler _episodeHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="BazarrSubtitleProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="bazarrService">The Bazarr service.</param>
    /// <param name="libraryManager">The library manager for refreshing items.</param>
    /// <param name="serviceProvider">The service provider for lazy resolution.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="movieHandler">The movie subtitle handler.</param>
    /// <param name="episodeHandler">The episode subtitle handler.</param>
    public BazarrSubtitleProvider(
        ILogger<BazarrSubtitleProvider> logger,
        IBazarrService bazarrService,
        ILibraryManager libraryManager,
        IServiceProvider serviceProvider,
        IFileSystem fileSystem,
        MovieSubtitleHandler movieHandler,
        EpisodeSubtitleHandler episodeHandler)
    {
        _logger = logger;
        _bazarrService = bazarrService;
        _libraryManager = libraryManager;
        _serviceProvider = serviceProvider;
        _fileSystem = fileSystem;
        _movieHandler = movieHandler;
        _episodeHandler = episodeHandler;
    }

    /// <inheritdoc />
    public string Name => "Bazarr";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes => new[]
    {
        VideoContentType.Movie,
        VideoContentType.Episode
    };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        // Skip automated requests (media scans, scheduled tasks, etc.)
        // Bazarr searches are slow by design - they query multiple subtitle providers in real-time
        // which can take 1-2 minutes per search. This would block media scans indefinitely.
        // This plugin is designed for manual user-initiated searches only.
        if (request.IsAutomated)
        {
            _logger.LogDebug("Skipping automated subtitle search - Bazarr is designed for manual searches only");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrEmpty(config.BazarrUrl) || string.IsNullOrEmpty(config.BazarrApiKey))
        {
            _logger.LogWarning("Bazarr is not configured. Please configure the plugin first");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        _logger.LogDebug(
            "Bazarr subtitle search: ContentType={ContentType}, Name={Name}, Language={Language}",
            request.ContentType,
            request.Name,
            request.Language);

        try
        {
            var timeout = config.SearchTimeoutSeconds;

            // Carried in the subtitle IDs for the post-download refresh
            var item = string.IsNullOrEmpty(request.MediaPath) ? null : _libraryManager.FindByPath(request.MediaPath, false);
            var itemId = item?.Id ?? Guid.Empty;

            if (request.ContentType == VideoContentType.Movie)
            {
                if (!config.EnableForMovies)
                {
                    _logger.LogDebug("Movie subtitle search disabled in plugin settings");
                    return Enumerable.Empty<RemoteSubtitleInfo>();
                }

                return await _movieHandler.SearchAsync(
                    itemId,
                    request.ProviderIds,
                    request.Name,
                    request.Language,
                    request.TwoLetterISOLanguageName,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            }

            if (request.ContentType == VideoContentType.Episode)
            {
                if (!config.EnableForEpisodes)
                {
                    _logger.LogDebug("Episode subtitle search disabled in plugin settings");
                    return Enumerable.Empty<RemoteSubtitleInfo>();
                }

                var series = (item as Episode)?.Series;

                return await _episodeHandler.SearchAsync(
                    itemId,
                    request.ProviderIds,
                    request.SeriesName,
                    request.ParentIndexNumber,
                    request.IndexNumber,
                    request.Language,
                    request.TwoLetterISOLanguageName,
                    timeout,
                    cancellationToken,
                    series?.ProviderIds,
                    series?.OriginalTitle).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Jellyfin swallows provider exceptions, so the user would otherwise just see "no results".
            _logger.LogError(ex, "Error searching Bazarr for subtitles");
            return SubtitlePlaceholder.SearchFailed(ex.Message);
        }

        return Enumerable.Empty<RemoteSubtitleInfo>();
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading subtitle from Bazarr: {Id}", id);

        if (id.StartsWith(SubtitlePlaceholder.IdPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This entry is a status message, not a subtitle. Run the search again once Bazarr has results.");
        }

        // ID format: "{movie|episode}|{bazarrId}|provider|hi|forced|subtitle(escaped)|itemId"
        var parts = id.Split('|');
        if (parts.Length < 7)
        {
            throw new ArgumentException($"Invalid subtitle ID format: {id}");
        }

        var itemType = parts[0];
        var bazarrId = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var provider = parts[2];
        var hi = parts[3];
        var forced = parts[4];
        var subtitle = Uri.UnescapeDataString(parts[5]);
        var itemId = Guid.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture);

        bool success;
        if (itemType == "movie")
        {
            var request = new Api.Models.DownloadRequest
            {
                RadarrId = bazarrId,
                Provider = provider,
                Subtitle = subtitle,
                Hi = hi,
                Forced = forced,
                OriginalFormat = "False"
            };
            success = await _bazarrService.DownloadMovieSubtitleAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else if (itemType == "episode")
        {
            // For episodes, we need the series ID which we don't have in the subtitle ID
            // We'll need to look it up from Bazarr
            var seriesId = await _bazarrService.GetSeriesIdByEpisodeIdAsync(bazarrId, cancellationToken).ConfigureAwait(false);

            var request = new Api.Models.DownloadRequest
            {
                SonarrSeriesId = seriesId,
                SonarrEpisodeId = bazarrId,
                Provider = provider,
                Subtitle = subtitle,
                Hi = hi,
                Forced = forced,
                OriginalFormat = "False"
            };
            success = await _bazarrService.DownloadEpisodeSubtitleAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentException($"Unknown item type: {itemType}");
        }

        if (!success)
        {
            throw new InvalidOperationException("Failed to download subtitle from Bazarr");
        }

        // Bazarr downloads directly to the media folder.
        // Trigger a refresh of the item so Jellyfin picks up the new subtitle.
        await RefreshItemSubtitlesAsync(itemId, cancellationToken).ConfigureAwait(false);

        // We throw an exception to prevent Jellyfin from trying to save an empty file.
        // The subtitle has been saved by Bazarr and the item has been refreshed.
        _logger.LogInformation("Subtitle downloaded by Bazarr and item refresh triggered.");
        throw new InvalidOperationException(
            "Subtitle downloaded successfully by Bazarr. " +
            "The item is being refreshed - please close and reopen this dialog to see the new subtitle.");
    }

    /// <summary>
    /// Refreshes an item's subtitles after download.
    /// </summary>
    private async Task RefreshItemSubtitlesAsync(Guid itemId, CancellationToken cancellationToken)
    {
        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item != null)
            {
                _logger.LogInformation("Triggering full metadata refresh for '{Name}' to detect new subtitle", item.Name);

                // Resolve IProviderManager lazily to avoid circular dependency
                // (IProviderManager -> ISubtitleManager -> ISubtitleProvider -> IProviderManager)
                var providerManager = _serviceProvider.GetRequiredService<IProviderManager>();

                // Use RefreshFullItem with MetadataRefreshMode.Default to trigger ProbeProvider
                // which will scan for new external subtitle files
                var refreshOptions = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    // Default mode triggers the ProbeProvider which detects external subtitle changes
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    // Force save to ensure changes are persisted
                    ForceSave = true
                };

                await providerManager.RefreshFullItem(item, refreshOptions, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Full metadata refresh completed for '{Name}'", item.Name);
            }
            else
            {
                _logger.LogWarning("Could not find item {ItemId} for refresh", itemId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing item {ItemId} after subtitle download", itemId);
            // Don't throw - the download succeeded, refresh is optional
        }
    }
}
