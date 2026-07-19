#!/usr/bin/env bash

set -euo pipefail

package_id=${1:?Package ID is required.}
version=${2:?Package version is required.}
gh_bin=${GH_BIN:-gh}
api_name=$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')

# Include the status line so an explicit 404 can be distinguished from authentication,
# authorization, network, and server failures. Callers decide whether absence is acceptable.
if ! response=$("$gh_bin" api --include "/user/packages/nuget/${api_name}" 2>&1); then
  if grep -Eq '^HTTP/[0-9.]+ 404 ' <<<"$response"; then
    echo "absent"
    exit 0
  fi

  echo "GitHub Packages metadata query failed for ${package_id}." >&2
  printf '%s\n' "$response" >&2
  exit 1
fi

# Visibility is deliberately not release policy: personal-account packages may be public or
# private. Only the requested version's presence affects whether publication is safe.
if ! versions=$("$gh_bin" api --paginate "/user/packages/nuget/${api_name}/versions?per_page=100" \
  --jq '.[].name' 2>&1); then
  echo "GitHub Packages version query failed for ${package_id}." >&2
  printf '%s\n' "$versions" >&2
  exit 1
fi

if grep -Fxq "$version" <<<"$versions"; then
  echo "version-present"
else
  echo "version-absent"
fi
