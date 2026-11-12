# Project CLI for the Headless MSBuild SDK packages. A library: nothing for `make up` to start, so
# doctor reports run and data as `library, no runtime`. `make help` lists the targets.

# make 3.81 (macOS) ignores .SHELLFLAGS, so strict mode rides on SHELL.
SHELL := /bin/bash -eu -o pipefail
.DEFAULT_GOAL := help
MAKEFLAGS += -r --no-print-directory
.SUFFIXES:

# Agent and IDE shells are not login shells, so on macOS /etc/paths.d (the SDK's install folder)
# never reaches them; append it rather than report dotnet missing.
ifneq ($(wildcard /usr/local/share/dotnet/dotnet),)
export PATH := $(PATH):/usr/local/share/dotnet
endif

DOTNET ?= dotnet
PYTHON ?= python3
SOLUTION ?= headless-sdk.slnx
CONFIGURATION ?= Release
TEST_PROJECT ?= tests/Headless.NET.Sdk.Tests.Integrations/Headless.NET.Sdk.Tests.Integrations.csproj
ARTIFACTS_DIR ?= artifacts
PROOF = $(PYTHON) eng/tools/proof.py
# Proof bundles kept per run of verify; older ones are deleted when verify starts (each holds the six
# packages and the TRX files).
PROOF_KEEP ?= 3
CHECK_GATES ?= format-check test-static verify
HOOK_BASE_REF ?=
CLASS ?=
JSON ?=
# One timestamp per make invocation, so every stage of a target writes into the same bundle. Memoized:
# `date` runs on first use only, never for help, doctor, or a hook.
RUN_ID = $(eval RUN_ID := $(shell date -u +%Y%m%dT%H%M%SZ))$(RUN_ID)
PROOF_RUN = $(ARTIFACTS_DIR)/proof/$(RUN_ID)
# The integration fixture refuses a package version that already sits in the NuGet cache, because a
# cached copy would shadow the packages under test. MinVer would stamp a local pack with a released
# version (or the same version on every run of one commit), so local packs get a unique prerelease.
# Memoized once per invocation, because build and pack must stamp the same version.
ifeq ($(origin LOCAL_VERSION),undefined)
LOCAL_VERSION = $(eval LOCAL_VERSION := 0.0.0-local.$(shell date -u +%s))$(LOCAL_VERSION)
endif
# The last packed directory, so test-class can rerun against it without packing again.
LAST_PACKAGES = $(ARTIFACTS_DIR)/proof/last-packages
TEST_REPORT_ARGS = --report-trx --minimum-expected-tests 1
# Static tests read the repository and need no packed packages or consumer builds.
STATIC_TEST_CLASSES = --filter-class '*AnalyzerRuleCoverageTests' --filter-class '*VersionConsistencyTests'

# CI's release path stays in the workflows: it packs once, then shards one immutable package
# artifact across Linux test jobs and Windows and macOS smoke jobs. `make verify` runs the same
# restore, build, pack, and suite on this machine, and `make test-class` runs any one shard or smoke
# class (CLASS='*WindowsPlatformContractTests').
# cli-exclude: ci:dotnet restore tests/* -- the per-shard harness restore of validate.yml and platform-smoke.yml; make restore covers it locally
# cli-exclude: ci:dotnet build tests/* -- the per-shard harness build in CI; make verify builds the harness locally
# cli-exclude: ci:dotnet test tests/* -- the CI shards and platform smoke run filtered slices of the suite; make verify runs all of it, make test-class one slice
# cli-exclude: ci:dotnet pack * -- CI packs immutable release artifacts with the tag's MinVer version; make verify packs under a unique local version
# cli-exclude: ci:dotnet nuget push * -- publishing is external and irreversible; publish.yml owns it behind a tag or release
# cli-exclude: ci:sha256sum * -- release artifact hashing inside publish.yml
# cli-exclude: ci:gh run * -- release orchestration inside publish.yml

