using System.Net;
using Jellyfin.Plugin.Bazarr.Api.Models;
using Jellyfin.Plugin.Bazarr.Services;
using Jellyfin.Plugin.Bazarr.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Jellyfin.Plugin.Bazarr.Tests;

/// <summary>
/// Tests for BazarrService that verify critical ID lookup logic.
/// These tests ensure the service correctly maps external IDs (TMDB, IMDB, TVDB)
/// to internal Radarr/Sonarr IDs. A bug here would mean:
/// - Subtitles downloaded for the wrong movie/episode
/// - Unable to find items that exist in Bazarr
/// </summary>
public class BazarrServiceIdLookupTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<BazarrService>> _loggerMock;
    private readonly MockConfigProvider _configProvider;
    private readonly BazarrService _service;

    public BazarrServiceIdLookupTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<BazarrService>>();
        _configProvider = new MockConfigProvider();
        _service = new BazarrService(_httpClient, _cache, _loggerMock.Object, _configProvider);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    #region FindRadarrIdByTmdbAsync Tests

    /// <summary>
    /// CRITICAL: Verify we find the correct movie by TMDB ID.
    /// If this fails, subtitles could be downloaded for the wrong movie.
    /// </summary>
    [Fact]
    public async Task FindRadarrIdByTmdbAsync_WhenMovieExists_ReturnsCorrectRadarrId()
    {
        // Arrange - Bazarr returns 3 movies, we want movie with TMDB ID 550 (Fight Club)
        var moviesJson = """
        {
            "data": [
                { "radarrId": 1, "title": "The Matrix", "tmdbId": 603 },
                { "radarrId": 2, "title": "Fight Club", "tmdbId": 550 },
                { "radarrId": 3, "title": "Inception", "tmdbId": 27205 }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act
        var result = await _service.FindRadarrIdByTmdbAsync(550);

        // Assert - Must return radarrId 2 for Fight Club, not any other movie
        Assert.Equal(2, result);
    }

    /// <summary>
    /// Verify we don't accidentally return a movie when TMDB ID doesn't match.
    /// A bug here could cause subtitles for a random movie.
    /// </summary>
    [Fact]
    public async Task FindRadarrIdByTmdbAsync_WhenMovieDoesNotExist_ReturnsNull()
    {
        // Arrange - Bazarr has movies, but not the one we're looking for
        var moviesJson = """
        {
            "data": [
                { "radarrId": 1, "title": "The Matrix", "tmdbId": 603 },
                { "radarrId": 2, "title": "Inception", "tmdbId": 27205 }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act - Look for TMDB ID 550 which doesn't exist
        var result = await _service.FindRadarrIdByTmdbAsync(550);

        // Assert - Must be null, not some other movie
        Assert.Null(result);
    }

    /// <summary>
    /// Edge case: What if Bazarr has no movies at all?
    /// </summary>
    [Fact]
    public async Task FindRadarrIdByTmdbAsync_WhenNoMoviesExist_ReturnsNull()
    {
        // Arrange - Empty list from Bazarr
        var moviesJson = """{ "data": [] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act
        var result = await _service.FindRadarrIdByTmdbAsync(550);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region FindRadarrIdByImdbAsync Tests

    /// <summary>
    /// CRITICAL: IMDB lookup is the fallback when TMDB fails.
    /// Verify case-insensitive matching (IMDB IDs could be "tt0137523" or "TT0137523").
    /// </summary>
    [Fact]
    public async Task FindRadarrIdByImdbAsync_WhenMovieExists_IsCaseInsensitive()
    {
        // Arrange - Bazarr stores IMDB ID in lowercase
        var moviesJson = """
        {
            "data": [
                { "radarrId": 1, "title": "The Matrix", "imdbId": "tt0133093" },
                { "radarrId": 2, "title": "Fight Club", "imdbId": "tt0137523" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act - Search with uppercase (could come from Jellyfin metadata)
        var result = await _service.FindRadarrIdByImdbAsync("TT0137523");

        // Assert - Must find it despite case difference
        Assert.Equal(2, result);
    }

    /// <summary>
    /// Verify exact IMDB matching - don't match partial strings.
    /// "tt013" should not match "tt0137523".
    /// </summary>
    [Fact]
    public async Task FindRadarrIdByImdbAsync_RequiresExactMatch()
    {
        // Arrange
        var moviesJson = """
        {
            "data": [
                { "radarrId": 1, "title": "Fight Club", "imdbId": "tt0137523" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act - Search with partial IMDB ID
        var result = await _service.FindRadarrIdByImdbAsync("tt013");

        // Assert - Must not match
        Assert.Null(result);
    }

    #endregion

    #region FindSonarrEpisodeIdAsync Tests

    /// <summary>
    /// CRITICAL: Episode lookup requires finding the series first, then the episode.
    /// This tests the full chain: TVDB -> Sonarr Series ID -> Episode.
    /// </summary>
    [Fact]
    public async Task FindSonarrEpisodeIdAsync_WhenEpisodeExists_ReturnsCorrectId()
    {
        // Arrange - Series lookup
        var seriesJson = """
        {
            "data": [
                { "sonarrSeriesId": 10, "title": "Breaking Bad", "tvdbId": 81189 },
                { "sonarrSeriesId": 20, "title": "The Office", "tvdbId": 73244 }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        // Arrange - Episode lookup for series 10 (Breaking Bad)
        var episodesJson = """
        {
            "data": [
                { "sonarrEpisodeId": 100, "sonarrSeriesId": 10, "season": 1, "episode": 1, "title": "Pilot" },
                { "sonarrEpisodeId": 101, "sonarrSeriesId": 10, "season": 1, "episode": 2, "title": "Cat's in the Bag..." },
                { "sonarrEpisodeId": 150, "sonarrSeriesId": 10, "season": 2, "episode": 1, "title": "Seven Thirty-Seven" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodesJson);

        // Act - Find Breaking Bad S01E02
        var result = await _service.FindSonarrEpisodeIdAsync(81189, season: 1, episode: 2);

        // Assert - Must return episode 101 (S01E02)
        Assert.Equal(101, result);
    }

    /// <summary>
    /// CRITICAL: Don't match episodes from wrong season.
    /// S02E01 should not match when looking for S01E01.
    /// </summary>
    [Fact]
    public async Task FindSonarrEpisodeIdAsync_MustMatchBothSeasonAndEpisode()
    {
        // Arrange - Series
        var seriesJson = """
        {
            "data": [
                { "sonarrSeriesId": 10, "title": "Breaking Bad", "tvdbId": 81189 }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        // Arrange - Only has S02E01, not S01E01
        var episodesJson = """
        {
            "data": [
                { "sonarrEpisodeId": 150, "sonarrSeriesId": 10, "season": 2, "episode": 1, "title": "Seven Thirty-Seven" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodesJson);

        // Act - Look for S01E01 which doesn't exist
        var result = await _service.FindSonarrEpisodeIdAsync(81189, season: 1, episode: 1);

        // Assert - Must be null, don't return S02E01 just because episode number matches
        Assert.Null(result);
    }

    /// <summary>
    /// When series doesn't exist in Bazarr, should return null early.
    /// </summary>
    [Fact]
    public async Task FindSonarrEpisodeIdAsync_WhenSeriesNotFound_ReturnsNullWithoutEpisodeCall()
    {
        // Arrange - Series list doesn't include our show
        var seriesJson = """
        {
            "data": [
                { "sonarrSeriesId": 20, "title": "The Office", "tvdbId": 73244 }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);
        // Note: We don't queue an episode response - if it's called, test will fail

        // Act - Look for series that doesn't exist
        var result = await _service.FindSonarrEpisodeIdAsync(81189, season: 1, episode: 1);

        // Assert
        Assert.Null(result);
        // Verify only one request was made (series lookup, not episodes)
        Assert.Single(_mockHandler.CapturedRequests);
    }

    #endregion

    #region FindSonarrEpisodeIdByImdbAsync Tests

    /// <summary>
    /// CRITICAL: IMDB ID-based episode lookup - IMDB uses series-level IDs for episodes.
    /// This is more reliable than TVDB because Jellyfin's ProviderIds contains
    /// the episode's TVDB ID, but the series' IMDB ID.
    /// </summary>
    [Fact]
    public async Task FindSonarrEpisodeIdByImdbAsync_WhenSeriesExists_ReturnsCorrectEpisodeId()
    {
        // Arrange - Series lookup
        var seriesJson = """
        {
            "data": [
                { "sonarrSeriesId": 88, "title": "Landman", "tvdbId": 397424, "imdbId": "tt14186672" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        // Arrange - Episode lookup
        var episodesJson = """
        {
            "data": [
                { "sonarrEpisodeId": 3732, "sonarrSeriesId": 88, "season": 2, "episode": 3, "title": "Almost a Home" },
                { "sonarrEpisodeId": 3733, "sonarrSeriesId": 88, "season": 2, "episode": 4, "title": "Dancing Rainbows" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodesJson);

        // Act - Find Landman S02E04 by IMDB ID
        var result = await _service.FindSonarrEpisodeIdByImdbAsync("tt14186672", season: 2, episode: 4);

        // Assert
        Assert.Equal(3733, result);
    }

    /// <summary>
    /// IMDB lookup should be case-insensitive.
    /// </summary>
    [Fact]
    public async Task FindSonarrEpisodeIdByImdbAsync_IsCaseInsensitive()
    {
        // Arrange
        var seriesJson = """
        {
            "data": [
                { "sonarrSeriesId": 88, "title": "Landman", "tvdbId": 397424, "imdbId": "tt14186672" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        var episodesJson = """
        {
            "data": [
                { "sonarrEpisodeId": 3733, "sonarrSeriesId": 88, "season": 2, "episode": 4, "title": "Dancing Rainbows" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodesJson);

        // Act - Search with uppercase
        var result = await _service.FindSonarrEpisodeIdByImdbAsync("TT14186672", season: 2, episode: 4);

        // Assert
        Assert.Equal(3733, result);
    }

    /// <summary>
    /// When series IMDB ID doesn't match any series, return null.
    /// </summary>
    [Fact]
    public async Task FindSonarrEpisodeIdByImdbAsync_WhenSeriesNotFound_ReturnsNull()
    {
        // Arrange - No matching series
        var seriesJson = """
        {
            "data": [
                { "sonarrSeriesId": 10, "title": "Breaking Bad", "tvdbId": 81189, "imdbId": "tt0903747" }
            ]
        }
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        // Act - Look for a different IMDB ID
        var result = await _service.FindSonarrEpisodeIdByImdbAsync("tt14186672", season: 1, episode: 1);

        // Assert
        Assert.Null(result);
        // Should only make one request (series lookup)
        Assert.Single(_mockHandler.CapturedRequests);
    }

    #endregion
}
