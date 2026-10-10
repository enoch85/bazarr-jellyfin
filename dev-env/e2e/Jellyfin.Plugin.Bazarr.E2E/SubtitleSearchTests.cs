using Microsoft.Playwright;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// Drives Jellyfin's real subtitle dialog in a browser against a real Jellyfin server
/// running the plugin. Run dev-env/e2e/run-e2e.sh - it provisions the server first.
/// </summary>
public class SubtitleSearchTests : JellyfinUiTest
{
    /// <summary>
    /// Issue #9: Bazarr answers a throttled search with 500. Jellyfin's SubtitleManager
    /// swallows provider exceptions, so before the fix the user saw "no results found"
    /// and the reason only appeared in the server log.
    /// </summary>
    [Fact]
    public async Task ThrottledBazarr_ShowsTheReasonInTheSubtitleDialog()
    {
        var page = await SearchSubtitlesAsync(Env("JF_ITEM_THROTTLED"));

        var results = page.Locator(".subtitleResults");
        await Assertions.Expect(results).ToContainTextAsync("Bazarr");
        await Assertions.Expect(results).ToContainTextAsync("Search failed");
        await Assertions.Expect(results).ToContainTextAsync("All providers are throttled");
        await Assertions.Expect(page.Locator(".noSearchResults")).Not.ToBeVisibleAsync();

        await page.ScreenshotAsync(new() { Path = "issue-9-throttled.png" });
    }

    /// <summary>
    /// The status row must not displace real results when Bazarr answers normally.
    /// </summary>
    [Fact]
    public async Task WorkingBazarr_ShowsTheSubtitle()
    {
        var page = await SearchSubtitlesAsync(Env("JF_ITEM_OK"));

        var results = page.Locator(".subtitleResults");
        await Assertions.Expect(results).ToContainTextAsync("Inception.2010.1080p.BluRay.x264");
        await Assertions.Expect(results).ToContainTextAsync("opensubtitlescom");
        await Assertions.Expect(results).Not.ToContainTextAsync("Search failed");
    }

    /// <summary>
    /// Issue #15: Bazarr flags hearing-impaired and forced results, and the plugin already
    /// sends those flags back on download, but the dialog gave no hint which row was which.
    /// </summary>
    [Fact]
    public async Task HearingImpairedAndForced_AreBadgedOnTheirOwnRows()
    {
        var page = await SearchSubtitlesAsync(Env("JF_ITEM_OK"));

        var regular = page.Locator(".subtitleResults .listItem", new() { HasText = "Inception.2010.1080p.BluRay.x264" });
        var hi = page.Locator(".subtitleResults .listItem", new() { HasText = "Inception.2010.720p.WEB.SDH" });
        var forced = page.Locator(".subtitleResults .listItem", new() { HasText = "Inception.2010.720p.WEB.FORCED" });

        await Assertions.Expect(hi.Locator(".subtitleFeaturePillow")).ToHaveTextAsync("HI/SDH");
        await Assertions.Expect(forced.Locator(".subtitleFeaturePillow")).ToHaveTextAsync("Forced/Foreign parts only");
        await Assertions.Expect(regular.Locator(".subtitleFeaturePillow")).ToHaveTextAsync("Perfect match");

        await page.ScreenshotAsync(new() { Path = "issue-15-hi-forced.png" });
    }
}
