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
    if ! visibility=$(jq -er '.visibility | select(type == "string")' <<<"$metadata"); then
      echo "GitHub Packages returned invalid visibility for ${package_id}; refusing to publish."
      exit 1
    fi
    if [[ $visibility != private ]]; then
      if [[ $visibility == public ]]; then
        echo "${package_id} is already public and cannot receive an internal-only release."
      else
        echo "${package_id} has unsupported visibility '${visibility}'; expected private."
      fi
      exit 1
    fi

    if ! versions=$("$gh_bin" api --paginate "/user/packages/nuget/${api_name}/versions?per_page=100" \
      --jq ".[] | select(.name == \"${version}\") | .name" 2>&1); then
      echo "GitHub Packages version preflight failed for ${package_id}; refusing to publish."
      printf '%s\n' "$versions"
      exit 1
    fi

    # Capture before matching: grep -q closes a live pipe after the first match, which makes gh
    # exit with SIGPIPE under pipefail and can turn an existing version into a false negative.
    if grep -Fxq "$version" <<<"$versions"; then
      echo "${package_id} ${version} already exists; refusing a partial duplicate release."
      exit 1
    fi
  elif ! grep -Eq '^HTTP/[0-9.]+ 404 ' <<<"$response"; then
    echo "GitHub Packages preflight failed for ${package_id}; refusing to publish."
    printf '%s\n' "$response"
    exit 1
  fi
done < "$manifest_path"
