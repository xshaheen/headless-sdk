#!/usr/bin/env bash

set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
postflight="$repository_root/.github/scripts/github-package-postflight.sh"
fake_gh="$repository_root/.github/scripts/tests/fake-gh.sh"
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT

printf 'First.Package\t1.0.0\n' > "$test_root/one-package.tsv"

GH_BIN="$fake_gh" GH_FAKE_SCENARIO=postflight-present \
  GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS=3 GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS=0 \
  bash "$postflight" "$test_root/one-package.tsv"

mkdir "$test_root/delayed"
GH_BIN="$fake_gh" GH_FAKE_SCENARIO=postflight-delayed GH_FAKE_STATE_DIR="$test_root/delayed" \
  GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS=3 GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS=0 \
  bash "$postflight" "$test_root/one-package.tsv"
[[ $(<"$test_root/delayed/version-calls") == 2 ]]

mkdir "$test_root/transient"
GH_BIN="$fake_gh" GH_FAKE_SCENARIO=postflight-version-transient GH_FAKE_STATE_DIR="$test_root/transient" \
  GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS=3 GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS=0 \
  bash "$postflight" "$test_root/one-package.tsv"
[[ $(<"$test_root/transient/version-calls") == 2 ]]

mkdir "$test_root/api-timeout"
if output=$(
  GH_BIN="$fake_gh" GH_FAKE_SCENARIO=postflight-version-failure GH_FAKE_STATE_DIR="$test_root/api-timeout" \
    GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS=3 GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS=0 \
    bash "$postflight" "$test_root/one-package.tsv" 2>&1
); then
  echo "Expected exhausted version API failures to fail postflight."
  exit 1
fi
grep -Fq "version query failed" <<<"$output"
grep -Fq "did not verify First.Package 1.0.0 after 3 checks" <<<"$output"
[[ $(<"$test_root/api-timeout/version-calls") == 3 ]]

mkdir "$test_root/timeout"
if output=$(
  GH_BIN="$fake_gh" GH_FAKE_SCENARIO=absent GH_FAKE_STATE_DIR="$test_root/timeout" \
    GITHUB_PACKAGE_POSTFLIGHT_ATTEMPTS=2 GITHUB_PACKAGE_POSTFLIGHT_DELAY_SECONDS=0 \
    bash "$postflight" "$test_root/one-package.tsv" 2>&1
); then
  echo "Expected absent package state to time out postflight."
  exit 1
fi
grep -Fq "did not verify First.Package 1.0.0 after 2 checks" <<<"$output"
[[ $(<"$test_root/timeout/metadata-calls") == 2 ]]

echo "GitHub package postflight tests passed."
