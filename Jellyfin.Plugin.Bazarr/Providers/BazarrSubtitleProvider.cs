using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bazarr.Services;
using MediaBrowser.Controller.Entities;
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

            if (request.ContentType == VideoContentType.Movie)
            {
                if (!config.EnableForMovies)
                {
                    _logger.LogDebug("Movie subtitle search disabled in plugin settings");
                    return Enumerable.Empty<RemoteSubtitleInfo>();
                }

                return await _movieHandler.SearchAsync(
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

                return await _episodeHandler.SearchAsync(
                    request.ProviderIds,
                    request.SeriesName,
                    request.ParentIndexNumber,
                    request.IndexNumber,
                    request.Language,
                    request.TwoLetterISOLanguageName,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Bazarr for subtitles");
        }

        return Enumerable.Empty<RemoteSubtitleInfo>();
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading subtitle from Bazarr: {Id}", id);

        // ID format: "movie|episode|radarrId|sonarrEpisodeId|provider|hi|forced|subtitle(escaped)"
        var parts = id.Split('|');
        if (parts.Length < 6)
        {
            throw new ArgumentException($"Invalid subtitle ID format: {id}");
        }

        var itemType = parts[0];
        var bazarrId = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var provider = parts[2];
        var hi = parts[3];
        var forced = parts[4];
        var subtitle = Uri.UnescapeDataString(parts[5]);

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
        await RefreshItemSubtitlesAsync(itemType, bazarrId, cancellationToken).ConfigureAwait(false);

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
    private async Task RefreshItemSubtitlesAsync(string itemType, int bazarrId, CancellationToken cancellationToken)
    {
        try
        {
            BaseItem? item = null;

            if (itemType == "movie")
            {
                // Find movie by Radarr ID via TMDB lookup
                var radarrMovie = await _bazarrService.GetMovieByRadarrIdAsync(bazarrId, cancellationToken).ConfigureAwait(false);
                if (radarrMovie != null && radarrMovie.TmdbId.HasValue)
                {
                    var tmdbIdStr = radarrMovie.TmdbId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var movies = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = [BaseItemKind.Movie],
                        IsVirtualItem = false,
                        Recursive = true
                    });
                    item = movies.FirstOrDefault(m =>
                        m.ProviderIds.TryGetValue("Tmdb", out var id) && id == tmdbIdStr);
                }
            }
            else if (itemType == "episode")
            {
                // Find episode by matching season and episode number
                var bazarrEpisode = await _bazarrService.GetEpisodeBySonarrIdAsync(bazarrId, cancellationToken).ConfigureAwait(false);
                if (bazarrEpisode != null)
                {
                    var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = [BaseItemKind.Episode],
                        IsVirtualItem = false,
                        Recursive = true
                    });
                    item = episodes.Cast<Episode>().FirstOrDefault(e =>
                        e.ParentIndexNumber == bazarrEpisode.Season &&
                        e.IndexNumber == bazarrEpisode.Episode);
                }
            }

            if (item != null)
            {
                _logger.LogInformation("Triggering full metadata refresh for {ItemType} '{Name}' to detect new subtitle", itemType, item.Name);

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
                _logger.LogWarning("Could not find {ItemType} with Bazarr ID {BazarrId} for refresh", itemType, bazarrId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing {ItemType} after subtitle download", itemType);
            // Don't throw - the download succeeded, refresh is optional
        }
    }
}
