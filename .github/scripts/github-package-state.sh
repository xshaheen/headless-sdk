#!/usr/bin/env bash

set -euo pipefail

package_id=${1:?Package ID is required.}
version=${2:?Package version is required.}
gh_bin=${GH_BIN:-gh}
api_name=$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')

# Include the status line so an explicit 404 can be distinguished from authentication,
# authorization, network, and server failures. Callers decide whether absence is acceptable.
if response=$("$gh_bin" api --include "/user/packages/nuget/${api_name}" 2>&1); then
  metadata=$(tail -n 1 <<<"$response")
elif grep -Eq '^HTTP/[0-9.]+ 404 ' <<<"$response"; then
  echo "absent"
  exit 0
else
  echo "GitHub Packages metadata query failed for ${package_id}." >&2
  printf '%s\n' "$response" >&2
  exit 1
fi

if ! visibility=$(jq -er '.visibility | select(type == "string")' <<<"$metadata"); then
  echo "invalid-visibility"
  exit 0
fi

if [[ $visibility != private ]]; then
  printf 'non-private\t%s\n' "$visibility"
  exit 0
fi

# Keep the jq program constant and match the requested version locally. This avoids embedding a
# package version in jq source and avoids SIGPIPE by capturing the complete paginated response.
if ! versions=$("$gh_bin" api --paginate "/user/packages/nuget/${api_name}/versions?per_page=100" \
  --jq '.[].name' 2>&1); then
  echo "GitHub Packages version query failed for ${package_id}." >&2
  printf '%s\n' "$versions" >&2
  exit 1
fi

if grep -Fxq "$version" <<<"$versions"; then
  printf 'private\tversion-present\n'
else
  printf 'private\tversion-absent\n'
fi
