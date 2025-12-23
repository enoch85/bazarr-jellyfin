# Bazarr-Jellyfin Development Environment

Pre-configured development environment for testing the plugin with real services.

## Quick Start

```bash
cd dev-env
./setup-dev.sh
```

This will:
1. Start all Docker containers
2. Wait for services to be ready
3. Configure Sonarr/Radarr root folders
4. Enable all languages in Bazarr
5. Complete Jellyfin setup wizard (creates `dev`/`dev123` user)
6. Create mock media files for testing
7. Show connection details

## Services

| Service  | URL                     | Login                 | API Key                      |
|----------|-------------------------|-----------------------|------------------------------|
| Jellyfin | http://localhost:8096   | `dev` / `dev123`      | (use UI)                     |
| Bazarr   | http://localhost:6767   | (no auth)             | `devkey123456789012345`      |
| Sonarr   | http://localhost:8989   | (no auth)             | `sonarrdevkey12345678901234` |
| Radarr   | http://localhost:7878   | (no auth)             | `radarrdevkey12345678901234` |

## Script Commands

```bash
./setup-dev.sh          # Full setup (default)
./setup-dev.sh start    # Just start containers
./setup-dev.sh stop     # Stop containers
./setup-dev.sh restart  # Restart containers
./setup-dev.sh reset    # Reset everything and start fresh
./setup-dev.sh status   # Show container status
./setup-dev.sh logs     # Show logs (all or specific: ./setup-dev.sh logs bazarr-dev)
```

## First-Time Setup

After running `./setup-dev.sh`, you may still need to:

### Sonarr/Radarr
1. Add root folders if not auto-added:
   - Sonarr: Settings → Media Management → `/tv`
   - Radarr: Settings → Media Management → `/movies`

### Bazarr
1. Go to http://localhost:6767
2. Settings → Providers → Enable subtitle providers (e.g., OpenSubtitles)

### Jellyfin
Setup wizard is completed automatically with user `dev`/`dev123`.
Libraries and Bazarr plugin repository are pre-configured.

1. Go to http://localhost:8096 and login
2. Install the Bazarr plugin: Dashboard → Plugins → Catalog → Bazarr → Install
3. Configure the plugin: Dashboard → Plugins → Bazarr
   - URL: `http://bazarr-dev:6767`
   - API Key: `devkey123456789`

## Plugin Configuration

In Jellyfin's Bazarr plugin settings:
- **Bazarr URL**: `http://bazarr-dev:6767` (use container name)
- **API Key**: `devkey123456789`

## Mock Media Files

Pre-created mock media structure:
```
media/
├── movies/
│   ├── The Matrix (1999)/
│   │   └── The.Matrix.1999.1080p.BluRay.x264.mkv
│   └── Inception (2010)/
│       └── Inception.2010.1080p.BluRay.x264.mkv
└── tv/
    ├── Breaking Bad/
    │   ├── Season 01/
    │   │   ├── Breaking.Bad.S01E01.Pilot.1080p.BluRay.x264.mkv
    │   │   └── Breaking.Bad.S01E02.Cat's.in.the.Bag.1080p.BluRay.x264.mkv
    │   └── Season 02/
    │       └── Breaking.Bad.S02E01.Seven.Thirty-Seven.1080p.BluRay.x264.mkv
    └── The Office (US)/
        └── Season 01/
            └── The.Office.US.S01E01.Pilot.1080p.BluRay.x264.mkv
```

## API Testing

Test Bazarr API:
```bash
curl -H "X-API-KEY: devkey123456789" http://localhost:6767/api/system/status
curl -H "X-API-KEY: devkey123456789" http://localhost:6767/api/system/languages
curl -H "X-API-KEY: devkey123456789" http://localhost:6767/api/movies
curl -H "X-API-KEY: devkey123456789" http://localhost:6767/api/series
curl -H "X-API-KEY: devkey123456789" "http://localhost:6767/api/episodes?seriesid[]=1"
```

## Investigating Episode IDs

To check what IDs Bazarr returns for episodes:
```bash
# Get all series
curl -s -H "X-API-KEY: devkey123456789" http://localhost:6767/api/series | jq '.data[0]'

# Get episodes for series ID 1
curl -s -H "X-API-KEY: devkey123456789" "http://localhost:6767/api/episodes?seriesid[]=1" | jq '.data[0]'
```

Look for `sonarrEpisodeId` in the episode response - this is the key field for simplifying episode lookup.

## Reset Environment

```bash
docker compose down -v
rm -rf config/bazarr/db config/sonarr/*.db config/radarr/*.db
docker compose up -d
```

## Troubleshooting

### Bazarr not connecting to Sonarr/Radarr
- Ensure all containers are on the same Docker network (default)
- Bazarr uses container names: `sonarr-dev:8989` and `radarr-dev:7878`
- Check API keys match in Bazarr settings

### Jellyfin not finding media
- Ensure media folder has correct permissions
- Trigger library scan in Jellyfin admin
