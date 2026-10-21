#!/usr/bin/env bash
# Launch Alien Invasion. Builds (Release) if needed, then runs the game.
# Any arguments are passed through, e.g.  ./run.sh --level 10 --god
set -e
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: the .NET SDK (dotnet) is not installed - get it from https://dotnet.microsoft.com/download" >&2
    exit 1
fi

dotnet build -c Release --nologo -v quiet -clp:ErrorsOnly
exec dotnet bin/Release/net10.0/AlienInvasion.dll "$@"
