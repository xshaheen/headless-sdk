#!/usr/bin/env bash

set -euo pipefail

scenario=${GH_FAKE_SCENARIO:?Fake GitHub scenario is required.}
arguments="$*"

if [[ $arguments == *" --include "* ]]; then
  if [[ -n ${GH_FAKE_STATE_DIR:-} ]]; then
    metadata_calls_file="$GH_FAKE_STATE_DIR/metadata-calls"
    metadata_calls=0
    if [[ -f $metadata_calls_file ]]; then
      metadata_calls=$(<"$metadata_calls_file")
    fi
    printf '%s\n' "$((metadata_calls + 1))" > "$metadata_calls_file"
  fi

  case $scenario in
    absent)
      echo "HTTP/2.0 404 Not Found"
      echo "gh: Package not found. (HTTP 404)"
      exit 1
      ;;
    forbidden)
      echo "HTTP/2.0 403 Forbidden"
      echo "gh: Resource not accessible. (HTTP 403)"
      exit 1
      ;;
    public)
      printf 'HTTP/2.0 200 OK\n\n{"visibility":"public"}\n'
      ;;
    missing-visibility)
      printf 'HTTP/2.0 200 OK\n\n{}\n'
      ;;
    unknown-visibility)
      printf 'HTTP/2.0 200 OK\n\n{"visibility":"internal"}\n'
      ;;
    private-duplicate | private-duplicate-early | postflight-private-present | postflight-delayed | postflight-version-failure | postflight-version-transient)
      printf 'HTTP/2.0 200 OK\n\n{"visibility":"private"}\n'
      ;;
    *)
      echo "Unknown fake GitHub scenario: ${scenario}"
      exit 2
      ;;
  esac
elif [[ $scenario == private-duplicate && $arguments == *"/second.package/versions"* ]]; then
  echo "1.0.0"
elif [[ $scenario == private-duplicate-early && $arguments == *"/first.package/versions"* ]]; then
  echo "1.0.0"
  for version in {1..10000}; do
    echo "0.0.${version}"
  done
elif [[ $scenario == postflight-private-present && $arguments == *"/first.package/versions"* ]]; then
  echo "1.0.0"
elif [[ $scenario == postflight-delayed && $arguments == *"/first.package/versions"* ]]; then
  state_dir=${GH_FAKE_STATE_DIR:?Fake GitHub state directory is required.}
  version_calls_file="$state_dir/version-calls"
  version_calls=0
  if [[ -f $version_calls_file ]]; then
    version_calls=$(<"$version_calls_file")
  fi
  version_calls=$((version_calls + 1))
  printf '%s\n' "$version_calls" > "$version_calls_file"
  if [[ $version_calls -ge 2 ]]; then
    echo "1.0.0"
  fi
elif [[ ( $scenario == postflight-version-failure || $scenario == postflight-version-transient ) && $arguments == *"/first.package/versions"* ]]; then
  state_dir=${GH_FAKE_STATE_DIR:?Fake GitHub state directory is required.}
  version_calls_file="$state_dir/version-calls"
  version_calls=0
  if [[ -f $version_calls_file ]]; then
    version_calls=$(<"$version_calls_file")
  fi
  version_calls=$((version_calls + 1))
  printf '%s\n' "$version_calls" > "$version_calls_file"
  if [[ $scenario == postflight-version-transient && $version_calls -ge 2 ]]; then
    echo "1.0.0"
  else
    echo "gh: transient version API failure. (HTTP 503)" >&2
    exit 1
  fi
fi
