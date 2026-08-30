#!/usr/bin/env bash
# Provisions Jellyfin + the plugin and runs the Playwright end-to-end tests.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JF_ROOT="${JF_ROOT:-/opt/jf}"

[ "${SKIP_PROVISION:-0}" = "1" ] || "$HERE/provision.sh"

set -a
# shellcheck source=/dev/null
. "$JF_ROOT/e2e.env"
set +a

dotnet build "$HERE/Jellyfin.Plugin.Bazarr.E2E" --nologo -v q

# Playwright ships its own Node, so no system Node is needed. Installs the browser once.
# System libraries come from `apt-get install libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0
# libatspi2.0-0 libcups2 libxcomposite1 libxdamage1 libxkbcommon0 libpango-1.0-0 libcairo2`.
PW="$HERE/Jellyfin.Plugin.Bazarr.E2E/bin/Debug/net9.0/.playwright"
"$PW"/node/*/node "$PW/package/cli.js" install chromium

dotnet test "$HERE/Jellyfin.Plugin.Bazarr.E2E" --nologo "$@"
