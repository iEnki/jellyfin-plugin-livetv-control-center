#!/usr/bin/env bash
# Rebuilds manifest-dev.json on the "dev-channel" pre-release:
# all stable versions from manifest.json plus development builds newer than the latest stable version.
# Usage: update-dev-manifest.sh <stable-manifest.json> [entry.json] [channel] [catalog] [marker]
set -euo pipefail

stable="$1"
entry="${2:-}"
channel="${3:-dev-channel}"
catalog="${4:-manifest-dev.json}"
marker="${5:-[DEV]}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if ! gh release view "$channel" >/dev/null 2>&1; then
  gh release create "$channel" --prerelease --target main --title "${channel}" \
    --notes "Jellyfin plugin repository for development builds. Add this URL in Jellyfin: https://github.com/${GITHUB_REPOSITORY}/releases/download/${channel}/${catalog}"
fi

if ! gh release download "$channel" --pattern "$catalog" --dir "$work" 2>/dev/null; then
  echo '[{"versions": []}]' > "$work/$catalog"
fi

if [ -n "$entry" ]; then new="$(cat "$entry")"; else new='null'; fi

jq --slurpfile old "$work/$catalog" --argjson new "$new" --arg marker "$marker" '
  def ver: split(".") | map(tonumber);
  (.[0].versions | map(.version | ver) | max) as $latest
  | ([ $old[0][0].versions[]? | select(.changelog | startswith($marker)) ]) as $devs
  | ((if $new == null then [] else [$new] end) + $devs
      | map(select(.version | ver > ($latest // [0])))
      | unique_by(.version)
      | sort_by(.version | ver) | reverse) as $keep
  | .[0].versions = ($keep + .[0].versions)
' "$stable" > "$catalog"

gh release upload "$channel" "$catalog" --clobber
jq -r '.[0].versions[].version' "$catalog"
