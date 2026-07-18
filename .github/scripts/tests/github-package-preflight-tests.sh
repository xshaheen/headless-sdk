#!/usr/bin/env bash

set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
preflight="$repository_root/.github/scripts/github-package-preflight.sh"
fake_gh="$repository_root/.github/scripts/tests/fake-gh.sh"
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT

printf 'First.Package\t1.0.0\n' > "$test_root/one-package.tsv"
printf 'First.Package\t1.0.0\nSecond.Package\t1.0.0\n' > "$test_root/two-packages.tsv"

GH_BIN="$fake_gh" GH_FAKE_SCENARIO=absent bash "$preflight" "$test_root/one-package.tsv"

if output=$(GH_BIN="$fake_gh" GH_FAKE_SCENARIO=forbidden bash "$preflight" "$test_root/one-package.tsv" 2>&1); then
  echo "Expected a forbidden API response to fail preflight."
  exit 1
fi
grep -Fq "preflight failed" <<<"$output"

if output=$(GH_BIN="$fake_gh" GH_FAKE_SCENARIO=public bash "$preflight" "$test_root/one-package.tsv" 2>&1); then
  echo "Expected a public package to fail preflight."
  exit 1
fi
grep -Fq "already public" <<<"$output"

if output=$(GH_BIN="$fake_gh" GH_FAKE_SCENARIO=missing-visibility bash "$preflight" "$test_root/one-package.tsv" 2>&1); then
  echo "Expected missing package visibility to fail preflight."
  exit 1
fi
grep -Fq "invalid visibility" <<<"$output"

if output=$(GH_BIN="$fake_gh" GH_FAKE_SCENARIO=unknown-visibility bash "$preflight" "$test_root/one-package.tsv" 2>&1); then
  echo "Expected unsupported package visibility to fail preflight."
  exit 1
fi
grep -Fq "expected private" <<<"$output"

if output=$(
  GH_BIN="$fake_gh" GH_FAKE_SCENARIO=private-duplicate \
    bash "$preflight" "$test_root/two-packages.tsv" 2>&1
); then
  echo "Expected a later duplicate version to fail preflight."
  exit 1
fi
grep -Fq "Second.Package 1.0.0 already exists" <<<"$output"

if output=$(
  GH_BIN="$fake_gh" GH_FAKE_SCENARIO=private-duplicate-early \
    bash "$preflight" "$test_root/one-package.tsv" 2>&1
); then
  echo "Expected an early duplicate in a long version list to fail preflight."
  exit 1
fi
grep -Fq "First.Package 1.0.0 already exists" <<<"$output"

echo "GitHub package preflight tests passed."
