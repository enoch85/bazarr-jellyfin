# End-to-end tests

Playwright tests that drive Jellyfin's real subtitle dialog in a browser, against a real
Jellyfin server with the plugin installed.

```bash
./run-e2e.sh                    # provision + run
SKIP_PROVISION=1 ./run-e2e.sh   # re-run against an already provisioned server
```

`provision.sh` downloads Jellyfin (10.11.11 by default, into `/opt/jf`), builds and installs
the plugin, resets the server's data directory, runs the startup wizard, seeds two movies and
points the plugin at Bazarr. `run-e2e.sh` then installs the Chromium build Playwright ships
with and runs the tests.

## What is faked, and why

Bazarr itself is replaced by `FakeBazarr`, which replays Bazarr's exact wire format - a
JSON-encoded string body with content type `application/json`, which is how flask-restx
serialises `return 'All providers are throttled', 500` in
`bazarr/api/providers/providers_movies.py`. Error paths like provider throttling cannot be
triggered on demand in a real Bazarr, and that is precisely what these tests cover.

Each scenario has its own movie, because the plugin caches search results for an hour:

| Movie                          | Bazarr `radarrId` | Response                          |
|--------------------------------|-------------------|-----------------------------------|
| The Matrix (1999), tt0133093   | 1                 | 500 `All providers are throttled` |
| Inception (2010), tt1375666    | 2                 | one English subtitle              |

To run against a real Bazarr instead, set `BAZARR_URL` before provisioning.

## Requirements

`dotnet`, `curl`, `perl`, `ffmpeg`, and Chromium's shared libraries:

```bash
apt-get install -y ffmpeg libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libatspi2.0-0 \
    libcups2 libxcomposite1 libxdamage1 libxkbcommon0 libpango-1.0-0 libcairo2
```

Failures write `failure.png`, `failure.html` and `failure.log` (browser console, page errors
and any HTTP 4xx/5xx) next to the test binary.
