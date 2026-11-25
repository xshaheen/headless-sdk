# Repository Guidelines

## What this repository is

**headless-sdk** builds the Headless MSBuild SDK family: `Headless.NET.Sdk` plus its `.Web`, `.Test`, `.Razor`, `.BlazorWebAssembly`, and `.WindowsDesktop` satellites. The packages ship no compiled code, only MSBuild props and targets, injected analyzer configs, banned-symbol lists, and scaffold files. Any .NET repository can consume them. headless-framework is one consumer: it pins a version in `global.json`, so a change here reaches it only after a release and a pin bump.

The product is the consumer contract, and the README's **Support contract** section is its source of truth. Read that section before you change anything under `src/`. Two consequences guide most decisions:

- **Every consumption mode is first-class.** A change must behave the same under PackageReference and MSBuild-SDK consumption, in single- and multi-targeting builds, and for every satellite. Prove it with a consumer-build test, not by reading the targets.
- **Policies are not consumer knobs.** Analyzer infrastructure and quality gates stay on. Add an opt-out only where the README already documents one, such as the banned-symbol lists. The local inner-loop switches `RunAnalyzersDuringBuild` and `RunAnalyzersDuringLiveAnalysis` are honored only outside CI and AI-agent builds, which force them back on.

## Where a change lands

- `src/Headless.NET.Sdk/build` and `configurations` are packed into every satellite (`src/_shared/Headless.NET.Sdk.Satellite.nuspec`). One edit there changes all six packages.
- `src/_shared` owns satellite packaging and metadata. A satellite's own `build/` and `sdk/` folders hold only its project-type wrapper.
- `tests/Headless.NET.Sdk.Tests.Integrations` is the only test project. Most tests pack the six packages and build throwaway consumer projects against them.

## Hit every surface

A change that works in the one consumer build you tried is often wrong in the others. Before calling a change done, say which of these it touches and what you decided for each, even "not supported here":

- **Consumption modes.** PackageReference, project SDK (`Sdk="..."`), additional SDK (`<Sdk Name>`), `global.json` SDK resolution, and .NET 10 file-based apps (`#:sdk`); the project modes in both single- and multi-targeting builds.
- **Satellites.** `Headless.NET.Sdk` and its `.Web`, `.Test`, `.Razor`, `.BlazorWebAssembly`, and `.WindowsDesktop` satellites. A shared `build/` or `configurations/` edit reaches all six.
- **Props and targets.** The project can override a property set in props; one set in targets overrides the project. Design-time builds evaluate them too.
- **Shipped policy files.** Severities in the injected editorconfigs (plus the regenerated root `.editorconfig` and `AnalyzerRulesAtPackageDefaults.txt`), the `BannedSymbols*.txt` lists and their opt-out properties, and the scaffold templates.
- **Build contexts.** Local, CI (`ContinuousIntegrationBuild`: warnings as errors, locked restore), and AI-agent builds (`SupportDetectLlmContext.props`).
- **Platforms.** Linux shards, Windows smoke (`WindowsDesktop`), and macOS smoke, which runs only in Publish.
- **Docs.** The README's Support contract and consumption sections, and the package README of each satellite touched.

## Build and test

Use the `make` targets; `make help` lists them. A fresh clone or worktree needs `make bootstrap` (tools, restore, hooks); `make doctor` names any missing prerequisite with its fix.

- **`make test-static`** runs the repository-only test classes (analyzer rule coverage, version pins, test discipline) in seconds. Run it after any edit to a config file or a version pin.
- **`make verify`** runs the CI sequence: restore, `--no-incremental` build, pack, then the full suite, which takes minutes. It writes `artifacts/proof/<run>/summary.md`. State its result in the PR's Test section by behavior; never paste it.
- **`make test-class CLASS='*Name*'`** reruns matching tests against the packages from the last `make verify`. Those packages go stale when anything under `src/` changes, so rerun `make verify` first.
- **`make verify-affected`** is the gate to run once, on the final head, before you publish. It runs `make check`: `format-check`, `test-static`, then `verify`. The repo has one solution and one test project, so there is no narrower affected scope.
- **`make prove TEST='*method*' [BASE=origin/main]`** is a fix's regression proof: it runs the tests TEST selects on the merge base, with this checkout's changed test files copied in, then here, and passes only when they fail there and pass here. The PR's Test section names that test by the behavior it holds and says it fails on the base. Read the base log's tail: a base too old for the installed SDK (`global.json` disables roll-forward) also fails, for a reason that is not the bug.
- **`make build`** is incremental and asserts restore instead of restoring (run `make restore` after a package change). `make rebuild` is CI's `--no-incremental` build.
- **`HEADLESS_CONSUMER_SDK_VERSION=<version>`** picks the SDK that every throwaway consumer project builds with. The repository pins SDK 11 in `global.json` and builds and packs with it, but the consumer suite also has to pass on SDK 10, so CI runs each shard once unset (the pinned SDK) and once with `10.0.100`. Set it for any `make test-class` or `make verify` run: the fixture writes a `global.json` into each consumer with that version and `rollForward: latestFeature`, so the matching SDK must be installed. Tests that need a newer SDK than the one selected, such as the `net11.0` consumers, skip themselves with `Assert.SkipUnless`. A test that writes its own consumer `global.json` splices in `TestRepository.ConsumerSdkPinJson`; one that copies the repository's takes `TestRepository.ConsumerSdkVersion`, so neither hardcodes a version.

## Rules the code does not state

