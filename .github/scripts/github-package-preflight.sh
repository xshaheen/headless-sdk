#!/usr/bin/env bash

set -euo pipefail

manifest_path=${1:?Package manifest path is required.}
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
state_probe="$script_dir/github-package-state.sh"

# Existing package IDs may be private or public. Fail before publishing any family member when the
# requested version already exists or GitHub returns an unsupported/malformed state. Only an
# explicit HTTP 404 means a package is absent; authentication, authorization, network, and server
# failures abort the release.
while IFS=$'\t' read -r package_id version; do
  if ! state=$(GH_BIN="${GH_BIN:-gh}" bash "$state_probe" "$package_id" "$version"); then
    echo "GitHub Packages preflight failed for ${package_id}; refusing to publish."
    exit 1
  fi

  case $state in
    absent | $'private\tversion-absent' | $'public\tversion-absent')
      ;;
    invalid-visibility)
      echo "GitHub Packages returned invalid visibility for ${package_id}; refusing to publish."
      exit 1
      ;;
    $'private\tversion-present' | $'public\tversion-present')
      echo "${package_id} ${version} already exists; refusing a partial duplicate release."
      exit 1
      ;;
    $'unsupported-visibility\t'*)
      visibility=${state#*$'\t'}
      echo "${package_id} has unsupported visibility '${visibility}'; expected private or public."
      exit 1
      ;;
    *)
      echo "GitHub Packages returned unexpected state '${state}' for ${package_id}; refusing to publish."
      exit 1
      ;;
  esac
done < "$manifest_path"
