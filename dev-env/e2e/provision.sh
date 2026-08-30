#!/usr/bin/env bash
# Provisions a real Jellyfin server with the Bazarr plugin installed, ready for the
# Playwright end-to-end tests. Idempotent: re-running resets Jellyfin's data directory.
set -euo pipefail

JF_ROOT="${JF_ROOT:-/opt/jf}"
JF_URL="${JF_URL:-http://127.0.0.1:8096}"
JF_VERSION="${JF_VERSION:-10.11.11}"
JF_USER=dev
JF_PASS=dev123
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

log() { echo "[provision] $*"; }
api() { curl -sS --max-time 60 -H "Authorization: MediaBrowser Token=\"$TOKEN\"" "$@"; }

# --- 1. Jellyfin server + web UI -------------------------------------------------
if [ ! -x "$JF_ROOT/jellyfin/jellyfin" ]; then
    log "downloading Jellyfin $JF_VERSION"
    mkdir -p "$JF_ROOT"
    curl -sSL -o "$JF_ROOT/jellyfin.tar.gz" \
        "https://repo.jellyfin.org/files/server/linux/latest-stable/amd64/jellyfin_${JF_VERSION}-amd64.tar.gz"
    tar xzf "$JF_ROOT/jellyfin.tar.gz" -C "$JF_ROOT"
    rm -f "$JF_ROOT/jellyfin.tar.gz"
fi

command -v ffmpeg >/dev/null || { log "ffmpeg is required (apt-get install ffmpeg)"; exit 1; }

# --- 2. Media ---------------------------------------------------------------------
# The IMDB ID in the folder name is what Bazarr matches on - Bazarr's /api/movies
# response exposes imdbId and nothing else. One movie per scenario, so the plugin's
# one-hour result cache cannot leak between tests.
make_movie() {
    local dir="$JF_ROOT/media/movies/$1" file="$2"
    [ -f "$dir/$file" ] && return 0
    mkdir -p "$dir"
    ffmpeg -y -loglevel error -f lavfi -i testsrc=size=320x180:rate=5:duration=2 \
        -c:v libx264 -preset ultrafast "$dir/$file"
}
log "creating test media"
make_movie "The Matrix (1999) [imdbid-tt0133093]" "The.Matrix.1999.1080p.BluRay.x264.mkv"
make_movie "Inception (2010) [imdbid-tt1375666]" "Inception.2010.1080p.BluRay.x264.mkv"

# --- 3. Plugin --------------------------------------------------------------------
log "building plugin"
dotnet publish "$REPO_ROOT/Jellyfin.Plugin.Bazarr/Jellyfin.Plugin.Bazarr.csproj" \
    -c Release -o "$JF_ROOT/plugin-build" --nologo -v q

stop_jellyfin() {
    for p in /proc/[0-9]*; do
        case "$( { tr '\0' ' ' < "$p/cmdline"; } 2>/dev/null )" in
            *"jellyfin --datadir $JF_ROOT/data"*) kill "${p#/proc/}" 2>/dev/null || true ;;
        esac
    done
    for _ in $(seq 1 30); do
        curl -s -o /dev/null --max-time 1 "$JF_URL/System/Info/Public" || return 0
        sleep 1
    done
}
stop_jellyfin
rm -rf "$JF_ROOT/data" "$JF_ROOT/cache"
mkdir -p "$JF_ROOT/data/plugins/Bazarr_1.0.0.0"
cp "$JF_ROOT/plugin-build/Jellyfin.Plugin.Bazarr.dll" "$JF_ROOT/data/plugins/Bazarr_1.0.0.0/"

# --- 4. Start ---------------------------------------------------------------------
log "starting Jellyfin"
(cd "$JF_ROOT/jellyfin" && nohup ./jellyfin \
    --datadir "$JF_ROOT/data" --cachedir "$JF_ROOT/cache" \
    --webdir "$JF_ROOT/jellyfin/jellyfin-web" --ffmpeg "$(command -v ffmpeg)" \
    > "$JF_ROOT/jellyfin.log" 2>&1 &)

# /System/Info/Public answers from the setup app while the server is still booting;
# /Startup/User only returns 200 once the real host is up.
for _ in $(seq 1 120); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$JF_URL/Startup/User" || true)" = "200" ] && break
    sleep 1
