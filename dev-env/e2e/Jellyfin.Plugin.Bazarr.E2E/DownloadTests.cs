using System.Text.Json;
using Microsoft.Playwright;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// Bazarr writes the subtitle into the media folder itself, so the plugin has nothing to stream
/// back and deliberately fails Jellyfin's own save (see AGENTS.md). Jellyfin runs the download
/// behind a 204 and only logs the failure, so what counts is Bazarr's request, the file, the
/// refresh and what Jellyfin logged.
/// </summary>
public class DownloadTests : JellyfinUiTest
{
    private const string MovieDir = "movies/Memento (2000) [imdbid-tt0209144]";
    private const string EpisodeDir = "shows/Hotel Show [tvdbid-8008]/Season 02";

    [Fact]
    public async Task Movie_DownloadIsRequestedFromBazarr_SavedBesideTheFile_AndPickedUpByJellyfin()
    {
        var itemId = Env("JF_ITEM_DOWNLOAD");
        var folder = await FreshFolderAsync(itemId, MovieDir);

        var log = LogMark();
        await DownloadFromDialogAsync(itemId, "Memento.2000.1080p.BluRay.x264.OK");

        await AssertDownloadedAsync(itemId, folder, "Memento.2000.1080p.BluRay.x264.en.srt", log);
        var post = Assert.Single(Bazarr.Requests, r => r.Method == "POST");
        Assert.Equal("/api/providers/movies", post.PathAndQuery);
        Assert.Equal(FakeBazarr.ApiKey, post.ApiKey);
        using var body = JsonDocument.Parse(post.Body);
        Assert.Equal(3, body.RootElement.GetProperty("radarrid").GetInt32());
        Assert.Equal("opensubtitlescom", body.RootElement.GetProperty("provider").GetString());
        Assert.Equal("pickled-download", body.RootElement.GetProperty("subtitle").GetString());
    }

    [Fact]
    public async Task Episode_DownloadIsRequestedFromBazarr_SavedBesideTheFile_AndPickedUpByJellyfin()
    {
        var itemId = await FindEpisodeAsync("Hotel Show [tvdbid-8008]");
        var folder = await FreshFolderAsync(itemId, EpisodeDir);

        var log = LogMark();
        await DownloadFromDialogAsync(itemId, "Hotel.Reference.S02E05.WEB.OK");

        await AssertDownloadedAsync(itemId, folder, "Hotel Show S02E05.en.srt", log);
        var post = Assert.Single(Bazarr.Requests, r => r.Method == "POST");
        Assert.Equal("/api/providers/episodes", post.PathAndQuery);
        using var body = JsonDocument.Parse(post.Body);
        Assert.Equal(85, body.RootElement.GetProperty("episodeid").GetInt32());
        Assert.Equal(8, body.RootElement.GetProperty("seriesid").GetInt32());
        Assert.Equal("pickled-download", body.RootElement.GetProperty("subtitle").GetString());
    }

    [Fact]
    public async Task BazarrRefusingTheDownload_ReportsItAndSavesNothing()
    {
        var itemId = Env("JF_ITEM_DOWNLOAD");
        var folder = await FreshFolderAsync(itemId, MovieDir);

        var log = LogMark();
        await DownloadFromDialogAsync(itemId, "Memento.2000.1080p.BluRay.x264.REFUSED");

        await WaitForLogAsync(log, "Failed to download subtitle from Bazarr");
        Assert.Single(Bazarr.Requests, r => r.Method == "POST");
        Assert.Empty(Directory.GetFiles(folder, "*.srt"));
        Assert.Empty(await ExternalSubtitlesAsync(itemId));
    }

    [Fact]
    public async Task StatusRows_CannotBeDownloaded()
    {
        // The throttled movie is answered with a status row; its id is the one Jellyfin hands the plugin.
        var itemId = Env("JF_ITEM_THROTTLED");
        var folder = Path.Combine(Env("JF_MEDIA"), "movies/The Matrix (1999) [imdbid-tt0133093]");
        using var search = await GetJsonAsync($"Items/{itemId}/RemoteSearch/Subtitles/eng");
        var row = Assert.Single(search.RootElement.EnumerateArray());
        Assert.StartsWith("Search failed", row.GetProperty("Name").GetString(), StringComparison.Ordinal);

        var log = LogMark();
        using var response = await Api.PostAsync($"Items/{itemId}/RemoteSearch/Subtitles/{row.GetProperty("Id").GetString()}", null);
        response.EnsureSuccessStatusCode();

        await WaitForLogAsync(log, "This entry is a status message, not a subtitle");
        Assert.DoesNotContain(Bazarr.Requests, r => r.Method == "POST");
        Assert.Empty(Directory.GetFiles(folder, "*.srt"));
    }

    /// <summary>Removes any subtitle an earlier run left, and waits until Jellyfin has forgotten it too.</summary>
    private static async Task<string> FreshFolderAsync(string itemId, string relativeDir)
    {
        var folder = Path.Combine(Env("JF_MEDIA"), relativeDir);
        foreach (var file in Directory.GetFiles(folder, "*.srt"))
        {
            File.Delete(file);
        }

        using var refresh = await Api.PostAsync($"Items/{itemId}/Refresh?MetadataRefreshMode=Default", null);
        refresh.EnsureSuccessStatusCode();
        await WaitForAsync(async () => (await ExternalSubtitlesAsync(itemId)).Count == 0, "Jellyfin to drop the old subtitle");
        return folder;
    }

    /// <summary>Clicks the row's download button in the real dialog and waits for Jellyfin to accept it.</summary>
    private async Task DownloadFromDialogAsync(string itemId, string release)
    {
        var page = await SearchSubtitlesAsync(itemId);

        var download = page.Locator(".subtitleResults .listItem", new() { HasText = release }).Locator("button.btnDownload");
        var response = await page.RunAndWaitForResponseAsync(
            () => download.ClickAsync(),
            r => r.Url.Contains("/RemoteSearch/Subtitles/", StringComparison.Ordinal) && r.Request.Method == "POST");

        Assert.True(response.Ok);
    }

    private async Task AssertDownloadedAsync(string itemId, string folder, string expectedFile, long log)
    {
        // The plugin fails Jellyfin's save on purpose once Bazarr has the subtitle.
        await WaitForLogAsync(log, "Subtitle downloaded successfully by Bazarr");

        // Only Bazarr's file may exist: Jellyfin would have saved an empty one had the plugin returned a stream.
        var saved = Assert.Single(Directory.GetFiles(folder, "*.srt"));
        Assert.Equal(expectedFile, Path.GetFileName(saved));
        Assert.Contains("Hello from Bazarr", await File.ReadAllTextAsync(saved), StringComparison.Ordinal);

        // The post-download refresh is what makes the file show up without a library scan.
        await WaitForAsync(async () => (await ExternalSubtitlesAsync(itemId)).Count == 1, "Jellyfin to pick up the subtitle");
        Assert.Equal(saved, Assert.Single(await ExternalSubtitlesAsync(itemId)));
    }

    private static long LogMark() => new FileInfo(Env("JF_LOG")).Length;

    private static Task WaitForLogAsync(long mark, string text) => WaitForAsync(async () =>
    {
        using var stream = new FileStream(Env("JF_LOG"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(mark, SeekOrigin.Begin);
        return (await new StreamReader(stream).ReadToEndAsync()).Contains(text, StringComparison.Ordinal);
    }, $"Jellyfin to log '{text}'");

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}");
            await Task.Delay(500);
        }
    }
}
