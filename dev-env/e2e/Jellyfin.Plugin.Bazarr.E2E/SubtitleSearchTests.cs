using Microsoft.Playwright;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// Drives Jellyfin's real subtitle dialog in a browser against a real Jellyfin server
/// running the plugin. Run dev-env/e2e/run-e2e.sh - it provisions the server first.
/// </summary>
public class SubtitleSearchTests : IAsyncLifetime
{
    private static readonly string JellyfinUrl = Env("JF_URL");

    private FakeBazarr _bazarr = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        _bazarr = new FakeBazarr();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Args = ["--no-sandbox"] });
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        _bazarr.Dispose();
    }

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

    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is not set - run dev-env/e2e/run-e2e.sh");

    private async Task<IPage> SearchSubtitlesAsync(string itemId)
    {
        // An explicit locale is required: jellyfin-web calls toLocaleTimeString, which throws
        // on the POSIX locale a bare container reports, and the details page never renders.
        var context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            Locale = "en-US"
        });
        var page = await context.NewPageAsync();

        var diagnostics = new List<string>();
        page.Console += (_, m) => diagnostics.Add($"console.{m.Type}: {m.Text}");
        page.PageError += (_, e) => diagnostics.Add($"pageerror: {e}");
        page.Response += (_, r) => { if (r.Status >= 400) { diagnostics.Add($"http {r.Status}: {r.Url}"); } };

        try
        {
            await page.GotoAsync($"{JellyfinUrl}/web/#/login.html");
            await page.FillAsync("#txtManualName", Env("JF_USER"));
            await page.FillAsync("#txtManualPassword", Env("JF_PASS"));
            await page.Locator(".manualLoginForm button[type=submit]").ClickAsync();
            await page.WaitForURLAsync(u => !u.Contains("login.html", StringComparison.Ordinal));

            // Reload so the SPA boots straight into the details route rather than only
            // changing the hash of the page it is already on.
            await page.GotoAsync($"{JellyfinUrl}/web/#/details?id={itemId}");
            await page.ReloadAsync();

            await page.Locator("button.btnMoreCommands:not(.hide)").First.ClickAsync();
            await page.Locator("button.actionSheetMenuItem[data-id=editsubtitles]").ClickAsync();
            await page.Locator("#selectLanguage").WaitForAsync();

            await page.SelectOptionAsync("#selectLanguage", "eng");
            await page.Locator(".btnSearchSubtitles").ClickAsync();

            // Bazarr searches are slow by design; the plugin's own timeout is 25s.
            await page.Locator(".subtitleResults .listItem, .noSearchResults:visible")
                .First.WaitForAsync(new() { Timeout = 60_000 });
        }
        catch
        {
            await page.ScreenshotAsync(new() { Path = "failure.png", FullPage = true });
            await File.WriteAllTextAsync("failure.html", await page.ContentAsync());
            await File.WriteAllLinesAsync("failure.log", diagnostics);
            throw;
        }

        return page;
    }
}
