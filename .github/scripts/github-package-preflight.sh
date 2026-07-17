#!/usr/bin/env bash

set -euo pipefail

manifest_path=${1:?Package manifest path is required.}
gh_bin=${GH_BIN:-gh}

# Personal-account packages must be private. GitHub does not allow a public package to become
# private, so fail before publishing any member of the family. Only an explicit HTTP 404 means a
# package is absent; authentication, authorization, network, and server failures abort the release.
while IFS=$'\t' read -r package_id version; do
  api_name=$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')
  if response=$("$gh_bin" api --include "/user/packages/nuget/${api_name}" 2>&1); then
    metadata=$(tail -n 1 <<<"$response")
    visibility=$(jq -r '.visibility' <<<"$metadata")
    if [[ $visibility == public ]]; then
      echo "${package_id} is already public and cannot receive an internal-only release."
      exit 1
    fi

    if "$gh_bin" api --paginate "/user/packages/nuget/${api_name}/versions?per_page=100" \
      --jq ".[] | select(.name == \"${version}\") | .name" | grep -Fxq "$version"; then
      echo "${package_id} ${version} already exists; refusing a partial duplicate release."
      exit 1
    fi
  elif ! grep -Eq '^HTTP/[0-9.]+ 404 ' <<<"$response"; then
    echo "GitHub Packages preflight failed for ${package_id}; refusing to publish."
    printf '%s\n' "$response"
    exit 1
  fi
done < "$manifest_path"
