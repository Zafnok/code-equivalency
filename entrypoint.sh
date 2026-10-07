#!/bin/sh
# GitHub Action entrypoint (action.yml `entrypoint:`), not the image's own ENTRYPOINT: `docker run
# equiv ...` still runs `equiv` directly (M3-004 criterion 2). Positional args, in the order
# action.yml passes them: legacy, modern, config, baseline, fail-on, mode. An unset input arrives as "".
set -eu

legacy="$1"
modern="$2"
config="$3"
baseline="$4"
failon="$5"
mode="${6:-}"

out="${GITHUB_WORKSPACE:-/github/workspace}/equiv.sarif"

set -- compare --legacy "$legacy" --modern "$modern" --out "$out"
[ -n "$config" ] && set -- "$@" --config "$config"
[ -n "$baseline" ] && set -- "$@" --baseline "$baseline"
[ -n "$failon" ] && set -- "$@" --fail-on "$failon"
[ -n "$mode" ] && set -- "$@" --mode "$mode"

status=0
dotnet /app/Equiv.Cli.dll "$@" || status=$?

if [ -n "${GITHUB_OUTPUT:-}" ]; then
    echo "sarif=$out" >> "$GITHUB_OUTPUT"
fi

exit "$status"
