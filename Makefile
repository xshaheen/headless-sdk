SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.DEFAULT_GOAL := help

DOTNET ?= dotnet
PYTHON ?= python3
SOLUTION ?= headless-sdk.slnx
CONFIGURATION ?= Release
TEST_PROJECT ?= tests/Headless.NET.Sdk.Tests.Integrations/Headless.NET.Sdk.Tests.Integrations.csproj
ARTIFACTS_DIR ?= artifacts
PROOF = $(PYTHON) eng/tools/proof.py
# One timestamp per make invocation, so every stage of a target writes into the same bundle.
RUN_ID := $(shell date -u +%Y%m%dT%H%M%SZ)
PROOF_RUN := $(ARTIFACTS_DIR)/proof/$(RUN_ID)
# The integration fixture refuses a package version that already sits in the NuGet cache, because a
# cached copy would shadow the packages under test. MinVer would stamp a local pack with a released
# version (or the same version on every run of one commit), so local packs get a unique prerelease.
# Resolved once per invocation: a recursive `?=` would re-run date per reference, and build and pack
# must stamp the same version.
ifeq ($(origin LOCAL_VERSION),undefined)
LOCAL_VERSION := 0.0.0-local.$(shell date -u +%s)
endif
# The last packed directory, so test-class can rerun against it without packing again.
LAST_PACKAGES = $(ARTIFACTS_DIR)/proof/last-packages
TEST_REPORT_ARGS = --report-trx --minimum-expected-tests 1
# Static tests read the repository and need no packed packages or consumer builds.
STATIC_TEST_CLASSES = --filter-class '*AnalyzerRuleCoverageTests' --filter-class '*VersionConsistencyTests'

.PHONY: help
help: ## Show available commands.
	@awk 'BEGIN {FS = ":.*##"; printf "\nCommands:\n"} /^[a-zA-Z0-9_.-]+:.*##/ { printf "  %-16s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@printf "\nExamples:\n  make verify\n  make test-static\n  make test-class CLASS='*ScaffoldingTests*'\n\n"

# A repository core.hooksPath replaces the global one, so on a machine whose global hooks already
# dispatch to .githooks the repository override is removed instead; elsewhere this worktree points
# at .githooks directly.
.PHONY: hooks
hooks: ## Wire the committed hooks in .githooks: through global hooks that dispatch to them, else directly.
	@global="$$(git config --global core.hooksPath || true)"; global="$${global/#\~/$$HOME}"; \
	if [ -n "$$global" ] && grep -qs '\.githooks/' "$$global/pre-commit" "$$global/pre-push"; then \
		git config --worktree --unset core.hooksPath 2>/dev/null || true; \
		git config --local --unset core.hooksPath 2>/dev/null || true; \
		echo "[hooks] global hooks in $$global run .githooks; repository override removed"; \
	else \
		git config --local extensions.worktreeConfig true; \
		git config --worktree core.hooksPath .githooks; \
		echo "[hooks] core.hooksPath set to .githooks for this worktree"; \
	fi

# The build fails on unformatted C#, so format staged files at commit time. Partially staged files
# are skipped: formatting the whole file would commit the unstaged hunks too.
.PHONY: hook-pre-commit
hook-pre-commit: ## Git hook: format staged C# files with CSharpier.
	@staged=(); safe=(); \
	while IFS= read -r file; do staged+=("$$file"); done < <(git diff --cached --name-only --diff-filter=ACMR -- '*.cs'); \
	[ "$${#staged[@]}" -gt 0 ] || exit 0; \
	for file in "$${staged[@]}"; do if git diff --quiet -- "$$file"; then safe+=("$$file"); else echo "[pre-commit] skipped partially staged $$file"; fi; done; \
	if [ "$${#safe[@]}" -gt 0 ]; then $(DOTNET) csharpier format "$${safe[@]}" >/dev/null; git add -- "$${safe[@]}"; fi

# Formatting only, on the C# files changed against upstream. No build: CI compiles, and a build here
# costs minutes per push.
.PHONY: hook-pre-push
hook-pre-push: ## Git hook: CSharpier-check the C# files changed against upstream.
	@base=$$(git rev-parse --verify -q '@{upstream}' 2>/dev/null || git merge-base origin/main HEAD 2>/dev/null || true); \
	files=(); while IFS= read -r file; do files+=("$$file"); done < <(if [ -n "$$base" ]; then git diff --name-only --diff-filter=ACMR "$$base"...HEAD -- '*.cs'; fi); \
	[ "$${#files[@]}" -gt 0 ] || { echo "[pre-push] no changed C# files to check"; exit 0; }; \
	$(DOTNET) csharpier check "$${files[@]}"

.PHONY: build
build: ## Restore and build the solution the way CI does (--no-incremental, no package generation).
	$(DOTNET) restore "$(SOLUTION)"
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -p:GeneratePackageOnBuild=false -p:MinVerVersionOverride=$(LOCAL_VERSION) -v:minimal -nologo

.PHONY: test-static
test-static: ## Run the repository-only test classes (analyzer rule coverage, version pins); seconds, no packing.
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" -- $(STATIC_TEST_CLASSES) --minimum-expected-tests 1

.PHONY: test-class
test-class: ## Run tests matching CLASS against the last packed packages (run `make verify` first).
	@test -n "$(CLASS)" || (echo "CLASS is required. Example: make test-class CLASS='*ScaffoldingTests*'" && exit 2)
	@test -d "$(LAST_PACKAGES)" || (echo "No packed packages yet. Run: make verify" && exit 2)
	HEADLESS_PACKAGES_DIR="$$(cd "$(LAST_PACKAGES)" && pwd -P)" \
		$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore --no-build -- --filter-class '$(CLASS)' --minimum-expected-tests 1

# The full CI sequence: build, pack all six packages under a unique local version, run the
# integration suite against them, then reduce the logs and TRX into artifacts/proof/<run>/summary.md
# (paste it into the PR) and summary.json. Every stage runs, so one failure does not hide the rest,
# except that tests are skipped when the build or pack failed rather than run against stale output.
.PHONY: verify
verify: ## Build, pack (unique local version), and run the full suite; writes a proof bundle (~3 min).
	@run="$(PROOF_RUN)-verify"; status=0; mkdir -p "$$run"; \
	$(PROOF) run --dir "$$run" --name restore -- $(DOTNET) restore "$(SOLUTION)" || status=1; \
	$(PROOF) run --dir "$$run" --name build -- $(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -p:GeneratePackageOnBuild=false -p:MinVerVersionOverride=$(LOCAL_VERSION) -v:minimal -nologo || status=1; \
	$(PROOF) run --dir "$$run" --name pack -- $(DOTNET) pack "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-build -p:MinVerVersionOverride=$(LOCAL_VERSION) --output "$$run/packages" -v:minimal -nologo || status=1; \
	if [ $$status -ne 0 ]; then \
		$(PROOF) skip --dir "$$run" --name tests --reason "an earlier stage failed; the packages under test would be stale"; \
	else \
		ln -sfn "$(RUN_ID)-verify/packages" "$(LAST_PACKAGES)"; \
		HEADLESS_PACKAGES_DIR="$(CURDIR)/$$run/packages" $(PROOF) run --dir "$$run" --name tests -- $(DOTNET) test "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-build -- $(TEST_REPORT_ARGS) --results-directory "$(CURDIR)/$$run/tests" || status=1; \
	fi; \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	cat "$$run/summary.md"; printf 'Proof bundle: %s (summary.md for the PR body, summary.json for tools)\n' "$$run"; exit $$status
