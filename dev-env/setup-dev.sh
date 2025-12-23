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
SONARR_API_KEY="sonarrdevkey1234"
RADARR_API_KEY="radarrdevkey1234"
BAZARR_API_KEY="devkey123456789"
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
    
    # Check if user already exists by trying the API
    if curl -s "http://localhost:8989/api/v3/system/status" \
        -H "X-Api-Key: $SONARR_API_KEY" | grep -q "version"; then
        log_success "Sonarr already configured"
        return 0
    fi
    
    # Sonarr v4 initialization endpoint
    local init_response=$(curl -s -X POST "http://localhost:8989/initialize.json" \
        -H "Content-Type: application/json" \
        -d "{
            \"username\": \"$DEV_USER\",
            \"password\": \"$DEV_PASS\",
            \"authenticationMethod\": \"forms\"
        }" 2>/dev/null)
    
    if [ -n "$init_response" ]; then
        log_success "Sonarr user created"
    else
        log_warn "Sonarr may already be initialized or using different auth"
    fi
    
    # Add root folder via API
    curl -s -X POST "http://localhost:8989/api/v3/rootfolder" \
        -H "X-Api-Key: $SONARR_API_KEY" \
        -H "Content-Type: application/json" \
        -d '{"path": "/tv"}' > /dev/null 2>&1 || true
    
    log_success "Sonarr setup complete"
}

setup_radarr() {
    log_info "Setting up Radarr..."
    
    # Check if already configured
    if curl -s "http://localhost:7878/api/v3/system/status" \
        -H "X-Api-Key: $RADARR_API_KEY" | grep -q "version"; then
        log_success "Radarr already configured"
        return 0
    fi
    
    # Radarr v4/v5 initialization
    local init_response=$(curl -s -X POST "http://localhost:7878/initialize.json" \
        -H "Content-Type: application/json" \
        -d "{
            \"username\": \"$DEV_USER\",
            \"password\": \"$DEV_PASS\",
            \"authenticationMethod\": \"forms\"
        }" 2>/dev/null)
    
    if [ -n "$init_response" ]; then
        log_success "Radarr user created"
    else
        log_warn "Radarr may already be initialized or using different auth"
    fi
    
    # Add root folder via API
    curl -s -X POST "http://localhost:7878/api/v3/rootfolder" \
        -H "X-Api-Key: $RADARR_API_KEY" \
        -H "Content-Type: application/json" \
        -d '{"path": "/movies"}' > /dev/null 2>&1 || true
    
    log_success "Radarr setup complete"
}

setup_bazarr() {
    log_info "Setting up Bazarr..."
    
    # Enable all languages in SQLite database
    if [ -f "./config/bazarr/db/bazarr.db" ]; then
        sqlite3 ./config/bazarr/db/bazarr.db "UPDATE table_settings_languages SET enabled = 1;" 2>/dev/null || true
        local count=$(sqlite3 ./config/bazarr/db/bazarr.db "SELECT COUNT(*) FROM table_settings_languages WHERE enabled = 1;" 2>/dev/null || echo "0")
        log_success "Enabled $count languages in Bazarr"
    else
        log_warn "Bazarr database not found yet - languages will be enabled on next run"
    fi
    
    # Configure enabled providers via API
    # First, get current settings
    local settings=$(curl -s "http://localhost:6767/api/system/settings" \
        -H "X-API-KEY: $BAZARR_API_KEY" 2>/dev/null)
    
    if [ -n "$settings" ]; then
        # Update enabled_providers in config.yaml directly (API doesn't easily support this)
        if [ -f "./config/bazarr/config/config.yaml" ]; then
            # Check if providers are already set
            if ! grep -q "supersubtitles" ./config/bazarr/config/config.yaml 2>/dev/null; then
                # Use sed to update enabled_providers list
                sed -i 's/enabled_providers:.*/enabled_providers:\n  - tvsubtitles\n  - supersubtitles/' ./config/bazarr/config/config.yaml 2>/dev/null || true
                log_info "Configured subtitle providers"
            fi
        fi
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
        
        log_success "Jellyfin setup complete (user: $DEV_USER / $DEV_PASS)"
    else
        log_warn "Could not authenticate - libraries need manual setup"
    fi
}

create_mock_media() {
    log_info "Creating mock media files..."
    
    # Movies (using Blender open movies as safe examples)
    mkdir -p "./media/movies/Big Buck Bunny (2008)"
    mkdir -p "./media/movies/Sintel (2010)"
    mkdir -p "./media/movies/Tears of Steel (2012)"
    touch "./media/movies/Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4"
    touch "./media/movies/Sintel (2010)/Sintel (2010).mp4"
    touch "./media/movies/Tears of Steel (2012)/Tears of Steel (2012).mp4"
    
    # TV Shows
    mkdir -p "./media/tv/Breaking Bad/Season 01"
    mkdir -p "./media/tv/Breaking Bad/Season 02"
    mkdir -p "./media/tv/The Office (US)/Season 01"
    touch "./media/tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.mp4"
    touch "./media/tv/Breaking Bad/Season 01/Breaking Bad - S01E02 - Cat's in the Bag.mp4"
    touch "./media/tv/Breaking Bad/Season 02/Breaking Bad - S02E01 - Seven Thirty-Seven.mp4"
    touch "./media/tv/The Office (US)/Season 01/The Office (US) - S01E01 - Pilot.mp4"
    touch "./media/tv/The Office (US)/Season 01/The Office (US) - S01E02 - Diversity Day.mp4"
    
    log_success "Mock media created (3 movies, 2 TV shows)"
}

create_media_dirs() {
    log_info "Creating media directories..."
    mkdir -p ./media/tv
    mkdir -p ./media/movies
    mkdir -p ./downloads
    log_success "Media directories created"
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
    
    # Create directories and mock media
    create_media_dirs
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