# Runtime files go to CLI_DIR, untracked: the checkout's .local/cli/<checkout>/ when a wiki exists, else a
# temp folder so CI still runs. Memoized so help and doctor never fork.
ITEM ?=
REPO_ROOT = $(eval REPO_ROOT := $(or $(shell git rev-parse --show-toplevel 2>/dev/null),$(CURDIR)))$(REPO_ROOT)
CLI_KEY = $(eval CLI_KEY := $(shell $(cli_key)))$(CLI_KEY)
WIKI_ROOT = $(eval WIKI_ROOT := $(shell $(find_wiki)))$(WIKI_ROOT)
WORK_ITEM = $(eval WORK_ITEM := $(shell $(find_item)))$(WORK_ITEM)
CLI_TMP_DIR = $(eval CLI_TMP_DIR := $(shell $(cli_tmp_dir)))$(CLI_TMP_DIR)
CLI_DIR ?= $(if $(WIKI_ROOT),$(REPO_ROOT)/.local/cli/$(CLI_KEY),$(CLI_TMP_DIR))
EVIDENCE_DIR ?= $(if $(WIKI_ROOT),$(REPO_ROOT)/.local/work/$(WORK_ITEM)/evidence)
RESULTS_DIR ?= $(CLI_DIR)/test-results

# The nearest .wiki/ at or above REPO_ROOT, checking $HOME itself and nothing above it; empty when none.
define find_wiki
d='$(REPO_ROOT)'; h=$$(cd "$$HOME" 2>/dev/null && pwd -P || true); while :; do if [ -d "$$d/.wiki" ]; then printf '%s' "$$d/.wiki"; break; fi; if [ "$$d" = "$$h" ] || [ "$$d" = / ]; then break; fi; d=$$(dirname "$$d"); done
endef

