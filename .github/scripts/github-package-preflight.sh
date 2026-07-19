#!/usr/bin/env bash

set -euo pipefail

manifest_path=${1:?Package manifest path is required.}
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
state_probe="$script_dir/github-package-state.sh"

# Fail before publishing any family member when the requested version already exists. Only an
# explicit HTTP 404 means a package is absent; authentication, authorization, network, and server
# failures abort the release. Package visibility is intentionally irrelevant.
while IFS=$'\t' read -r package_id version; do
  if ! state=$(GH_BIN="${GH_BIN:-gh}" bash "$state_probe" "$package_id" "$version"); then
    echo "GitHub Packages preflight failed for ${package_id}; refusing to publish."
    exit 1
  fi

  case $state in
    absent | version-absent)
      ;;
    version-present)
      echo "${package_id} ${version} already exists; refusing a partial duplicate release."
      exit 1
      ;;
    *)
      echo "GitHub Packages returned unexpected state '${state}' for ${package_id}; refusing to publish."
      exit 1
      ;;
  esac
done < "$manifest_path"
