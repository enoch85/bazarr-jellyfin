using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// The plugin's configuration page in Jellyfin's dashboard, and what the saved settings do.
/// Every test leaves the configuration the way provision.sh set it.
/// </summary>
public class SettingsPageTests : JellyfinUiTest
{
    private const string PluginId = "72449d0e-7ba4-4ae7-a996-c00e6b06c8f8";

    private string _originalConfiguration = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _originalConfiguration = await Api.GetStringAsync($"Plugins/{PluginId}/Configuration");
    }

    public override async Task DisposeAsync()
    {
        using var content = new StringContent(_originalConfiguration, Encoding.UTF8, "application/json");
        (await Api.PostAsync($"Plugins/{PluginId}/Configuration", content)).EnsureSuccessStatusCode();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Plugin_IsLoadedAndActive()
    {
        using var plugins = await GetJsonAsync("Plugins");

        var plugin = Assert.Single(plugins.RootElement.EnumerateArray(), p => p.GetProperty("Name").GetString() == "Bazarr");
        Assert.Equal("Active", plugin.GetProperty("Status").GetString());
        Assert.Equal(Guid.Parse(PluginId), Guid.Parse(plugin.GetProperty("Id").GetString()!));
    }

    [Fact]
    public async Task Page_ShowsTheSavedConfiguration()
    {
        var page = await OpenSettingsAsync();

        await Assertions.Expect(page.Locator("#BazarrUrl")).ToHaveValueAsync(Bazarr.Url.TrimEnd('/'));
        await Assertions.Expect(page.Locator("#BazarrApiKey")).ToHaveValueAsync(FakeBazarr.ApiKey);
        await Assertions.Expect(page.Locator("#EnableForMovies")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#EnableForEpisodes")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#SearchTimeoutSeconds")).ToHaveValueAsync("25");
    }

    [Fact]
    public async Task Save_PersistsEveryField()
    {
        var page = await OpenSettingsAsync();

        await page.FillAsync("#BazarrUrl", "http://bazarr.example:6767");
        await page.FillAsync("#BazarrApiKey", "another-key");
        await page.Locator("label:has(#EnableForMovies)").ClickAsync();
        await page.Locator("label:has(#EnableForEpisodes)").ClickAsync();
        await page.FillAsync("#SearchTimeoutSeconds", "40");
        await page.Locator("#BazarrConfigForm button[type=submit]").ClickAsync();

        await WaitForConfigurationAsync(c =>
            c.GetProperty("BazarrUrl").GetString() == "http://bazarr.example:6767"
            && c.GetProperty("BazarrApiKey").GetString() == "another-key"
            && !c.GetProperty("EnableForMovies").GetBoolean()
            && !c.GetProperty("EnableForEpisodes").GetBoolean()
            && c.GetProperty("SearchTimeoutSeconds").GetInt32() == 40);
    }

    [Fact]
    public async Task TestConnection_ReportsASuccessfulConnection()
    {
        var page = await OpenSettingsAsync();

        await page.Locator("button:has-text('Test Connection')").ClickAsync();

        await Assertions.Expect(page.Locator("#TestConnectionResult"))
            .ToContainTextAsync("Connected successfully. 2 languages available.");
        Assert.Contains(Bazarr.Requests, r => r.PathAndQuery == "/api/system/languages" && r.ApiKey == FakeBazarr.ApiKey);
    }

    [Fact]
    public async Task TestConnection_ReportsARejectedApiKey()
    {
        var page = await OpenSettingsAsync();

        await page.FillAsync("#BazarrApiKey", "wrong-key");
        await page.Locator("button:has-text('Test Connection')").ClickAsync();

        await Assertions.Expect(page.Locator("#TestConnectionResult"))
            .ToContainTextAsync("Bazarr returned 401");
    }

    [Fact]
    public async Task TestConnection_ReportsAnUnreachableBazarr()
    {
        var page = await OpenSettingsAsync();

        await page.FillAsync("#BazarrUrl", "http://127.0.0.1:1");
        await page.Locator("button:has-text('Test Connection')").ClickAsync();

        await Assertions.Expect(page.Locator("#TestConnectionResult")).ToContainTextAsync("Connection failed");
    }

    [Fact]
    public async Task MoviesDisabled_SearchReturnsNothing()
    {
        await SetEnabledAsync(movies: false, episodes: true);

        var page = await SearchSubtitlesAsync(Env("JF_ITEM_OK"));

        await Assertions.Expect(page.Locator(".noSearchResults")).ToBeVisibleAsync();
        Assert.DoesNotContain(Bazarr.Requests, r => r.PathAndQuery.StartsWith("/api/providers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EpisodesDisabled_SearchReturnsNothing()
    {
        await SetEnabledAsync(movies: true, episodes: false);

        var page = await SearchSubtitlesAsync(await FindEpisodeAsync("Alpha Show [tvdbid-1001]"));

        await Assertions.Expect(page.Locator(".noSearchResults")).ToBeVisibleAsync();
        Assert.DoesNotContain(Bazarr.Requests, r => r.PathAndQuery.StartsWith("/api/providers", StringComparison.Ordinal));
    }

    private Task<IPage> OpenSettingsAsync() => BrowseAsync(async page =>
    {
        await page.GotoAsync($"{JellyfinUrl}/web/#/configurationpage?name=Bazarr");
        await page.Locator("#BazarrConfigForm").WaitForAsync();
        await Assertions.Expect(page.Locator("#BazarrUrl")).Not.ToHaveValueAsync(string.Empty);
    });

    private async Task SetEnabledAsync(bool movies, bool episodes)
    {
        using var config = JsonDocument.Parse(_originalConfiguration);
        var updated = config.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
        updated["EnableForMovies"] = movies;
        updated["EnableForEpisodes"] = episodes;

        using var content = new StringContent(JsonSerializer.Serialize(updated), Encoding.UTF8, "application/json");
        (await Api.PostAsync($"Plugins/{PluginId}/Configuration", content)).EnsureSuccessStatusCode();
    }

    private static async Task WaitForConfigurationAsync(Func<JsonElement, bool> matches)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            using var config = await GetJsonAsync($"Plugins/{PluginId}/Configuration");
            if (matches(config.RootElement))
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "The saved configuration never reached the server");
            await Task.Delay(300);
        }
    }
}
