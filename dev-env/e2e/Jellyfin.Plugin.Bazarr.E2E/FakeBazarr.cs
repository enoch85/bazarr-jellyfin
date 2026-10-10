using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// Stands in for Bazarr so error paths can be reproduced on demand.
/// Replays Bazarr's exact wire format: flask-restx serialises <c>return 'msg', 500</c>
/// as a JSON-encoded string with content type application/json
/// (bazarr/api/providers/providers_movies.py, libs/flask_restx/representations.py).
///
/// Each scenario gets its own movie or episode so the plugin's one-hour result cache cannot
/// leak between tests.
///
/// Like Bazarr, a download writes the subtitle next to the media file itself and answers 204;
/// the file location comes from the table below, never from the request.
/// </summary>
public sealed class FakeBazarr : IDisposable
{
    public const string ApiKey = "devkey123456789012345";

    private const string SubtitleText = "1\n00:00:00,000 --> 00:00:01,000\nHello from Bazarr\n";

    // Series are matched by the plugin's lookup chain in this order, one series per step - and a title
    // that cannot match the Jellyfin name wherever an ID step is meant to carry the lookup:
    // series TVDB, series IMDB, episode TVDB, episode IMDB, original title, title, alternative title.
    private static readonly SeriesEntry[] Series =
    [
        new(1, "Alpha Reference", 1001, null, [], 1, 1),
        new(2, "Bravo Reference", 2002, "tt2000002", [], 1, 1),
        new(3, "Charlie Reference", 3003, null, [], 1, 1),
        new(4, "Delta Reference", null, "tt4000004", [], 1, 1),
        new(5, "Echo Original", null, null, [], 1, 1),
        new(6, "Foxtrot Show", null, null, [], 1, 1),
        new(7, "Zulu Title", null, null, ["Juliet Show"], 1, 1),
        new(8, "Hotel Reference", 8008, null, [], 2, 5)
    ];

    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly string? _mediaRoot;

