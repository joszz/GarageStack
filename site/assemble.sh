#!/usr/bin/env bash
# Builds the website into _site/ (or the folder given): the pages in site/, with the shared
# partials put in, plus the screenshots and icons the app already has, so each lives in one place.
# Used by .github/workflows/pages.yml and for a local preview, see .github/CONTRIBUTING.md.
#
# A page takes a partial with a comment on a line of its own, indented as the partial should be:
#   <!-- include header features -->
# The optional second word names the page, and marks its link in the partial (data-nav="features")
# with aria-current="page".
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-$root/_site}"
partials="$root/site/partials"
include='^([[:space:]]*)<!-- include ([a-z]+)( ([a-z]+))? -->$'

render() {
  local line part indent name page
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"
    if [[ $line =~ $include ]]; then
      indent="${BASH_REMATCH[1]}" name="${BASH_REMATCH[2]}" page="${BASH_REMATCH[4]}"
      if [ ! -f "$partials/$name.html" ]; then
        echo "$1: no partial named $name" >&2
        exit 1
      fi
      while IFS= read -r part || [ -n "$part" ]; do
        part="${part%$'\r'}"
        if [ -n "$page" ]; then
          part="${part//data-nav=\"$page\"/data-nav=\"$page\" aria-current=\"page\"}"
        fi
        printf '%s%s\n' "${part:+$indent}" "$part"
      done <"$partials/$name.html"
    else
      printf '%s\n' "$line"
    fi
  done <"$1"
}

# Emptied rather than removed, so a preview server running inside it keeps working.
mkdir -p "$out"
find "$out" -mindepth 1 -delete
mkdir -p "$out/screenshots"

for page in "$root"/site/*.html; do
  render "$page" >"$out/$(basename "$page")"
done
cp -R "$root/site/assets" "$out/"
cp "$root"/documentation/screenshots/*.webp "$out/screenshots/"
cp "$root/frontend/public/logo.svg" \
  "$root/frontend/public/favicon.ico" \
  "$root/frontend/public/apple-touch-icon-180x180.png" \
  "$out/"
# Served as is: no Jekyll run over the files.
touch "$out/.nojekyll"

echo "Website assembled in $out"
