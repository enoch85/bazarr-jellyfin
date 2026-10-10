# End-to-end tests

Playwright tests that drive Jellyfin's real subtitle dialog in a browser, against a real
Jellyfin server with the plugin installed.

```bash
./run-e2e.sh                    # provision + run
SKIP_PROVISION=1 ./run-e2e.sh   # re-run against an already provisioned server
```

`provision.sh` downloads Jellyfin (12.1 by default, into `/opt/jf`), builds and installs
the plugin, resets the server's data directory, runs the startup wizard, seeds two movies and
points the plugin at Bazarr. `run-e2e.sh` then installs the Chromium build Playwright ships
with and runs the tests.

## What is faked, and why

Bazarr itself is replaced by `FakeBazarr`, which replays Bazarr's exact wire format - a
JSON-encoded string body with content type `application/json`, which is how flask-restx
serialises `return 'All providers are throttled', 500` in
`bazarr/api/providers/providers_movies.py`. Error paths like provider throttling cannot be
triggered on demand in a real Bazarr, and that is precisely what these tests cover.

Each scenario has its own movie or episode, because the plugin caches search results for an
hour. `FakeBazarr` also writes the subtitle beside the media file on a download, as Bazarr does,
and records every request it receives.

| Item                                   | Bazarr id   | Response / what it covers                                 |
|----------------------------------------|-------------|-----------------------------------------------------------|
| The Matrix (1999), tt0133093           | radarr 1    | 500 `All providers are throttled`                         |
| Inception (2010), tt1375666            | radarr 2    | English subtitles, plain / HI / forced                    |
| Memento (2000), tt0209144              | radarr 3    | downloadable subtitle, and one Bazarr refuses to fetch    |
| Alpha - Foxtrot, Juliet Show           | sonarr 1-7  | one step of the episode lookup chain each (see below)     |
| Golf Show                              | -           | not in Bazarr: no results                                 |
| Hotel Show S02E05                      | sonarr 8    | episode download and refresh                              |

The episode lookup chain is series TVDB (folder name), series IMDB (folder name), episode TVDB
and IMDB (episode NFO), original title (`tvshow.nfo`), title, then Bazarr's alternative titles.
The Bazarr title of the ID-matched series deliberately differs from the Jellyfin one, so only the
ID step can find it.

Besides the subtitle dialog the tests cover downloads (Bazarr's request, the saved file, the
post-download refresh, the failure paths), the plugin's configuration page (load, save, Test
Connection) and the movie/episode switches.

To run against a real Bazarr instead, set `BAZARR_URL` before provisioning.

## Requirements

`dotnet`, `curl` and `perl` must already be present. `ffmpeg` and the shared libraries
Playwright's Chromium needs are installed by `provision.sh` itself when they are missing and
`apt-get` is available - it uses `sudo` if it is not already running as root. Set `SKIP_DEPS=1`
to manage them yourself, which is what you want on a non-Debian machine:

```bash
apt-get install -y ffmpeg libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libatspi2.0-0 \
    libcups2 libxcomposite1 libxdamage1 libxkbcommon0 libpango-1.0-0 libcairo2
```

Inside a sandboxed container apt often cannot drop privileges to its `_apt` user, so a plain
install fails with `Failed to setgroups`. `provision.sh` retries such a failure with apt's own
sandbox disabled and the download cache redirected somewhere writable.

Failures write `failure.png`, `failure.html` and `failure.log` (browser console, page errors
and any HTTP 4xx/5xx) next to the test binary.
