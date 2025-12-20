using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bazarr.Api.Models;
using Jellyfin.Plugin.Bazarr.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bazarr.Api;

/// <summary>
/// Controller for Bazarr subtitle operations.
/// </summary>
[ApiController]
[Route("Bazarr")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public class BazarrController : ControllerBase
{
    private readonly IBazarrService _bazarrService;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<BazarrController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BazarrController"/> class.
    /// </summary>
    /// <param name="bazarrService">Instance of <see cref="IBazarrService"/>.</param>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/>.</param>
    /// <param name="logger">Instance of <see cref="ILogger{BazarrController}"/>.</param>
    public BazarrController(
        IBazarrService bazarrService,
        ILibraryManager libraryManager,
        ILogger<BazarrController> logger)
    {
        _bazarrService = bazarrService;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Search for subtitles for a Jellyfin item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <param name="language">The language code (default: en).</param>
    /// <response code="200">Subtitles found.</response>
    /// <response code="404">Item not found or not in Bazarr.</response>
    /// <returns>List of available subtitles.</returns>
    [HttpGet("Search/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SearchSubtitles(
        [FromRoute, Required] Guid itemId,
        [FromQuery] string language = "en")
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound("Item not found");
        }

        // Handle Movie
        if (item is Movie movie)
        {
            var radarrId = await ResolveRadarrIdAsync(movie).ConfigureAwait(false);
            if (radarrId == null)
            {
                return NotFound("Movie not found in Bazarr. Is it in Radarr?");
            }

            _logger.LogInformation("Searching subtitles for movie {Title} (radarrId: {RadarrId})", movie.Name, radarrId);
            var subtitles = await _bazarrService.SearchMovieSubtitlesAsync(radarrId.Value, language).ConfigureAwait(false);
            return Ok(subtitles);
        }

        // Handle Episode
        if (item is Episode episode)
        {
            var sonarrEpisodeId = await ResolveSonarrEpisodeIdAsync(episode).ConfigureAwait(false);
            if (sonarrEpisodeId == null)
            {
                return NotFound("Episode not found in Bazarr. Is it in Sonarr?");
            }

            _logger.LogInformation(
                "Searching subtitles for {Series} S{Season:D2}E{Episode:D2} (sonarrEpisodeId: {SonarrEpisodeId})",
                episode.SeriesName,
                episode.ParentIndexNumber,
                episode.IndexNumber,
                sonarrEpisodeId);

            // Get series ID for the episode
            int seriesId;
            try
            {
                seriesId = await _bazarrService.GetSeriesIdByEpisodeIdAsync(sonarrEpisodeId.Value).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return NotFound("Could not find series for episode");
            }

            var subtitles = await _bazarrService.SearchEpisodeSubtitlesAsync(sonarrEpisodeId.Value, seriesId, language).ConfigureAwait(false);
            return Ok(subtitles.Subtitles);
        }

        return BadRequest("Item must be a movie or episode");
    }

    /// <summary>
    /// Download a subtitle for a Jellyfin item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <param name="request">The download request.</param>
    /// <response code="200">Subtitle downloaded successfully.</response>
    /// <response code="404">Item not found or not in Bazarr.</response>
    /// <response code="500">Download failed.</response>
    /// <returns>Success status.</returns>
    [HttpPost("Download/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DownloadSubtitle(
        [FromRoute, Required] Guid itemId,
        [FromBody, Required] DownloadRequest request)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound("Item not found");
        }

        bool success;

        // Handle Movie
        if (item is Movie movie)
        {
            var radarrId = await ResolveRadarrIdAsync(movie).ConfigureAwait(false);
            if (radarrId == null)
            {
                return NotFound("Movie not found in Bazarr. Is it in Radarr?");
            }

            request.RadarrId = radarrId.Value;
            _logger.LogInformation("Downloading subtitle for movie {Title}", movie.Name);
            success = await _bazarrService.DownloadMovieSubtitleAsync(request).ConfigureAwait(false);
        }
        else if (item is Episode episode)
        {
            var sonarrEpisodeId = await ResolveSonarrEpisodeIdAsync(episode).ConfigureAwait(false);
            if (sonarrEpisodeId == null)
            {
                return NotFound("Episode not found in Bazarr. Is it in Sonarr?");
            }

            request.SonarrEpisodeId = sonarrEpisodeId.Value;
            _logger.LogInformation(
                "Downloading subtitle for {Series} S{Season:D2}E{Episode:D2}",
                episode.SeriesName,
                episode.ParentIndexNumber,
                episode.IndexNumber);
            success = await _bazarrService.DownloadEpisodeSubtitleAsync(request).ConfigureAwait(false);
        }
        else
        {
            return BadRequest("Item must be a movie or episode");
        }

        if (!success)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to download subtitle");
        }

        _logger.LogInformation("Subtitle downloaded for {Name}", item.Name);
        return Ok(new { success = true, message = "Subtitle downloaded" });
    }

    /// <summary>
    /// Test the connection to Bazarr.
    /// </summary>
    /// <response code="200">Connection test result.</response>
    /// <returns>The connection test result.</returns>
    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection()
    {
        _logger.LogInformation("Testing Bazarr connection");
        var result = await _bazarrService.TestConnectionAsync().ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Get available languages from Bazarr.
    /// </summary>
    /// <response code="200">List of available languages.</response>
    /// <returns>The list of available languages.</returns>
    [HttpGet("Languages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetLanguages()
    {
        var languages = await _bazarrService.GetLanguagesAsync().ConfigureAwait(false);
        return Ok(languages);
    }

    private async Task<int?> ResolveRadarrIdAsync(Movie movie)
    {
        // Try TMDB first - use ProviderIds dictionary directly
        if (movie.ProviderIds.TryGetValue("Tmdb", out var tmdbIdStr) && int.TryParse(tmdbIdStr, out var tmdbId))
        {
            var radarrId = await _bazarrService.FindRadarrIdByTmdbAsync(tmdbId).ConfigureAwait(false);
            if (radarrId != null)
            {
                return radarrId;
            }
        }

        // Fallback to IMDB
        if (movie.ProviderIds.TryGetValue("Imdb", out var imdbId) && !string.IsNullOrEmpty(imdbId))
        {
            return await _bazarrService.FindRadarrIdByImdbAsync(imdbId).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<int?> ResolveSonarrEpisodeIdAsync(Episode episode)
    {
        var series = episode.Series;
        if (series == null)
        {
            return null;
        }

        // Use ProviderIds dictionary directly
        if (!series.ProviderIds.TryGetValue("Tvdb", out var tvdbIdStr) || !int.TryParse(tvdbIdStr, out var tvdbId))
        {
            return null;
        }

        return await _bazarrService.FindSonarrEpisodeIdAsync(
            tvdbId,
            episode.ParentIndexNumber ?? 0,
            episode.IndexNumber ?? 0).ConfigureAwait(false);
    }
}
