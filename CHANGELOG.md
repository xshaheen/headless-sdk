# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.0.129] - 2026-07-14

### Changed

- Updated the injected Microsoft Testing Platform tooling to CrashDump 2.3.1, HangDump 2.3.1, HotReload 2.3.1, Retry 2.3.1, TrxReport 2.3.1, and CodeCoverage 18.9.0.

## [0.0.128] - 2026-07-08

### Changed

- `GenerateSBOM` is now opt-in instead of auto-enabled on CI. Set `GenerateSBOM=true` to add `Microsoft.Sbom.Targets`; CI detection no longer changes the restore graph by default.

## [0.0.127] - 2026-07-07

### Added

- `IsTestHarnessProject=true` now lets shared harness projects receive SDK test defaults without being discovered or executed as test hosts. Harnesses force `IsTestProject=false`, `IsTestingPlatformApplication=false`, and `GenerateRuntimeConfigurationFiles=true` while retaining the test `NoWarn` set, `Headless.NET.Sdk.Tests.editorconfig`, coverage/test assembly exclusions, and Microsoft Testing Platform tooling.
- Plain `Headless.NET.Sdk` projects can opt into runnable test-project defaults directly with `IsTestProject=true`; `Headless.NET.Sdk.Test` still sets that automatically.

### Removed

- Removed the unreleased `IsTestableProject` classification property. Use `IsTestProject=true` for runnable test projects or `IsTestHarnessProject=true` for non-runnable shared harness projects.

## [0.0.126] - 2026-07-07

### Changed

- **Symbols policy is now owned by the SDK via the new `HeadlessSymbolFormat` property.** Non-test projects default to `embedded` (`DebugType=embedded`, `IncludeSymbols=false`): the PDB ships inside the assembly, so symbols resolve on feeds without a symbol server (e.g. GitHub Packages) and no `.snupkg` is produced. Set `HeadlessSymbolFormat=snupkg` for the previous portable-PDB + `.snupkg` behavior, or `none` to ship no symbols. Consumer-set `DebugType`, `IncludeSymbols`, or `SymbolPackageFormat` always win. Blazor WebAssembly projects default to `none` because an embedded PDB leaks into the published `_framework` browser payload. Note: `dotnet pack --include-symbols` passes `IncludeSymbols=true` as a global property and overrides this policy — drop that flag when relying on the embedded default.
- `GitHubActionsTestLogger` is now referenced unconditionally for VSTest test projects instead of only when `GITHUB_ACTIONS=true`, so the restore graph — and consumer `packages.lock.json` files — no longer depend on CI environment variables (CI-committed lock files were rewritten by every local restore). The package stays dev-time only (`PrivateAssets=all`), remains excluded from Microsoft Testing Platform projects, and the logger still activates only on GitHub Actions.

## [0.0.125] - 2026-07-02

### Changed

- Suppressed `MA0182` (internal type apparently never used) in the injected analyzer baseline and the `editorconfig.txt` eject. It systematically false-positives in this framework, where internal types are registered via DI across `InternalsVisibleTo` seams the analyzer cannot see.
- Lowered `CA2227` (collection properties should be read only) from suggestion to `silent` in both the injected analyzer baseline and the `editorconfig.txt` eject.
- Suppressed `MA0176` (optimize `Guid` creation) in the injected test analyzer configuration and the `editorconfig.txt` eject, since the micro-optimization is irrelevant in test fixtures.
- Extended the `editorconfig.txt` eject with ReSharper closure-inspection relaxations (`access_to_disposed_closure`, `access_to_modified_closure`) and a `[{demo,benchmarks,tests}/**/*.cs]` relaxation for `CA5394` (insecure randomness), matching the repo's own dogfooded configuration.

## [0.0.124] - 2026-07-01

### Changed

- The optional `editorconfig.txt` eject now carries the complete analyzer diagnostic severity set plus the `[tests/**/*.cs]` relaxations, restoring full coverage for consumers whose projects do not receive the MSBuild-injected baseline (e.g. plain `Microsoft.NET.Sdk` test projects).
- Reorganized the injected analyzer severities (`Headless.NET.Sdk.Analyzers.editorconfig` and `Headless.NET.Sdk.Tests.editorconfig`) and the `editorconfig.txt` eject into intent-based groups (globalization, naming, design, performance, async, security, correctness, style). Behavior-preserving: every rule and severity is unchanged.

## [0.0.123] - 2026-06-30

### Changed

- Relaxed the SDK-injected analyzer baseline: `CA1716` (identifiers should not match keywords) is now `none`, and `CA1045` (do not pass types by reference) ships as a suggestion.
- Test-project analyzer config now fully suppresses `CA2201` (reserved exception types), `CA1028` (Int32 enum storage), and `CA2227` (read-only collection properties), so intentional test patterns no longer surface as analyzer or IDE noise.

