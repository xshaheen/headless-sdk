#!/usr/bin/env bash

set -euo pipefail

manifest_path=${1:?Package manifest path is required.}
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
state_probe="$script_dir/github-package-state.sh"

# Personal-account packages must be private. GitHub does not allow a public package to become
# private, so fail before publishing any member of the family. Only an explicit HTTP 404 means a
# package is absent; authentication, authorization, network, and server failures abort the release.
while IFS=$'\t' read -r package_id version; do
  if ! state=$(GH_BIN="${GH_BIN:-gh}" bash "$state_probe" "$package_id" "$version"); then
    echo "GitHub Packages preflight failed for ${package_id}; refusing to publish."
    exit 1
  fi

  case $state in
    absent | $'private\tversion-absent')
      ;;
    invalid-visibility)
      echo "GitHub Packages returned invalid visibility for ${package_id}; refusing to publish."
      exit 1
      ;;
    $'private\tversion-present')
      echo "${package_id} ${version} already exists; refusing a partial duplicate release."
      exit 1
      ;;
    $'non-private\t'*)
      visibility=${state#*$'\t'}
      if [[ $visibility == public ]]; then
        echo "${package_id} is already public and cannot receive an internal-only release."
      else
        echo "${package_id} has unsupported visibility '${visibility}'; expected private."
      fi
      exit 1
      ;;
    *)
      echo "GitHub Packages returned unexpected state '${state}' for ${package_id}; refusing to publish."
      exit 1
      ;;
  esac
done < "$manifest_path"
