using System.Net;
using System.Text;

namespace Jellyfin.Plugin.Bazarr.E2E;

/// <summary>
/// Stands in for Bazarr so error paths can be reproduced on demand.
/// Replays Bazarr's exact wire format: flask-restx serialises <c>return 'msg', 500</c>
/// as a JSON-encoded string with content type application/json
/// (bazarr/api/providers/providers_movies.py, libs/flask_restx/representations.py).
///
/// Each scenario gets its own movie so the plugin's one-hour result cache cannot leak
/// between tests.
/// </summary>
public sealed class FakeBazarr : IDisposable
{
    private readonly HttpListener _listener = new();

    public FakeBazarr(int port = 16767)
    {
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string Url { get; }

    public void Dispose()
    {
        _listener.Close();
    }

    // Bazarr's /api/movies model exposes imdbId and no TMDB ID at all.
    private static (int Status, string Body) Respond(string path, string query) => (path, query) switch
    {
        ("/api/movies", _) => (200, """
            {"data":[
              {"radarrId":1,"title":"The Matrix","imdbId":"tt0133093","path":"/movies/The Matrix (1999)/matrix.mkv"},
              {"radarrId":2,"title":"Inception","imdbId":"tt1375666","path":"/movies/Inception (2010)/inception.mkv"}
            ],"total":2}
            """),
        ("/api/providers/movies", "radarrid=1") => (500, "\"All providers are throttled\"\n"),
        ("/api/providers/movies", "radarrid=2") => (200, """
            {"data":[{"provider":"opensubtitlescom","subtitle":"pickled","language":"en","score":98,
              "release_info":["Inception.2010.1080p.BluRay.x264"],"matches":["hash"],
              "original_format":"False","hearing_impaired":"False","forced":"False","uploader":"someone"}]}
            """),
        _ => (404, "\"unhandled\"\n")
    };

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
            var (status, body) = Respond(url.AbsolutePath, url.Query.TrimStart('?'));
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }
}