## [0.0.122] - 2026-06-28

### Changed

- Added ReSharper/Rider disposal-inspection relaxations to the scaffolded `.editorconfig` so test projects avoid by-design lifetime noise in IDE tooling.
- Relaxed `CA1001` in the injected test analyzer configuration for disposable-field test fixtures.

## [0.0.121] - 2026-06-28

### Changed

- Lowered `MA0025` in the SDK-injected analyzer baseline to suggestion, keeping intentional `NotImplementedException` test coverage from surfacing as a warning.
- Trimmed scaffolded `.editorconfig` ReSharper/Rider hints for rules that should not be enforced by the generated template.

## [0.0.120] - 2026-06-28

### Fixed

- Scaffolded `.editorconfig` files now carry the ReSharper/Rider severity alignment that `jb inspectcode` reads, while the SDK-injected analyzer configuration stays limited to compiler-consumed Roslyn settings.

## [0.0.119] - 2026-06-28

### Fixed

- Test-project analyzer suppressions (`CA1849`, `MA0042`, `MA0166`, `CA1861`, `CA1859`, `CA1720`, and the rest of the test `NoWarn` set) now apply when `IsTestProject` is set in `Directory.Build.props` or the project file under MSBuild SDK consumption. The suppressions moved from `SupportGeneral.props` to `SupportGeneral.targets` so a consumer-set value is visible; the `Headless.NET.Sdk.Test` SDK was already unaffected.
- NuGet audit no longer escalates `NU1900` (audit source unreachable) to a build error on CI or Release, so a registry outage or offline restore no longer fails the build. The vulnerability codes `NU1901`-`NU1904` remain errors.
- Normalized the base package's `buildTransitive` and `buildMultiTargeting` re-import paths so they match the project-type SDK variants.

## [0.0.118] - 2026-06-27

### Added

- Added `HeadlessEmitInternalsVisibleToAttributes` so consumers can opt out of conventional test friend assemblies while unsigned projects keep the SDK default.

### Changed

- Relaxed test-project analyzer noise for `CA1861`, `CA1859`, and `CA1720`, and kept `CA2227` as a suggestion in both regular and test analyzer configs.

### Fixed

- Signed projects no longer receive conventional `InternalsVisibleTo` attributes that would be invalid without public keys.
- CI restores for MSBuild SDK consumers now enforce locked mode by default, matching the documented restore contract.
- Scaffold output now reports `Created` only for newly copied config files and `Skipped` only for files that already existed.

## [0.0.117] - 2026-06-23

### Fixed

- Kept `GitHubActionsTestLogger` out of Microsoft Testing Platform test projects on GitHub Actions so MTP builds avoid unresolved logger integration references while VSTest projects still receive the GitHub Actions logger.

## [0.0.116] - 2026-06-22

### Changed

- Updated the injected test toolchain to `Microsoft.NET.Test.Sdk` 18.6.0 and `GitHubActionsTestLogger` 3.0.4 so consumer test projects receive the newer runner and GitHub Actions logging fixes.
- Updated CI and publish workflows to `actions/checkout` 7.0.0.

## [0.0.115] - 2026-06-21

### Fixed

- Suppressed `IDE1006` in the injected test analyzer config so test projects can keep snake_case test method names without naming diagnostics.

## [0.0.114] - 2026-06-21

### Added

- Added `HeadlessEnforceConfigureAwait`, an opt-in SDK property that injects a bundled analyzer config to enforce `CA2007` for projects that want strict `ConfigureAwait` usage.
- Added a test-project analyzer configuration that is injected for projects that opt into test defaults, relaxing common test-only analyzer false positives without weakening production defaults.
- Added on-demand scaffolding for repo config files (`.editorconfig`, `.csharpierignore`, `.gitignore`, and `.gitattributes`) while preserving existing files unless `HeadlessOverwriteConfigFiles` is set.

### Changed

- Moved code-style, analyzer severity, and naming opinions into the SDK-injected analyzer baseline so consumers get those defaults without copying a project `.editorconfig`.
- Reduced the optional `.editorconfig` scaffold to editor and file-formatting settings, keeping analyzer policy in the SDK-owned global analyzer config.
- Modernized the scaffolded CSharpier, Git attributes, and Git ignore templates.

### Fixed

- Kept the injected global analyzer config sectionless so Release builds no longer fail with Roslyn `InvalidGlobalSectionName` diagnostics.
- Removed the inert `MA0048` severity entry from the analyzer baseline while keeping that rule suppressed through `NoWarn`.

`0.0.113` was superseded by `0.0.114` after its tag-push publish workflow failed before package publication.
