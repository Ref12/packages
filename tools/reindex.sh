#!/usr/bin/env bash
# Rebuild registration indexes from the releases of this repo and upload them.
#   tools/reindex.sh Ref12.FeedProbe   # one package
#   tools/reindex.sh                   # every package (repair); also refreshes the service index
# Needs: gh (GH_TOKEN, GITHUB_REPOSITORY), jq, dotnet. Run it inside the 'index-<id>' concurrency group.
set -euo pipefail

id="${1:-}"
idl="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
repo="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
base="https://github.com/${repo}/releases/download/"
here="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"
work="$tmp/work"; out="$tmp/out"
mkdir -p "$work" "$out"

echo "Collecting version releases (prefix: ${idl:-<all>})"
gh release list --repo "$repo" --limit 1000 --json tagName,createdAt > "$tmp/releases.json"
count=0
while IFS=$'\t' read -r tag created; do
  [ "$tag" = "feed" ] && continue
  if [ -n "$idl" ] && [[ "$tag" != "$idl"-* ]]; then continue; fi
  assets="$(gh release view "$tag" --repo "$repo" --json assets -q '.assets[].name')"
  d="$work/$tag"
  if grep -qE '\.nuspec$' <<<"$assets"; then
    gh release download "$tag" --repo "$repo" -p '*.nuspec' -D "$d"
  elif grep -qE '\.nupkg$' <<<"$assets"; then
    # Releases created before nuspec assets existed: the tool reads the nuspec from the nupkg.
    gh release download "$tag" --repo "$repo" -p '*.nupkg' -D "$d"
  else
    continue   # package-level index release or unrelated release
  fi
  if grep -qx 'unlisted' <<<"$assets"; then echo unlisted > "$d/unlisted"; fi
  printf '%s' "$created" > "$d/published.txt"
  count=$((count + 1))
done < <(jq -r '.[] | [.tagName, .createdAt] | @tsv' "$tmp/releases.json")
echo "Found $count version release(s)"

dotnet run --project "$here/Feed" -c Release -- index --root "$work" --out "$out" --base-url "$base" ${id:+--id "$id"} | tee "$tmp/ids.txt"
if [ -n "$id" ] && ! grep -qx "$idl" "$tmp/ids.txt"; then
  echo "::error::No version releases found for package '$id' (expected releases named $idl-<version>)."
  exit 1
fi

while read -r pid; do
  [ -z "$pid" ] && continue
  if ! gh release view "$pid" --repo "$repo" >/dev/null 2>&1; then
    gh release create "$pid" --repo "$repo" --title "$pid" --latest=false \
      --notes "Generated NuGet registration index for $pid. Do not edit: rebuilt by tools/reindex.sh from the $pid-<version> releases."
  fi
  gh release upload "$pid" "$out/$pid/index.json" --repo "$repo" --clobber
  echo "Updated $base$pid/index.json"
done < "$tmp/ids.txt"

if [ -z "$id" ]; then
  printf '{\n  "version": "3.0.0",\n  "resources": [\n    {\n      "@id": "%s",\n      "@type": "RegistrationsBaseUrl/3.6.0"\n    }\n  ]\n}\n' "$base" > "$tmp/index.json"
  if ! gh release view feed --repo "$repo" >/dev/null 2>&1; then
    gh release create feed --repo "$repo" --title feed --latest=false --notes "NuGet v3 service index (source URL ends in feed/index.json)."
  fi
  gh release upload feed "$tmp/index.json" --repo "$repo" --clobber
  echo "Updated service index"
fi