    public FakeBazarr(int port = 16767, string? mediaRoot = null)
    {
        _mediaRoot = mediaRoot;
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string Url { get; }

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    public void Dispose()
    {
        _listener.Close();
    }

    // Bazarr's /api/movies model exposes imdbId and no TMDB ID at all.
    private (int Status, string Body) Respond(string method, string path, string query, string body)
    {
        if (method == "POST")
        {
            return Download(path, body);
        }

        return (path, query) switch
        {
            ("/api/system/languages", _) => (200, """
                [{"code2":"en","code3":"eng","name":"English","enabled":true},
                 {"code2":"sv","code3":"swe","name":"Swedish","enabled":true}]
                """),
            ("/api/movies", _) => (200, """
                {"data":[
                  {"radarrId":1,"title":"The Matrix","imdbId":"tt0133093","path":"/movies/The Matrix (1999)/matrix.mkv"},
                  {"radarrId":2,"title":"Inception","imdbId":"tt1375666","path":"/movies/Inception (2010)/inception.mkv"},
                  {"radarrId":3,"title":"Memento","imdbId":"tt0209144","path":"/movies/Memento (2000)/memento.mkv"}
                ],"total":3}
                """),
            ("/api/providers/movies", "radarrid=1") => (500, "\"All providers are throttled\"\n"),
            ("/api/providers/movies", "radarrid=2") => (200, """
                {"data":[{"provider":"opensubtitlescom","subtitle":"pickled","language":"en","score":98,
                  "release_info":["Inception.2010.1080p.BluRay.x264"],"matches":["hash"],
                  "original_format":"False","hearing_impaired":"False","forced":"False","uploader":"someone"},
                 {"provider":"opensubtitlescom","subtitle":"pickled-hi","language":"en","score":90,
                  "release_info":["Inception.2010.720p.WEB.SDH"],"matches":[],
                  "original_format":"False","hearing_impaired":"True","forced":"False","uploader":"someone"},
                 {"provider":"opensubtitlescom","subtitle":"pickled-forced","language":"en","score":85,
                  "release_info":["Inception.2010.720p.WEB.FORCED"],"matches":[],
                  "original_format":"False","hearing_impaired":"False","forced":"True","uploader":"someone"}]}
                """),
            ("/api/providers/movies", "radarrid=3") => (200, Options("Memento.2000.1080p.BluRay.x264")),
            ("/api/series", _) => (200, Json(new { data = Series.Select(x => x.ToJson()), total = Series.Length })),
            ("/api/providers/episodes", _) when EpisodeFromQuery(query) is { } ep =>
                (200, Options($"{ep.Series.Title.Replace(' ', '.')}.S{ep.Season:D2}E{ep.Episode:D2}.WEB")),
            ("/api/episodes", _) when SeriesFromQuery(query) is { } show => (200, Json(new
            {
                data = new[]
                {
                    new
                    {
                        sonarrEpisodeId = show.EpisodeId, sonarrSeriesId = show.Id, season = show.Season,
                        episode = show.Episode, title = "Pilot"
                    }
                },
                total = 1
            })),
            _ => (404, "\"unhandled\"\n")
        };
    }

    // A good subtitle and one Bazarr refuses to fetch, so both download outcomes can be driven.
    private static string Options(string release) => Json(new
    {
        data = new[]
        {
            Option("pickled-download", release + ".OK", "hash"),
            Option("pickled-unavailable", release + ".REFUSED")
        }
    });

    private static object Option(string subtitle, string release, params string[] matches) => new
    {
        provider = "opensubtitlescom", subtitle, language = "en", score = 90,
        release_info = new[] { release }, matches, original_format = "False",
        hearing_impaired = "False", forced = "False", uploader = "someone"
    };

    private (int Status, string Body) Download(string path, string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var v) ? v.ToString() : null;

        // Only the subtitle FakeBazarr offered can be fetched; anything else is a Bazarr refusal.
        if (Text("subtitle") != "pickled-download")
        {
            return (500, "\"Unable to download subtitle\"\n");
        }

        // The ids sent must be the real ones, and in the form the plugin documents: Bazarr's
        // hi/forced/original_format are the strings "True"/"False".
        string target;
        switch (path)
        {
            case "/api/providers/movies" when Text("radarrid") == "3":
                target = "movies/Memento (2000) [imdbid-tt0209144]/Memento.2000.1080p.BluRay.x264";
                break;
            case "/api/providers/episodes" when Text("episodeid") == "85" && Text("seriesid") == "8":
                target = "shows/Hotel Show [tvdbid-8008]/Season 02/Hotel Show S02E05";
                break;
            default:
                return (400, "\"Unknown item\"\n");
        }

        if (Text("provider") != "opensubtitlescom" || Text("hi") != "False"
            || Text("forced") != "False" || Text("original_format") != "False")
        {
            return (400, "\"Bad download request\"\n");
        }

        if (_mediaRoot is not null)
        {
            File.WriteAllText(Path.Combine(_mediaRoot, target + ".en.srt"), SubtitleText);
        }

        return (204, string.Empty);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static SeriesEntry? SeriesFromQuery(string query) =>
        Series.FirstOrDefault(x => Uri.UnescapeDataString(query) == $"seriesid[]={x.Id}");

    private static (SeriesEntry Series, int Season, int Episode)? EpisodeFromQuery(string query) =>
        Series.Where(x => query == $"episodeid={x.EpisodeId}")
            .Select(x => ((SeriesEntry, int, int)?)(x, x.Season, x.Episode)).FirstOrDefault();

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            var url = ctx.Request.Url!;
            string requestBody;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            _requests.Enqueue(new(ctx.Request.HttpMethod, url.PathAndQuery, ctx.Request.Headers["X-API-KEY"], requestBody));

            var (status, body) = ctx.Request.Headers["X-API-KEY"] == ApiKey
                ? Respond(ctx.Request.HttpMethod, url.AbsolutePath, url.Query.TrimStart('?'), requestBody)
                : (401, "\"Unauthorized\"\n");
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }
}

public sealed record RecordedRequest(string Method, string PathAndQuery, string? ApiKey, string Body);

internal sealed record SeriesEntry(
    int Id, string Title, int? TvdbId, string? ImdbId, string[] AlternativeTitles, int Season, int Episode)
{
    // One episode per series, with an id that cannot be confused with the series id.
    public int EpisodeId => (Id * 10) + Episode;

    public object ToJson() => new
    {
        sonarrSeriesId = Id, title = Title, tvdbId = TvdbId, imdbId = ImdbId, alternativeTitles = AlternativeTitles,
        path = $"/tv/{Title}"
    };
}
