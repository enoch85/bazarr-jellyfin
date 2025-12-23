#!/bin/bash
# Development Environment Setup Script
# This script sets up and configures all services for local plugin development

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Configuration
SONARR_API_KEY="sonarrdevkey12345678901234"
RADARR_API_KEY="radarrdevkey12345678901234"
BAZARR_API_KEY="devkey123456789012345"
DEV_USER="dev"
DEV_PASS="dev123"

wait_for_service() {
    local url=$1
    local name=$2
    local max_attempts=30
    local attempt=1
    
    log_info "Waiting for $name to be ready..."
    while [ $attempt -le $max_attempts ]; do
        if curl -s -o /dev/null -w "%{http_code}" "$url" | grep -q "200\|401\|302"; then
            log_success "$name is ready!"
            return 0
        fi
        echo -n "."
        sleep 2
        attempt=$((attempt + 1))
    done
    echo ""
    log_error "$name failed to start after $max_attempts attempts"
    return 1
}

setup_sonarr() {
    log_info "Setting up Sonarr..."
    
    # Check if API is responding
    if ! curl -s "http://localhost:8989/api/v3/system/status" \
        -H "X-Api-Key: $SONARR_API_KEY" | grep -q "version"; then
        log_warn "Sonarr API not responding"
        return 1
    fi
    
    # Add root folder if not exists
    local root_folders=$(curl -s "http://localhost:8989/api/v3/rootfolder" -H "X-Api-Key: $SONARR_API_KEY")
    if ! echo "$root_folders" | grep -q '"/tv"'; then
        curl -s -X POST "http://localhost:8989/api/v3/rootfolder" \
            -H "X-Api-Key: $SONARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d '{"path": "/tv"}' > /dev/null 2>&1
        log_info "Added root folder /tv"
    fi
    
    # Get quality profile ID
    local quality_id=$(curl -s "http://localhost:8989/api/v3/qualityprofile" -H "X-Api-Key: $SONARR_API_KEY" | grep -o '"id":[0-9]*' | head -1 | cut -d: -f2)
    
    # Add Breaking Bad if not exists
    if ! curl -s "http://localhost:8989/api/v3/series" -H "X-Api-Key: $SONARR_API_KEY" | grep -q "Breaking Bad"; then
        # TVDB ID for Breaking Bad: 81189
        curl -s -X POST "http://localhost:8989/api/v3/series" \
            -H "X-Api-Key: $SONARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d "{\"title\":\"Breaking Bad\",\"tvdbId\":81189,\"qualityProfileId\":$quality_id,\"rootFolderPath\":\"/tv\",\"path\":\"/tv/Breaking Bad\",\"monitored\":true,\"seasonFolder\":true,\"addOptions\":{\"searchForMissingEpisodes\":false}}" > /dev/null 2>&1
        log_info "Added Breaking Bad"
    fi
    
    # Add The Office (US) if not exists
    if ! curl -s "http://localhost:8989/api/v3/series" -H "X-Api-Key: $SONARR_API_KEY" | grep -q "The Office"; then
        # TVDB ID for The Office (US): 73244
        curl -s -X POST "http://localhost:8989/api/v3/series" \
            -H "X-Api-Key: $SONARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d "{\"title\":\"The Office (US)\",\"tvdbId\":73244,\"qualityProfileId\":$quality_id,\"rootFolderPath\":\"/tv\",\"path\":\"/tv/The Office (US)\",\"monitored\":true,\"seasonFolder\":true,\"addOptions\":{\"searchForMissingEpisodes\":false}}" > /dev/null 2>&1
        log_info "Added The Office (US)"
    fi
    
    log_success "Sonarr setup complete"
}

setup_radarr() {
    log_info "Setting up Radarr..."
    
    # Check if API is responding
    if ! curl -s "http://localhost:7878/api/v3/system/status" \
        -H "X-Api-Key: $RADARR_API_KEY" | grep -q "version"; then
        log_warn "Radarr API not responding"
        return 1
    fi
    
    # Add root folder if not exists
    local root_folders=$(curl -s "http://localhost:7878/api/v3/rootfolder" -H "X-Api-Key: $RADARR_API_KEY")
    if ! echo "$root_folders" | grep -q '"/movies"'; then
        curl -s -X POST "http://localhost:7878/api/v3/rootfolder" \
            -H "X-Api-Key: $RADARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d '{"path": "/movies"}' > /dev/null 2>&1
        log_info "Added root folder /movies"
    fi
    
    # Get quality profile ID
    local quality_id=$(curl -s "http://localhost:7878/api/v3/qualityprofile" -H "X-Api-Key: $RADARR_API_KEY" | grep -o '"id":[0-9]*' | head -1 | cut -d: -f2)
    
    # Add Big Buck Bunny if not exists (TMDB: 10378)
    if ! curl -s "http://localhost:7878/api/v3/movie" -H "X-Api-Key: $RADARR_API_KEY" | grep -q "Big Buck Bunny"; then
        curl -s -X POST "http://localhost:7878/api/v3/movie" \
            -H "X-Api-Key: $RADARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d "{\"title\":\"Big Buck Bunny\",\"year\":2008,\"tmdbId\":10378,\"qualityProfileId\":$quality_id,\"rootFolderPath\":\"/movies\",\"path\":\"/movies/Big Buck Bunny (2008)\",\"monitored\":true,\"addOptions\":{\"searchForMovie\":false}}" > /dev/null 2>&1
        log_info "Added Big Buck Bunny"
    fi
    
    # Add Sintel if not exists (TMDB: 45745)
    if ! curl -s "http://localhost:7878/api/v3/movie" -H "X-Api-Key: $RADARR_API_KEY" | grep -q "Sintel"; then
        curl -s -X POST "http://localhost:7878/api/v3/movie" \
            -H "X-Api-Key: $RADARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d "{\"title\":\"Sintel\",\"year\":2010,\"tmdbId\":45745,\"qualityProfileId\":$quality_id,\"rootFolderPath\":\"/movies\",\"path\":\"/movies/Sintel (2010)\",\"monitored\":true,\"addOptions\":{\"searchForMovie\":false}}" > /dev/null 2>&1
        log_info "Added Sintel"
    fi
    
    # Add Tears of Steel if not exists (TMDB: 110416)
    if ! curl -s "http://localhost:7878/api/v3/movie" -H "X-Api-Key: $RADARR_API_KEY" | grep -q "Tears of Steel"; then
        curl -s -X POST "http://localhost:7878/api/v3/movie" \
            -H "X-Api-Key: $RADARR_API_KEY" \
            -H "Content-Type: application/json" \
            -d "{\"title\":\"Tears of Steel\",\"year\":2012,\"tmdbId\":110416,\"qualityProfileId\":$quality_id,\"rootFolderPath\":\"/movies\",\"path\":\"/movies/Tears of Steel (2012)\",\"monitored\":true,\"addOptions\":{\"searchForMovie\":false}}" > /dev/null 2>&1
        log_info "Added Tears of Steel"
    fi
    
    log_success "Radarr setup complete"
}

setup_bazarr() {
    log_info "Setting up Bazarr..."
    
    # Wait for Bazarr database to be created
    local max_wait=30
    local waited=0
    while [ ! -f "./config/bazarr/db/bazarr.db" ] && [ $waited -lt $max_wait ]; do
        sleep 2
        waited=$((waited + 2))
    done
    
    if [ ! -f "./config/bazarr/db/bazarr.db" ]; then
        log_warn "Bazarr database not found - skipping database setup"
        return 1
    fi
    
    # Enable ALL languages in SQLite database
    sqlite3 ./config/bazarr/db/bazarr.db "UPDATE table_settings_languages SET enabled = 1;" 2>/dev/null || true
    local lang_count=$(sqlite3 ./config/bazarr/db/bazarr.db "SELECT COUNT(*) FROM table_settings_languages WHERE enabled = 1;" 2>/dev/null || echo "0")
    log_info "Enabled $lang_count languages"
    
    # Create default language profile (English) if not exists
    local profile_exists=$(sqlite3 ./config/bazarr/db/bazarr.db "SELECT COUNT(*) FROM table_languages_profiles WHERE profileId = 1;" 2>/dev/null || echo "0")
    if [ "$profile_exists" = "0" ]; then
        sqlite3 ./config/bazarr/db/bazarr.db "INSERT INTO table_languages_profiles (profileId, items, name, cutoff, originalFormat) VALUES (1, '[{\"id\": 1, \"language\": \"en\", \"hi\": \"False\", \"forced\": \"False\", \"audio_exclude\": \"False\"}]', 'English', NULL, 0);" 2>/dev/null || true
        log_info "Created English language profile"
    fi
    
    # Update config file - replace auth.apikey value (Bazarr generates a random one)
    if [ -f "./config/bazarr/config/config.yaml" ]; then
        # Replace the random auth apikey with our known key
        sed -i "s/^  apikey: [a-f0-9]\{32\}$/  apikey: $BAZARR_API_KEY/" ./config/bazarr/config/config.yaml 2>/dev/null || true
        
        # Enable default profiles for movies and series
        sed -i "s/movie_default_enabled: false/movie_default_enabled: true/" ./config/bazarr/config/config.yaml 2>/dev/null || true
        sed -i "s/movie_default_profile: ''/movie_default_profile: '1'/" ./config/bazarr/config/config.yaml 2>/dev/null || true
        sed -i "s/serie_default_enabled: false/serie_default_enabled: true/" ./config/bazarr/config/config.yaml 2>/dev/null || true
        sed -i "s/serie_default_profile: ''/serie_default_profile: '1'/" ./config/bazarr/config/config.yaml 2>/dev/null || true
        
        log_info "Updated Bazarr config"
    fi
    
    # Restart Bazarr to apply config changes
    log_info "Restarting Bazarr to apply settings..."
    docker restart bazarr-dev > /dev/null 2>&1
    sleep 10
    
    # Wait for Bazarr to be ready again
    wait_for_service "http://localhost:6767" "Bazarr"
    
    # Give Bazarr time to establish SignalR connections to Radarr/Sonarr
    log_info "Waiting for Bazarr to connect to Radarr/Sonarr..."
    sleep 10
    
    # Trigger sync with Radarr and Sonarr using POST with JSON body
    log_info "Syncing movies from Radarr..."
    curl -s -X POST -H "X-API-KEY: $BAZARR_API_KEY" -H "Content-Type: application/json" \
        "http://localhost:6767/api/system/tasks" -d '{"taskid":"update_movies"}' > /dev/null 2>&1
    
    # Wait for movie sync to complete (can take 15-30 seconds)
    log_info "Waiting for movie sync to complete..."
    local max_attempts=12
    local attempt=1
    local movie_count=0
    while [ $attempt -le $max_attempts ]; do
        sleep 5
        movie_count=$(curl -s -H "X-API-KEY: $BAZARR_API_KEY" "http://localhost:6767/api/movies" 2>/dev/null | grep -o '"total":[0-9]*' | cut -d: -f2 || echo "0")
        if [ "$movie_count" -gt 0 ]; then
            log_success "Movies synced: $movie_count"
            break
        fi
        echo -n "."
        attempt=$((attempt + 1))
    done
    echo ""
    
    log_info "Syncing series from Sonarr..."
    curl -s -X POST -H "X-API-KEY: $BAZARR_API_KEY" -H "Content-Type: application/json" \
        "http://localhost:6767/api/system/tasks" -d '{"taskid":"update_series"}' > /dev/null 2>&1
    
    # Wait for series sync to complete
    log_info "Waiting for series sync to complete..."
    attempt=1
    local series_count=0
    while [ $attempt -le $max_attempts ]; do
        sleep 5
        series_count=$(curl -s -H "X-API-KEY: $BAZARR_API_KEY" "http://localhost:6767/api/series" 2>/dev/null | grep -o '"total":[0-9]*' | cut -d: -f2 || echo "0")
        if [ "$series_count" -gt 0 ]; then
            log_success "Series synced: $series_count"
            break
        fi
        echo -n "."
        attempt=$((attempt + 1))
    done
    echo ""
    
    # Final verification
    movie_count=$(curl -s -H "X-API-KEY: $BAZARR_API_KEY" "http://localhost:6767/api/movies" 2>/dev/null | grep -o '"total":[0-9]*' | cut -d: -f2 || echo "0")
    series_count=$(curl -s -H "X-API-KEY: $BAZARR_API_KEY" "http://localhost:6767/api/series" 2>/dev/null | grep -o '"total":[0-9]*' | cut -d: -f2 || echo "0")
    log_info "Final count: $movie_count movies and $series_count series synced from *arr"
    
    if [ "$movie_count" -eq 0 ] && [ "$series_count" -eq 0 ]; then
        log_warn "Sync may have failed - check Bazarr logs with: docker compose logs bazarr"
    fi
    
    log_success "Bazarr setup complete"
}

setup_jellyfin() {
    log_info "Setting up Jellyfin..."
    
    # Check if wizard is already completed
    local info=$(curl -s "http://localhost:8096/System/Info/Public" 2>/dev/null)
    if echo "$info" | grep -q '"StartupWizardCompleted":true'; then
        log_success "Jellyfin already configured"
        return 0
    fi
    
    # Complete the setup wizard via API
    log_info "Running Jellyfin setup wizard..."
    
    # Step 1: Set language/culture
    curl -s -X POST "http://localhost:8096/Startup/Configuration" \
        -H "Content-Type: application/json" \
        -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' > /dev/null 2>&1
    
    sleep 1
    
    # Step 2: Get first user (this creates the initial user record that we can then update)
    curl -s "http://localhost:8096/Startup/FirstUser" > /dev/null 2>&1
    
    sleep 1
    
    # Step 3: Update the first user with our username and password
    curl -s -X POST "http://localhost:8096/Startup/User" \
        -H "Content-Type: application/json" \
        -d "{\"Name\":\"$DEV_USER\",\"Password\":\"$DEV_PASS\"}" > /dev/null 2>&1
    
    sleep 1
    
    # Step 4: Complete wizard
    curl -s -X POST "http://localhost:8096/Startup/Complete" > /dev/null 2>&1
    
    # Wait for Jellyfin to fully initialize after wizard completion
    sleep 5
    
    # Step 4: Authenticate to get token for library setup
    local auth_response=$(curl -s -X POST "http://localhost:8096/Users/AuthenticateByName" \
        -H "Content-Type: application/json" \
        -H 'X-Emby-Authorization: MediaBrowser Client="Setup", Device="Dev", DeviceId="setup", Version="1.0"' \
        -d "{\"Username\":\"$DEV_USER\",\"Pw\":\"$DEV_PASS\"}" 2>/dev/null)
    
    local token=$(echo "$auth_response" | grep -o '"AccessToken":"[^"]*"' | cut -d'"' -f4)
    
    if [ -n "$token" ]; then
        log_info "Creating media libraries..."
        
        # Add Movies library
        curl -s -X POST "http://localhost:8096/Library/VirtualFolders?name=Movies&collectionType=movies&refreshLibrary=false" \
            -H "X-Emby-Token: $token" \
            -H "Content-Type: application/json" \
            -d '{"LibraryOptions":{"PathInfos":[{"Path":"/media/movies"}]}}' > /dev/null 2>&1
        
        # Add TV Shows library
        curl -s -X POST "http://localhost:8096/Library/VirtualFolders?name=TV%20Shows&collectionType=tvshows&refreshLibrary=false" \
            -H "X-Emby-Token: $token" \
            -H "Content-Type: application/json" \
            -d '{"LibraryOptions":{"PathInfos":[{"Path":"/media/tv"}]}}' > /dev/null 2>&1
        
        # Trigger library scan
        curl -s -X POST "http://localhost:8096/Library/Refresh" \
            -H "X-Emby-Token: $token" > /dev/null 2>&1
        
        # Add Bazarr plugin repository
        log_info "Adding Bazarr plugin repository..."
        curl -s -X POST "http://localhost:8096/Repositories" \
            -H "X-Emby-Token: $token" \
            -H "Content-Type: application/json" \
            -d '[{"Name":"Jellyfin Stable","Url":"https://repo.jellyfin.org/files/plugin/manifest.json","Enabled":true},{"Name":"Bazarr Plugin","Url":"https://raw.githubusercontent.com/enoch85/bazarr-jellyfin/main/manifest.json","Enabled":true}]' > /dev/null 2>&1
        
        log_success "Jellyfin setup complete (user: $DEV_USER / $DEV_PASS)"
    else
        log_warn "Could not authenticate - libraries need manual setup"
    fi
}

create_mock_media() {
    log_info "Creating mock media files with ffmpeg (this may take a moment)..."
    
    # Check if ffmpeg is available
    if ! command -v ffmpeg &> /dev/null; then
        log_warn "ffmpeg not found, installing..."
        sudo apt-get update -qq && sudo apt-get install -y -qq ffmpeg > /dev/null 2>&1
    fi
    
    # Movies (using Blender open movies as safe examples)
    # Create real video files with test pattern so Radarr/Bazarr can properly detect them
    mkdir -p "./media/movies/Big Buck Bunny (2008)"
    mkdir -p "./media/movies/Sintel (2010)"
    mkdir -p "./media/movies/Tears of Steel (2012)"
    
    # Create 2-minute test videos (~10MB each) with color bars and silent audio
    for movie in "Big Buck Bunny (2008)" "Sintel (2010)" "Tears of Steel (2012)"; do
        local file="./media/movies/$movie/$movie.mp4"
        if [ ! -s "$file" ]; then
            log_info "Creating $movie..."
            ffmpeg -f lavfi -i testsrc=duration=120:size=1280x720:rate=24 \
                   -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 \
                   -c:v libx264 -preset ultrafast -crf 28 \
                   -c:a aac -shortest \
                   -y "$file" 2>/dev/null
        fi
    done
    
    # TV Shows - 1-minute test videos per episode (~5MB each)
    mkdir -p "./media/tv/Breaking Bad/Season 01"
    mkdir -p "./media/tv/Breaking Bad/Season 02"
    mkdir -p "./media/tv/The Office (US)/Season 01"
    
    local episodes=(
        "./media/tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.mp4"
        "./media/tv/Breaking Bad/Season 01/Breaking Bad - S01E02 - Cat's in the Bag.mp4"
        "./media/tv/Breaking Bad/Season 02/Breaking Bad - S02E01 - Seven Thirty-Seven.mp4"
        "./media/tv/The Office (US)/Season 01/The Office (US) - S01E01 - Pilot.mp4"
        "./media/tv/The Office (US)/Season 01/The Office (US) - S01E02 - Diversity Day.mp4"
    )
    
    for ep in "${episodes[@]}"; do
        if [ ! -s "$ep" ]; then
            log_info "Creating $(basename "$ep")..."
            ffmpeg -f lavfi -i testsrc=duration=60:size=1280x720:rate=24 \
                   -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 \
                   -c:v libx264 -preset ultrafast -crf 28 \
                   -c:a aac -shortest \
                   -y "$ep" 2>/dev/null
        fi
    done
    
    log_success "Mock media created (3 movies @ 2min, 5 episodes @ 1min)"
}

create_media_dirs() {
    log_info "Creating media directories..."
    mkdir -p ./media/tv
    mkdir -p ./media/movies
    mkdir -p ./downloads
    log_success "Media directories created"
}

# Pre-create config files with our API keys before containers start
create_config_files() {
    log_info "Creating config files with API keys..."
    
    # Sonarr config
    mkdir -p ./config/sonarr
    if [ ! -f "./config/sonarr/config.xml" ]; then
        cat > ./config/sonarr/config.xml << EOF
<Config>
  <BindAddress>*</BindAddress>
  <Port>8989</Port>
  <SslPort>9898</SslPort>
  <EnableSsl>False</EnableSsl>
  <LaunchBrowser>False</LaunchBrowser>
  <ApiKey>$SONARR_API_KEY</ApiKey>
  <AuthenticationMethod>None</AuthenticationMethod>
  <AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>
  <Branch>main</Branch>
  <LogLevel>info</LogLevel>
  <InstanceName>Sonarr</InstanceName>
  <UpdateMechanism>Docker</UpdateMechanism>
</Config>
EOF
    fi
    
    # Radarr config
    mkdir -p ./config/radarr
    if [ ! -f "./config/radarr/config.xml" ]; then
        cat > ./config/radarr/config.xml << EOF
<Config>
  <BindAddress>*</BindAddress>
  <Port>7878</Port>
  <SslPort>9898</SslPort>
  <EnableSsl>False</EnableSsl>
  <LaunchBrowser>False</LaunchBrowser>
  <ApiKey>$RADARR_API_KEY</ApiKey>
  <AuthenticationMethod>None</AuthenticationMethod>
  <AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>
  <Branch>master</Branch>
  <LogLevel>info</LogLevel>
  <InstanceName>Radarr</InstanceName>
  <UpdateMechanism>Docker</UpdateMechanism>
</Config>
EOF
    fi
    
    # Bazarr config - Note: Bazarr will merge this with defaults on first start
    # The auth.apikey is what's used for API authentication
    mkdir -p ./config/bazarr/config
    if [ ! -f "./config/bazarr/config/config.yaml" ]; then
        cat > ./config/bazarr/config/config.yaml << EOF
analytics:
  enabled: false
auth:
  apikey: $BAZARR_API_KEY
  type: null
general:
  ip: 0.0.0.0
  port: 6767
  base_url: ''
  debug: false
  branch: master
  auto_update: false
  single_language: false
  use_sonarr: true
  use_radarr: true
  apikey: $BAZARR_API_KEY
  analytics_enabled: false
  movie_default_enabled: true
  movie_default_profile: '1'
  serie_default_enabled: true
  serie_default_profile: '1'
sonarr:
  ip: sonarr-dev
  port: 8989
  apikey: $SONARR_API_KEY
  ssl: false
  base_url: ''
  only_monitored: false
  series_sync: 60
  episodes_sync: 60
radarr:
  ip: radarr-dev
  port: 7878
  apikey: $RADARR_API_KEY
  ssl: false
  base_url: ''
  only_monitored: false
  movies_sync: 60
EOF
    fi
    
    log_success "Config files created"
}

show_summary() {
    echo ""
    echo "=============================================="
    echo -e "${GREEN}Development Environment Ready!${NC}"
    echo "=============================================="
    echo ""
    echo "Services:"
    echo "  Jellyfin: http://localhost:8096"
    echo "    User: $DEV_USER / $DEV_PASS"
    echo ""
    echo "  Bazarr:   http://localhost:6767"
    echo "    API Key: $BAZARR_API_KEY"
    echo "    Auth: None"
    echo ""
    echo "  Sonarr:   http://localhost:8989"
    echo "    API Key: $SONARR_API_KEY"
    echo "    Auth: None"
    echo ""
    echo "  Radarr:   http://localhost:7878"
    echo "    API Key: $RADARR_API_KEY"
    echo "    Auth: None"
    echo ""
    echo "Mock Media:"
    echo "  Movies: Big Buck Bunny, Sintel, Tears of Steel"
    echo "  TV: Breaking Bad (S01-S02), The Office US (S01)"
    echo ""
    echo "Plugin Configuration (in Jellyfin):"
    echo "  Bazarr URL: http://bazarr-dev:6767"
    echo "  API Key:    $BAZARR_API_KEY"
    echo ""
}

main() {
    echo "=============================================="
    echo "  Bazarr-Jellyfin Dev Environment Setup"
    echo "=============================================="
    echo ""
    
    # Create directories, config files, and mock media
    create_media_dirs
    create_config_files
    create_mock_media
    
    # Start containers
    log_info "Starting Docker containers..."
    docker compose up -d
    
    # Wait for services
    wait_for_service "http://localhost:8989" "Sonarr"
    wait_for_service "http://localhost:7878" "Radarr"
    wait_for_service "http://localhost:6767" "Bazarr"
    wait_for_service "http://localhost:8096" "Jellyfin"
    
    # Give services a moment to fully initialize
    sleep 5
    
    # Setup each service
    setup_sonarr
    setup_radarr
    setup_bazarr
    setup_jellyfin
    
    # Show summary
    show_summary
}

# Handle arguments
case "${1:-}" in
    --help|-h)
        echo "Usage: $0 [command]"
        echo ""
        echo "Commands:"
        echo "  (none)    Full setup - start containers and configure"
        echo "  start     Just start containers"
        echo "  stop      Stop containers"
        echo "  restart   Restart containers"
        echo "  reset     Stop, remove volumes, and start fresh"
        echo "  status    Show container status"
        echo "  logs      Show container logs"
        echo ""
        exit 0
        ;;
    start)
        docker compose up -d
        ;;
    stop)
        docker compose down
        ;;
    restart)
        docker compose restart
        ;;
    reset)
        log_warn "This will delete all container data!"
        read -p "Are you sure? (y/N) " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            docker compose down -v
            rm -rf ./config/bazarr/db ./config/bazarr/cache ./config/bazarr/log
            rm -rf ./config/sonarr/logs ./config/sonarr/Backups
            rm -rf ./config/radarr/logs ./config/radarr/Backups
            rm -rf ./config/jellyfin/data ./config/jellyfin/log ./config/jellyfin/metadata
            main
        fi
        ;;
    status)
        docker compose ps
        ;;
    logs)
        docker compose logs -f "${2:-}"
        ;;
    *)
        main
        ;;
esac