- **Local packs need a unique version.** The integration fixture refuses a package version that already exists in `~/.nuget/packages`, because the cached copy would shadow the packages under test. The Makefile packs as `0.0.0-local.<unix-time>`. When you pack by hand, pass `-p:MinVerVersionOverride=<x.y.z>-local.<n>`.
- **The build fails on unformatted C#.** `CSharpier.MSBuild` reports `Was not formatted` as a build error and does not fix the file. Run `dotnet tool restore`, then `dotnet csharpier format <path>`. After `make hooks`, the pre-commit hook formats staged C# files and the pre-push hook checks the C# files changed against upstream; neither builds. Keep the CSharpier version in `dotnet-tools.json` equal to the `CSharpier.MSBuild` version in `Directory.Packages.props`.
- **Agent warnings-as-errors does not apply here.** `SupportDetectLlmContext.props` turns warnings into errors when an agent drives a consumer build. It is product behavior, covered by the integration tests. This repository's own projects use plain `Microsoft.NET.Sdk`, so it does not affect your builds.
- **Severities live only in the injected configs.** Edit `Headless.NET.Sdk.Analyzers.editorconfig` or `Headless.NET.Sdk.Tests.editorconfig`; each keeps one section per analyzer family with rules sorted by ID. The compiler and ReSharper's `jb inspectcode` both read them. `configurations/editorconfig.txt` is an editor-only scaffold, because a consumer `.editorconfig` outranks the SDK and a copied severity would pin that repository to the SDK version it was copied from. `editorconfig_scaffold_should_not_carry_analyzer_settings` fails if one is added. This repository's own root `.editorconfig` is generated from those files by `python3 eng/tools/sync_root_editorconfig.py`, because its projects use plain `Microsoft.NET.Sdk`; rerun the script after editing a shipped config, or `root_editorconfig_should_match_the_injected_configs` fails.
- **Version pins move together.** A tool or analyzer version appears in `Directory.Packages.props`, in the shipped props that expose it, and in `tests/Headless.NET.Sdk.TestToolVersions.Anchor`. The anchor exists only so that Dependabot opens bump PRs. `VersionConsistencyTests` fails when the three disagree.
- **Test names** use `should_{action}_{expected}_when_{condition}`.
- **Tests never wait on time.** `tests/BannedSymbols.Tests.txt` bans sleeps, `Task.Delay`, `Stopwatch`, and clock reads in test code (RS0030, an error); banned APIs inside a consumer-source string are not test code and are unaffected. A line that truly depends on real time suppresses RS0030 with the reason on the pragma: `#pragma warning disable RS0030 // wall-clock: <why>` or `// real-wait: <why>`. `TestDisciplineTests` (in `make test-static`) fails on a suppression without one, on an unconditional skip that names no issue (`#123` or a URL), and on a `Timeout` on `[Fact]` or `[Theory]`. A platform skip uses `Assert.SkipUnless(<capability>, <why>)`.

## CI and releases

- `main` gates on the `final-status` job in `ci.yml`. CI and Publish both call `validate.yml`, which packs once and then runs the Linux test shards and the platform smoke jobs in parallel. The macOS smoke job runs only in Publish. Put a new validation job in `validate.yml`; add any other new CI job to the `needs` of `final-status`.
- The Linux suite runs in four shards: three name their test classes and the fourth excludes those names, so a new class lands in the fourth. Tests run in parallel within a class too (`xunit.runner.json`, `parallelMode: all`), so a shard is bound by its runner's cores: balance shards by the summed test time of their classes on the .NET 10 consumer SDK, and when you add a class to a named shard, also exclude it from the last one. A class groups tests by topic, and a test that must not overlap another sets `DisableParallelization`.
- Consumer builds in the suite share one NuGet HTTP cache (`Headless.NET.Sdk.Tests/nuget-http-cache` under the temp folder, kept across runs) and restore into their own packages folders. Delete that folder to make the next run fetch from nuget.org cold.
- Release tags have no `v` prefix: `0.4.3`, not `v0.4.3`. MinVer ignores prefixed tags.
- Pushing a tag publishes to GitHub Packages (`publish.yml`). The NuGet.org job runs only for a published GitHub Release and waits for environment approval. It does not rebuild: it publishes the packages from that tag's successful push run. Publishing is external and irreversible: push a tag or create a release only when explicitly asked.
- A version is published once. If a release goes out partly, inspect both registries and ship the next version.

## Pull requests

- **Fill `.github/PULL_REQUEST_TEMPLATE.md`:** a summary of one or two sentences, then Why, What changed (one Before/After row per behavior a user or caller can observe), and the sections that apply: Decisions, Left out (each item with its issue), Screenshots (every changed screen and state), Contract changes, Rollout, Review focus, and Test. Delete a section with nothing to say, and keep each heading's emoji. Test marks results ✅ passes, ❌ fails on the base, ⚠️ not verified. Keep prose outside tables and code under about 300 words (450 for a large PR). Review focus asks for the feedback you want and names where you are least sure. Test states what the tests hold on the head commit, and for a fix the test that fails without it, by behavior: never a local command, gate output, or path. Link each Review focus question to its hunk; a reviewer reads that and the hunks it points to, not every line.
- **Split by kind of change.** Version pin bumps, the regenerated root `.editorconfig`, and renames go in their own PR, stacked under the behavior change, so the behavior change reads on its own.
- **This repository is public.** A title, description, commit message, or comment never names a private repository, a client or consumer app, or a local path. Write "a consumer app" and state the fact: which project type, consumption mode, and property misbehaved.
