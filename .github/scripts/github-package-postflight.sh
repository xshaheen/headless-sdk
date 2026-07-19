#!/usr/bin/env bash

set -euo pipefail

manifest_path=${1:?Package manifest path is required.}
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
state_probe="$script_dir/github-package-state.sh"
attempts=${GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS:-12}
delay_seconds=${GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS:-5}

if [[ ! $attempts =~ ^[1-9][0-9]*$ ]]; then
  echo "GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS must be a positive integer." >&2
  exit 2
fi
if [[ ! $delay_seconds =~ ^[0-9]+$ ]]; then
  echo "GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS must be a non-negative integer." >&2
  exit 2
fi

while IFS=$'\t' read -r package_id version; do
  verified=false
  attempt=1

  # GitHub's package API can lag a successful NuGet push. Retry only observation; never retry the
  # push itself or weaken duplicate-version protection.
  while [[ $attempt -le $attempts ]]; do
    if ! state=$(GH_BIN="${GH_BIN:-gh}" bash "$state_probe" "$package_id" "$version"); then
      echo "GitHub Packages observation failed for ${package_id} ${version} (${attempt}/${attempts})." >&2
    else
      case $state in
        version-present)
          verified=true
          break
          ;;
        absent | version-absent)
          ;;
        *)
          echo "GitHub Packages returned unexpected state '${state}' for ${package_id}."
          exit 1
          ;;
      esac
    fi

    if [[ $attempt -lt $attempts ]]; then
      echo "Waiting for GitHub Packages to expose ${package_id} ${version} (${attempt}/${attempts})."
      sleep "$delay_seconds"
    fi
    attempt=$((attempt + 1))
  done

  if [[ $verified != true ]]; then
    echo "GitHub Packages did not verify ${package_id} ${version} after ${attempts} checks."
    exit 1
  fi
done < "$manifest_path"
