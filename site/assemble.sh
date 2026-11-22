#!/usr/bin/env bash
# Builds the website into _site/ (or the folder given): the pages in site/, plus the screenshots
# and icons the app already has, so each lives in one place. Used by .github/workflows/pages.yml
# and for a local preview, see .github/CONTRIBUTING.md.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-$root/_site}"

# Emptied rather than removed, so a preview server running inside it keeps working.
mkdir -p "$out"
find "$out" -mindepth 1 -delete
mkdir -p "$out/screenshots"
cp -R "$root/site/index.html" "$root/site/assets" "$out/"
cp "$root"/documentation/screenshots/*.webp "$out/screenshots/"
cp "$root/frontend/public/logo.svg" \
  "$root/frontend/public/favicon.ico" \
  "$root/frontend/public/apple-touch-icon-180x180.png" \
  "$out/"
# Served as is: no Jekyll run over the files.
touch "$out/.nojekyll"

echo "Website assembled in $out"
