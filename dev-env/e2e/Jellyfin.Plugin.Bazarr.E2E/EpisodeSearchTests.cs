using Microsoft.Playwright;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// Bazarr stores no episode-level IDs, so the plugin finds an episode through a fallback chain.
/// Each series in provision.sh is only findable by one step of it, so a pass proves that step.
/// </summary>
public class EpisodeSearchTests : JellyfinUiTest
{
    [Theory]
    // Series TVDB ID, from the folder name.
    [InlineData("Alpha Show [tvdbid-1001]", "Alpha.Reference.S01E01.WEB")]
    // The folder's TVDB ID is unknown to Bazarr, so the series IMDB ID has to carry it.
    [InlineData("Bravo Show [tvdbid-1999] [imdbid-tt2000002]", "Bravo.Reference.S01E01.WEB")]
    // Episode-level IDs, from the episode NFO. Bazarr only knows series, so these work by collision.
    [InlineData("Charlie Show", "Charlie.Reference.S01E01.WEB")]
    [InlineData("Delta Show", "Delta.Reference.S01E01.WEB")]
    // Jellyfin calls the series "Echo Lokal"; Bazarr only knows its original title.
    [InlineData("Echo Show", "Echo.Original.S01E01.WEB")]
    // No IDs at all: the title alone.
    [InlineData("Foxtrot Show", "Foxtrot.Show.S01E01.WEB")]
    // Bazarr lists the series under a different title, with ours as an alternative.
    [InlineData("Juliet Show", "Zulu.Title.S01E01.WEB")]
    public async Task Episode_IsFoundThroughItsStepOfTheLookupChain(string showFolder, string release)
    {
        var page = await SearchSubtitlesAsync(await FindEpisodeAsync(showFolder));

        var results = page.Locator(".subtitleResults");
        await Assertions.Expect(results).ToContainTextAsync(release);
        await Assertions.Expect(results).Not.ToContainTextAsync("Search failed");
    }

    [Fact]
    public async Task EpisodeUnknownToBazarr_ShowsNoResults()
    {
        var page = await SearchSubtitlesAsync(await FindEpisodeAsync("Golf Show"));

        await Assertions.Expect(page.Locator(".noSearchResults")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".subtitleResults .listItem")).ToHaveCountAsync(0);
    }
}
