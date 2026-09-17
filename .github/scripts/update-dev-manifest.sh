#!/usr/bin/env bash
# Rebuilds manifest-dev.json on the "dev-channel" pre-release:
# all stable versions from manifest.json plus development builds newer than the latest stable version.
# Usage: update-dev-manifest.sh <stable-manifest.json> [new-dev-entry.json]
set -euo pipefail

stable="$1"
entry="${2:-}"
channel="dev-channel"
work="$(mktemp -d)"

if ! gh release view "$channel" >/dev/null 2>&1; then
  gh release create "$channel" --prerelease --target main --title "Development channel" \
    --notes "Jellyfin plugin repository for development builds. Add this URL in Jellyfin: https://github.com/${GITHUB_REPOSITORY}/releases/download/${channel}/manifest-dev.json"
fi

if ! gh release download "$channel" --pattern manifest-dev.json --dir "$work" 2>/dev/null; then
  echo '[{"versions": []}]' > "$work/manifest-dev.json"
fi

if [ -n "$entry" ]; then new="$(cat "$entry")"; else new='null'; fi

jq --slurpfile old "$work/manifest-dev.json" --argjson new "$new" '
  def ver: split(".") | map(tonumber);
  (.[0].versions | map(.version | ver) | max) as $latest
  | ([ $old[0][0].versions[]? | select(.changelog | startswith("[DEV]")) ]) as $devs
  | ((if $new == null then [] else [$new] end) + $devs
      | map(select(.version | ver > ($latest // [0])))
      | unique_by(.version)
      | sort_by(.version | ver) | reverse) as $keep
  | .[0].versions = ($keep + .[0].versions)
' "$stable" > manifest-dev.json

gh release upload "$channel" manifest-dev.json --clobber
jq -r '.[0].versions[].version' manifest-dev.json