# Reuse an exact item folder before a title-bearing ITEM-* folder, so CLI captures stay with the item.
define find_item
i='$(ITEM)'; if [ -n "$$i" ]; then r='$(REPO_ROOT)/.local/work'; if [ ! -d "$$r/$$i" ]; then for d in "$$r/$$i-"*; do [ -d "$$d" ] || continue; i=$${d##*/}; break; done; fi; else b=$$(git symbolic-ref -q --short HEAD 2>/dev/null || true); i=$${b##*/}; fi; printf '%s' "$${i:-adhoc}"
endef

# The checkout's folder name and a checksum of its full path.
define cli_key
r='$(REPO_ROOT)'; printf '%s-%s' "$${r##*/}" "$$(printf '%s' "$$r" | cksum | cut -d ' ' -f 1)"
endef

# Stable per checkout; said on stderr so a JSON=1 report stays clean.
define cli_tmp_dir
t="$${TMPDIR:-/tmp}"; d="$${t%/}/cli-$(CLI_KEY)"; echo "make: no .wiki/ at or above $(REPO_ROOT); runtime files go to $$d. Run x-project-init to keep them in the repo's .local/." >&2; printf '%s' "$$d"
endef

# The solution's projects, one per line, from the .slnx (its paths use backslashes).
define slnx_projects
sed -n 's/.*<Project Path="\([^"]*\)".*/\1/p' "$(SOLUTION)" | tr '\\' '/'
endef

# Prints the solution projects whose restore is missing or older than their project file or the
# central props; empty when restore is current. Read by build and doctor instead of restoring.
define stale_restores
while IFS= read -r p; do a="$${p%/*}/obj/project.assets.json"; if [ ! -f "$$a" ] || [ -n "$$(find "$$p" Directory.Packages.props Directory.Build.props -newer "$$a" 2>/dev/null)" ]; then printf '%s ' "$${p##*/}"; fi; done < <($(slnx_projects))
endef

##@ Start here

.PHONY: help
help: ## Show targets and examples
	@awk 'BEGIN {FS = ":.*##"} /^##@/ {printf "\n%s\n", substr($$0, 5)} /^[a-zA-Z0-9_.-]+:.*##/ {printf "  %-18s %s\n", $$1, $$2}' $(MAKEFILE_LIST)
	@printf '\nConventions:\n  JSON=1 prints doctor as the contract report; CLASS narrows test-class (an MTP --filter-class glob);\n  LOCAL_VERSION overrides the unique local pack version; PROOF_KEEP bounds the kept proof bundles; NO_COLOR is honored.\n'
	@printf '\nExamples:\n  make test-static\n  make test-class CLASS='"'"'*ScaffoldingTests*'"'"'\n  make verify-affected\n  make doctor JSON=1\n'

# doctor report. stdin: one check per line, `group|component|check|status|reason|fix`
# (status: ok fail missing gated unavailable). Table, or the contract JSON when JSON=1.
# Exits 4 when a tool is missing, 1 when a check failed, else 0.
define doctor_report
awk -F'|' -v json="$(JSON)" ' \
function q(s) { gsub(/\\/, "\\\\", s); gsub(/"/, "\\\"", s); return "\"" s "\"" } \
BEGIN { n = split("run data quality observe debug invariants", G, " ") } \
{ c++; g[c] = $$1; comp[c] = $$2; chk[c] = $$3; st[c] = $$4; why[c] = $$5; fix[c] = $$6; \
  if ($$4 == "missing") code = 4; else if ($$4 == "fail" && code != 4) code = 1; \
  if ($$4 == "fail" || $$4 == "missing" || $$4 == "unavailable") { if (!($$1 in bad)) bad[$$1] = $$3 ": " $$5 } \
  else if ($$4 == "gated") { if (!($$1 in gate)) gate[$$1] = $$3 ": " $$5 } else if ($$4 == "ok") seen[$$1] = 1 } \
END { for (i = 1; i <= n; i++) { grp = G[i]; \
    if (grp in bad) { gs[grp] = "unavailable"; gr[grp] = bad[grp] } \
    else if (grp in gate) { gs[grp] = "gated"; gr[grp] = gate[grp] } \
    else if (grp in seen) { gs[grp] = "available"; gr[grp] = "" } \
    else { gs[grp] = "unavailable"; gr[grp] = "no check declared for this group" } } \
  if (json) { printf "{\"contract\": 1, \"ok\": %s, \"exit\": %d, \"groups\": {", (code ? "false" : "true"), code; \
    for (i = 1; i <= n; i++) printf "%s%s: {\"status\": %s, \"reason\": %s}", (i > 1 ? ", " : ""), q(G[i]), q(gs[G[i]]), q(gr[G[i]]); \
    printf "}, \"checks\": ["; \
    for (i = 1; i <= c; i++) printf "%s{\"group\": %s, \"component\": %s, \"check\": %s, \"status\": %s, \"reason\": %s, \"fix\": %s}", \
      (i > 1 ? ", " : ""), q(g[i]), q(comp[i]), q(chk[i]), q(st[i]), q(why[i]), q(fix[i]); \
    print "]}" } \
  else { for (i = 1; i <= c; i++) printf "%-11s %-10s %-12s %-16s %s%s\n", st[i], g[i], comp[i], chk[i], why[i], (fix[i] != "" ? "  -> " fix[i] : ""); \
    for (i = 1; i <= n; i++) printf "group %-10s %s%s\n", G[i], gs[G[i]], (gr[G[i]] != "" ? ": " gr[G[i]] : "") } \
  exit code }'
endef

# tool <group> <component> <tool> <fix>: ok with the tool's version line, else missing.
# bounded <seconds> <cmd...>: coreutils timeout when present, else perl's alarm (stock macOS has no timeout).
define doctor_tool
tool() { if command -v "$$3" >/dev/null 2>&1; then echo "$$1|$$2|$$3|ok|$$("$$3" --version 2>/dev/null | head -n 1 | tr -d '|')|"; else echo "$$1|$$2|$$3|missing|$$3 is not on PATH|$$4"; fi; }; \
bounded() { if command -v timeout >/dev/null 2>&1; then timeout "$$@"; else perl -e 'alarm shift; exec @ARGV' "$$@"; fi; }
endef

# Every probe is a PATH lookup, a --version, or a file test, so doctor answers in about a second.
# CSharpier is proven restored from the NuGet cache (what `dotnet tool restore` fills) instead of
# launching the tool. observe and invariants are recorded for every repo this wiki serves, but a
# package repo has no sink to read and no database to hold business rules, so both stay unavailable
# with that reason and no target.
.PHONY: doctor
doctor: ## Check prerequisites per capability group, each with its fix; JSON=1 prints the contract report
	@{ $(doctor_tool); \
	  if ! command -v $(DOTNET) >/dev/null 2>&1; then echo "quality|dotnet|sdk|missing|$(DOTNET) is not on PATH|https://dot.net"; \
	  elif v=$$(bounded 10 $(DOTNET) --version 2>/dev/null); then echo "quality|dotnet|sdk|ok|SDK $$v|"; \
	  else echo "quality|dotnet|sdk|fail|no installed SDK satisfies global.json|install the SDK global.json names: https://dot.net"; fi; \
	  if [ -f "$(SOLUTION)" ]; then echo "quality|dotnet|solution|ok|$(SOLUTION)|"; else echo "quality|dotnet|solution|fail|$(SOLUTION) not found|make SOLUTION=<path>.slnx"; fi; \
	  if [ -f "$(SOLUTION)" ]; then s=$$($(stale_restores)); s="$${s% }"; if [ -z "$$s" ]; then echo "quality|dotnet|restore|ok|every solution project restored|"; else echo "quality|dotnet|restore|gated|not restored: $$s|make restore"; fi; fi; \
	  v=$$(sed -n '/"csharpier"/,/}/p' dotnet-tools.json 2>/dev/null | sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' | head -n 1 || true); \
	  if [ -n "$$v" ] && [ -d "$${NUGET_PACKAGES:-$$HOME/.nuget/packages}/csharpier/$$v" ]; then echo "quality|dotnet|csharpier|ok|$$v restored|"; else echo "quality|dotnet|csharpier|gated|csharpier $${v:-(not pinned)} not restored|make tools"; fi; \
	  tool quality proof $(PYTHON) 'install Python 3: https://www.python.org/downloads/'; \
	  p=$$(git config core.hooksPath 2>/dev/null || true); p="$${p/#\~/$$HOME}"; \
	  if [ "$$p" = .githooks ] || { [ -n "$$p" ] && grep -qs '\.githooks' "$$p/pre-commit" "$$p/pre-push"; }; then echo "quality|git|hooks|ok|$$p runs .githooks|"; else echo "quality|git|hooks|gated|core.hooksPath does not run .githooks|make hooks"; fi; \
	  echo "run|runtime|library|unavailable|library, no runtime|"; \
	  echo "data|runtime|library|unavailable|library, no runtime|"; \
	  echo "observe|runtime|sink|unavailable|library, no runtime: no log, trace, or metric sink to read|"; \
	  echo "invariants|wiki|files|unavailable|no invariant files under $(if $(WIKI_ROOT),$(WIKI_ROOT),.wiki)/tests/invariants; library, no database to hold business rules|"; \
	} | $(doctor_report)

##@ Daily loop

# Seconds: the two repository-only classes need no packed packages or consumer builds.
.PHONY: test-static
test-static: ## Run the repository-only test classes (analyzer rule coverage, version pins); seconds, no packing
	@rm -rf "$(RESULTS_DIR)/static"
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" -- $(STATIC_TEST_CLASSES) --minimum-expected-tests 1 --results-directory "$(RESULTS_DIR)/static"

.PHONY: test-class
test-class: ## Run tests matching CLASS= against the last packed packages; narrows what runs, not what builds (make verify first)
	@test -n "$(CLASS)" || { echo "CLASS is required. Example: make test-class CLASS='*ScaffoldingTests*'" >&2; exit 2; }
	@test -d "$(LAST_PACKAGES)" || { echo "No packed packages yet. Run: make verify" >&2; exit 1; }
	@rm -rf "$(RESULTS_DIR)/class"
	HEADLESS_PACKAGES_DIR="$$(cd "$(LAST_PACKAGES)" && pwd -P)" \
		$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore --no-build -- --filter-class '$(CLASS)' --minimum-expected-tests 1 --results-directory "$(RESULTS_DIR)/class"

# Incremental and without MinVer's override, so an unchanged tree compiles nothing; asserts restore
# instead of restoring (restore is the largest fixed cost and almost never has work to do).
.PHONY: build
build: ## Incremental build after make restore; full log in CLI_DIR/build.log
	@s=$$($(stale_restores)); [ -z "$$s" ] || { echo "packages not restored for: $$s. Run: make restore" >&2; exit 1; }
	@mkdir -p "$(CLI_DIR)"
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -p:GeneratePackageOnBuild=false -v:q -nologo -flp:logfile="$(CLI_DIR)/build.log"

##@ Before a PR

# This repo has one solution and one test project, so there is no affected routing: the local gate
# is check's gates, and verify inside it writes the proof bundle for the PR.
.PHONY: verify-affected
verify-affected: ## Local gate before publishing: check's gates (format-check, test-static, verify); no affected routing here
	@$(MAKE) check

# Loops instead of listing the gates as prerequisites so one failed gate does not hide the next;
# a dry run still only prints because each sub-make inherits -n.
.PHONY: check
check: ## CI gate: runs every gate in CHECK_GATES (format-check test-static verify) and reports each failure
	@failed=; for t in $(CHECK_GATES); do $(MAKE) $$t || failed="$$failed $$t"; done; \
	  [ -z "$$failed" ] || { echo "check: failed:$$failed" >&2; exit 1; }; echo "check: passed: $(CHECK_GATES)"

# The full CI sequence: build, pack all six packages under a unique local version, run the
# integration suite against them, then reduce the logs and TRX into artifacts/proof/<run>/summary.md
# (paste it into the PR) and summary.json. Every stage runs, so one failure does not hide the rest,
# except that tests are skipped when the build or pack failed rather than run against stale output.
# Bundles sort by their timestamp name; the newest PROOF_KEEP - 1 stay, so the new one makes PROOF_KEEP.
.PHONY: verify
verify: ## Build, pack (unique local version), and run the full suite; writes a proof bundle (~3 min)
	@run="$(PROOF_RUN)-verify"; status=0; \
	{ find "$(ARTIFACTS_DIR)/proof" -mindepth 1 -maxdepth 1 -type d -name '*-verify' 2>/dev/null || true; } | sort -r | awk -v keep=$$(( $(PROOF_KEEP) - 1 )) 'NR > keep' | while IFS= read -r old; do rm -rf -- "$$old"; done; \
	mkdir -p "$$run"; \
	$(PROOF) run --dir "$$run" --name restore -- $(DOTNET) restore "$(SOLUTION)" || status=1; \
	$(PROOF) run --dir "$$run" --name build -- $(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -p:GeneratePackageOnBuild=false -p:MinVerVersionOverride=$(LOCAL_VERSION) -v:minimal -nologo || status=1; \
	$(PROOF) run --dir "$$run" --name pack -- $(DOTNET) pack "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-build -p:MinVerVersionOverride=$(LOCAL_VERSION) --output "$$run/packages" -v:minimal -nologo || status=1; \
	if [ $$status -ne 0 ]; then \
		$(PROOF) skip --dir "$$run" --name tests --reason "an earlier stage failed; the packages under test would be stale"; \
	else \
		ln -sfn "$${run##*/}/packages" "$(LAST_PACKAGES)"; \
		HEADLESS_PACKAGES_DIR="$(CURDIR)/$$run/packages" $(PROOF) run --dir "$$run" --name tests -- $(DOTNET) test "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-build -- $(TEST_REPORT_ARGS) --results-directory "$(CURDIR)/$$run/tests" || status=1; \
	fi; \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	cat "$$run/summary.md"; printf 'Proof bundle: %s (summary.md for the PR body, summary.json for tools)\n' "$$run"; exit $$status

.PHONY: format
format: ## Write formatting with CSharpier (C# and the MSBuild files it formats)
	$(DOTNET) csharpier format .

# .csharpierignore excludes .wiki/ and .local/, which no .gitignore covers. Exit 3 on drift; a
# CSharpier failure that names no file exits 1 rather than passing.
.PHONY: format-check
format-check: ## Report formatting drift without writing; exit 3 on drift
	@out=$$($(DOTNET) csharpier check . 2>&1) && rc=0 || rc=$$?; \
	[ -z "$$out" ] || printf '%s\n' "$$out"; \
	if [ "$$rc" -ne 0 ]; then if printf '%s\n' "$$out" | grep -q 'Was not formatted'; then echo "format-check: drift found. Run: make format" >&2; exit 3; fi; echo "format-check: csharpier exited $$rc without naming a file" >&2; exit 1; fi

##@ Setup once

.PHONY: bootstrap
bootstrap: tools restore hooks ## Fresh clone or worktree: tools, restore, hooks
	@echo "Next: make doctor"

.PHONY: tools
tools: ## Restore the repo-pinned .NET tools (dotnet-tools.json: CSharpier)
	$(DOTNET) tool restore

# The same restore CI runs. A no-op restore leaves project.assets.json untouched, which would keep the
# restore assertion stale after a branch switch moves the props files, so a successful restore touches
# every tracked project's assets file (-c creates none).
.PHONY: restore
restore: ## Restore the solution's packages
	$(DOTNET) restore "$(SOLUTION)"
	@git -c core.quotePath=false ls-files -z -- '*.csproj' | while IFS= read -r -d '' p; do touch -c "$${p%/*}/obj/project.assets.json"; done

# A repository core.hooksPath replaces the global one, so on a machine whose global hooks already
# dispatch to .githooks the repository override is removed instead; elsewhere this worktree points
# at .githooks directly.
.PHONY: hooks
hooks: ## Wire the committed hooks in .githooks: through global hooks that dispatch to them, else directly
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

##@ Git hooks (run by .githooks)

# The build fails on unformatted C#, so format staged files at commit time. A partially staged file is
# skipped and named: formatting the whole file would commit its unstaged hunks. CSharpier prints how
# many files it read, not which it changed, so its output is kept for failures and the hook names
# only the files whose bytes changed.
.PHONY: hook-pre-commit
hook-pre-commit: ## Git hook: format staged C# files with CSharpier, re-stage them, and name the files it changed
	@safe=(); skipped=(); \
	while IFS= read -r -d '' f; do if git diff --quiet -- "$$f"; then safe+=("$$f"); else skipped+=("$$f"); fi; done \
	  < <(git -c core.quotePath=false diff --cached -z --name-only --diff-filter=ACMR -- '*.cs'); \
	if [ "$${#safe[@]}" -gt 0 ]; then \
	  out=$$($(DOTNET) csharpier format "$${safe[@]}" 2>&1) || { printf '%s\n' "$$out" >&2; exit 1; }; \
	  changed=$$(git -c core.quotePath=false diff --name-only -- "$${safe[@]}"); \
	  if [ -n "$$changed" ]; then echo "[pre-commit] formatted and re-staged:"; printf '%s\n' "$$changed" | sed 's/^/  /'; fi; \
	  git add -- "$${safe[@]}"; \
	fi; \
	if [ "$${#skipped[@]}" -gt 0 ]; then for f in "$${skipped[@]}"; do echo "[pre-commit] skipped partially staged $$f" >&2; done; fi

# Formatting only, on the C# files changed against upstream. No build: CI compiles, and a build here
# costs minutes per push. HOOK_BASE_REF= overrides the base.
.PHONY: hook-pre-push
hook-pre-push: ## Git hook: CSharpier-check the C# files changed against upstream; no build, no tests
	@base="$(HOOK_BASE_REF)"; \
	[ -n "$$base" ] || base=$$(git rev-parse --verify -q '@{upstream}' 2>/dev/null || git merge-base origin/main HEAD 2>/dev/null || true); \
	files=(); while IFS= read -r -d '' f; do files+=("$$f"); done < <(if [ -n "$$base" ]; then git -c core.quotePath=false diff -z --name-only --diff-filter=ACMR "$$base"...HEAD -- '*.cs'; fi); \
	[ "$${#files[@]}" -gt 0 ] || { echo "[pre-push] no changed C# files to check"; exit 0; }; \
	echo "[pre-push] CSharpier check on $${#files[@]} changed C# file(s); compilation and tests are not checked here (run: make build, make test-static)"; \
	$(DOTNET) csharpier check "$${files[@]}"

##@ Rare

.PHONY: rebuild
rebuild: ## Clean build without incremental compilation, as CI's pack job builds
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -p:GeneratePackageOnBuild=false -v:minimal -nologo

# The root .editorconfig is generated from the shipped analyzer configs, because this repo's own
# projects use plain Microsoft.NET.Sdk; root_editorconfig_should_match_the_injected_configs fails
# until it is rerun.
.PHONY: sync-editorconfig
sync-editorconfig: ## Regenerate the root .editorconfig from the shipped analyzer configs (writes tracked files)
	$(PYTHON) eng/tools/sync_root_editorconfig.py

.PHONY: clean
clean: ## Remove build outputs (bin/, obj/ contents)
	$(DOTNET) clean "$(SOLUTION)" --configuration "$(CONFIGURATION)" -v:q -nologo
