#!/usr/bin/env bash

set -euo pipefail

scenario=${GH_FAKE_SCENARIO:?Fake GitHub scenario is required.}
arguments="$*"

if [[ $arguments == *" --include "* ]]; then
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
    private-duplicate | private-duplicate-early)
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
fi
