# Repository Guidelines

## What this repository is

**headless-sdk** builds the Headless MSBuild SDK family: `Headless.NET.Sdk` plus its `.Web`, `.Test`, `.Razor`, `.BlazorWebAssembly`, and `.WindowsDesktop` satellites. The packages ship MSBuild props and targets, injected analyzer configs, banned-symbol lists, scaffold files, and one piece of compiled code: the Headless analyzers (`HLS*` rules) and their code fixes, under `build/analyzers/`. Any .NET repository can consume them. headless-framework is one consumer: it pins a version in `global.json`, so a change here reaches it only after a release and a pin bump.

The product is the consumer contract, and the README's **Support contract** section is its source of truth. Read that section before you change anything under `src/`. Two consequences guide most decisions:

- **Every consumption mode is first-class.** A change must behave the same under PackageReference and MSBuild-SDK consumption, in single- and multi-targeting builds, and for every satellite. Prove it with a consumer-build test, not by reading the targets.
- **Policies are not consumer knobs.** Analyzer infrastructure and quality gates stay on. Add an opt-out only where the README already documents one, such as the banned-symbol lists. The local inner-loop switches `RunAnalyzersDuringBuild` and `RunAnalyzersDuringLiveAnalysis` are honored only outside CI and AI-agent builds, which force them back on.

## Where a change lands

- `src/Headless.NET.Sdk/build` and `configurations` are packed into every satellite (`src/_shared/Headless.NET.Sdk.Satellite.nuspec`). One edit there changes all six packages.
- `src/_shared` owns satellite packaging and metadata. A satellite's own `build/` and `sdk/` folders hold only its project-type wrapper.
- `src/Headless.NET.Sdk.Analyzers` and `.CodeFixes` are never packed on their own: `Directory.Build.targets` builds them and every nuspec copies the two assemblies into `build/analyzers/`. They compile against Roslyn 4.14 through `VersionOverride`, with no central pin, so older compilers can still load them; do not raise it to the test project's Roslyn.
- `tests/Headless.NET.Sdk.Tests.Integrations` is the only test project. Most tests pack the six packages and build throwaway consumer projects against them.

## Build and test

Use the `make` targets; `make help` lists them.

- **`make test-static`** runs the repository-only test classes (analyzer rule coverage, version pins, the HLS analyzer unit tests) in seconds. Run it after any edit to a config file or a version pin.
- **`make verify`** runs the CI sequence: restore, `--no-incremental` build, pack, then the full suite, which takes minutes. It writes `artifacts/proof/<run>/summary.md`. Paste that summary into the PR description.
- **`make test-class CLASS='*Name*'`** reruns matching tests against the packages from the last `make verify`. Those packages go stale when anything under `src/` changes, so rerun `make verify` first.

## Rules the code does not state

- **Local packs need a unique version.** The integration fixture refuses a package version that already exists in `~/.nuget/packages`, because the cached copy would shadow the packages under test. The Makefile packs as `0.0.0-local.<unix-time>`. When you pack by hand, pass `-p:MinVerVersionOverride=<x.y.z>-local.<n>`.
- **The build fails on unformatted C#.** `CSharpier.MSBuild` reports `Was not formatted` as a build error and does not fix the file. Run `dotnet tool restore`, then `dotnet csharpier format <path>`. After `make hooks`, the pre-commit hook formats staged C# files and the pre-push hook checks the C# files changed against upstream; neither builds. Keep the CSharpier version in `dotnet-tools.json` equal to the `CSharpier.MSBuild` version in `Directory.Packages.props`.
- **Agent warnings-as-errors does not apply here.** `SupportDetectLlmContext.props` turns warnings into errors when an agent drives a consumer build. It is product behavior, covered by the integration tests. This repository's own projects use plain `Microsoft.NET.Sdk`, so it does not affect your builds.
- **Severities live only in the injected configs.** Edit `Headless.NET.Sdk.Analyzers.editorconfig` or `Headless.NET.Sdk.Tests.editorconfig`; each keeps one section per analyzer family with rules sorted by ID. The compiler and ReSharper's `jb inspectcode` both read them. `configurations/editorconfig.txt` is an editor-only scaffold, because a consumer `.editorconfig` outranks the SDK and a copied severity would pin that repository to the SDK version it was copied from. `editorconfig_scaffold_should_not_carry_analyzer_settings` fails if one is added. This repository's own root `.editorconfig` is generated from those files by `python3 eng/tools/sync_root_editorconfig.py`, because its projects use plain `Microsoft.NET.Sdk`; rerun the script after editing a shipped config, or `root_editorconfig_should_match_the_injected_configs` fails.
- **Version pins move together.** A tool or analyzer version appears in `Directory.Packages.props`, in the shipped props that expose it, and in `tests/Headless.NET.Sdk.TestToolVersions.Anchor`. The anchor exists only so that Dependabot opens bump PRs. `VersionConsistencyTests` fails when the three disagree.
- **Test names** use `should_{action}_{expected}_when_{condition}`.

## CI and releases

- `main` gates on the `final-status` job in `ci.yml`. CI and Publish both call `validate.yml`, which packs once and then runs the Linux test shards and the platform smoke jobs in parallel. The macOS smoke job runs only in Publish. Put a new validation job in `validate.yml`; add any other new CI job to the `needs` of `final-status`.
- The Linux suite runs in two shards: `Sdk*` test classes, and every other class. xUnit runs the tests of one class serially, so keep a slow area in its own class rather than growing an existing one.
- Release tags have no `v` prefix: `0.4.3`, not `v0.4.3`. MinVer ignores prefixed tags.
- Pushing a tag publishes to GitHub Packages (`publish.yml`). The NuGet.org job runs only for a published GitHub Release and waits for environment approval. It does not rebuild: it publishes the packages from that tag's successful push run. Publishing is external and irreversible: push a tag or create a release only when explicitly asked.
- A version is published once. If a release goes out partly, inspect both registries and ship the next version.