done
grep -q "Loaded assembly Jellyfin.Plugin.Bazarr" "$JF_ROOT/jellyfin.log" || { log "plugin did not load"; exit 1; }

# --- 5. Startup wizard ------------------------------------------------------------
log "running startup wizard"
curl -sS --max-time 30 -o /dev/null "$JF_URL/Startup/User"   # creates the default user
curl -sS --max-time 30 -o /dev/null -X POST "$JF_URL/Startup/Configuration" -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
curl -sS --max-time 30 -o /dev/null -X POST "$JF_URL/Startup/User" -H 'Content-Type: application/json' \
    -d "{\"Name\":\"$JF_USER\",\"Password\":\"$JF_PASS\"}"
curl -sS --max-time 30 -o /dev/null -X POST "$JF_URL/Startup/RemoteAccess" -H 'Content-Type: application/json' \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
curl -sS --max-time 30 -o /dev/null -X POST "$JF_URL/Startup/Complete"

TOKEN=$(curl -sS --max-time 30 -X POST "$JF_URL/Users/AuthenticateByName" -H 'Content-Type: application/json' \
    -H 'Authorization: MediaBrowser Client="e2e", Device="cli", DeviceId="e2e-cli", Version="1.0"' \
    -d "{\"Username\":\"$JF_USER\",\"Pw\":\"$JF_PASS\"}" | grep -oE '"AccessToken":"[^"]+"' | cut -d'"' -f4)
[ -n "$TOKEN" ] || { log "authentication failed"; exit 1; }

# --- 6. Library -------------------------------------------------------------------
# Metadata fetchers are off: no outbound calls, IDs come from the folder name.
log "adding movie library"
api -o /dev/null -X POST "$JF_URL/Library/VirtualFolders?name=Movies&collectionType=movies&refreshLibrary=true" \
    -H 'Content-Type: application/json' \
    -d "{\"LibraryOptions\":{\"PathInfos\":[{\"Path\":\"$JF_ROOT/media/movies\"}],\"EnableInternetProviders\":false,\"MetadataCountryCode\":\"US\",\"PreferredMetadataLanguage\":\"en\",\"TypeOptions\":[{\"Type\":\"Movie\",\"MetadataFetchers\":[],\"ImageFetchers\":[]}]}}"

api -o /dev/null -X POST "$JF_URL/Library/Refresh"
# Jellyfin 10.11 ignores AnyProviderIdEquals on /Items, so match on the item name -
# which is the folder name, IMDB tag included.
find_item() {
    local id=""
    for _ in $(seq 1 180); do
        id=$(api "$JF_URL/Items?IncludeItemTypes=Movie&Recursive=true" | IMDB="$1" perl -0ne '
            while (/"Name":"([^"]*)","ServerId":"[^"]*","Id":"([0-9a-f]{32})"/g) {
                print "$2\n" if index($1, $ENV{IMDB}) >= 0;
            }' | head -1)
        [ -n "$id" ] && { echo "$id"; return 0; }
        sleep 1
    done
    log "movie $1 was not scanned in with its IMDB id"
    return 1
}
ITEM_THROTTLED=$(find_item tt0133093)
ITEM_OK=$(find_item tt1375666)

# --- 7. Plugin configuration ------------------------------------------------------
log "configuring Bazarr plugin -> ${BAZARR_URL:-http://127.0.0.1:16767}"
api -o /dev/null -X POST "$JF_URL/Plugins/72449d0e-7ba4-4ae7-a996-c00e6b06c8f8/Configuration" \
    -H 'Content-Type: application/json' \
    -d "{\"BazarrUrl\":\"${BAZARR_URL:-http://127.0.0.1:16767}\",\"BazarrApiKey\":\"devkey123456789012345\",\"EnableForMovies\":true,\"EnableForEpisodes\":true,\"SearchTimeoutSeconds\":25}"

cat > "$JF_ROOT/e2e.env" <<EOF
JF_URL=$JF_URL
JF_USER=$JF_USER
JF_PASS=$JF_PASS
JF_TOKEN=$TOKEN
JF_ITEM_THROTTLED=$ITEM_THROTTLED
JF_ITEM_OK=$ITEM_OK
EOF

log "ready - settings written to $JF_ROOT/e2e.env"
