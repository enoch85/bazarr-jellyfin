using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Playwright;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// A real Jellyfin server with the plugin installed (dev-env/e2e/provision.sh), a browser, and a
/// <see cref="FakeBazarr"/> for the plugin to talk to.
/// </summary>
public abstract class JellyfinUiTest : IAsyncLifetime
{
    protected static readonly string JellyfinUrl = Env("JF_URL");

    protected static readonly HttpClient Api = CreateApiClient();

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    protected FakeBazarr Bazarr { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        Bazarr = new FakeBazarr(mediaRoot: Env("JF_MEDIA"));
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Args = ["--no-sandbox"] });
    }

    public virtual async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        Bazarr.Dispose();
    }

    protected static string Env(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is not set - run dev-env/e2e/run-e2e.sh");

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(JellyfinUrl + "/"), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{Env("JF_TOKEN")}\"");
        return client;
    }

    /// <summary>Logs in, then runs <paramref name="steps"/>; a failure leaves a screenshot, the HTML and a log behind.</summary>
    protected async Task<IPage> BrowseAsync(Func<IPage, Task> steps)
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

            await steps(page);
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

    protected Task<IPage> SearchSubtitlesAsync(string itemId) => BrowseAsync(async page =>
    {
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
    });

    /// <summary>The id of the episode whose file lives under <paramref name="showFolder"/>.</summary>
    protected static async Task<string> FindEpisodeAsync(string showFolder)
    {
        using var items = await GetJsonAsync("Items?IncludeItemTypes=Episode&Recursive=true&Fields=Path");
        return items.RootElement.GetProperty("Items").EnumerateArray()
            .Single(i => i.GetProperty("Path").GetString()!.Contains($"/{showFolder}/", StringComparison.Ordinal))
            .GetProperty("Id").GetString()!;
    }

    /// <summary>The external subtitle streams Jellyfin currently knows for an item.</summary>
    protected static async Task<IReadOnlyList<string>> ExternalSubtitlesAsync(string itemId)
    {
        using var items = await GetJsonAsync($"Items?Ids={itemId}&Fields=MediaStreams");
        var streams = items.RootElement.GetProperty("Items")[0].GetProperty("MediaStreams").EnumerateArray();
        return [.. streams
            .Where(s => s.GetProperty("Type").GetString() == "Subtitle" && s.GetProperty("IsExternal").GetBoolean())
            .Select(s => s.GetProperty("Path").GetString()!)];
    }

    protected static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Api.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }
}
