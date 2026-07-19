# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Added first-class contract coverage for all five supported consumption modes: direct `PackageReference`, versioned project SDK, additional SDK, versionless `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk` file-based apps. All six SDK family members support every mode.
- Added consumer tests against newly packed packages for restore, build, run, pack, static-graph evaluation, design-time evaluation, mixed and duplicate imports, multi-targeting outer and inner builds, target-framework compatibility, analyzer enforcement and opt-outs, CI warnings, locked restore, NuGet audit, SBOM generation, Microsoft Testing Platform execution, packaging defaults, and explicit consumer overrides.
- Added Windows validation for packed Windows Desktop WPF and Windows Forms consumers and macOS validation for a packed base-SDK consumer. Linux, Windows, and macOS validation must all pass before publication.
- Added `System.Collections.ArrayList` and `Assembly.GetAssembly(Type)` to the general banned-symbol list.
- Added tested GitHub Packages preflight and postflight checks for duplicate versions, supported public or private package visibility, and exact published versions.
- Added SHA-256 package hashes and a six-package ID/version manifest so publication promotes and verifies the exact artifacts produced by the build job.

### Changed

- All base and satellite `sdk`, `build`, and `buildMultiTargeting` imports are sentinel-guarded and evaluate exactly once across project-SDK, additional-SDK, mixed SDK plus `PackageReference`, static-graph, design-time, and multi-targeting outer/inner builds. Wrapper identity and pre-`Directory.Build.props`/pre-Microsoft-target ordering are preserved.
- Satellite packages are self-contained: each carries its project-type wrapper plus the shared base build and configuration assets and no longer depends on resolving a separate `Headless.NET.Sdk` package version.
- All six packages use explicit, content-only MSBuild SDK nuspec contracts. Package assets are limited to `sdk`, `build`, `buildMultiTargeting`, `configurations`, README, and logo content; dependencies use framework-agnostic groups, and the former `lib/netstandard2.0/_._` compatibility marker is gone.
- The repository is built with exactly .NET SDK `10.0.301` (`rollForward=disable`), but Headless no longer restricts consumer target frameworks. Normal MSBuild projects must declare `TargetFramework` or `TargetFrameworks`; compatibility is determined by the selected Microsoft SDK and installed targeting packs or workloads. .NET 10 is required only for repository tooling and the file-app host.
- Blazor WebAssembly and Windows Desktop file apps use the base `Microsoft.NET.Sdk` while retaining their Headless satellite identity; normal projects continue to wrap `Microsoft.NET.Sdk.BlazorWebAssembly` and `Microsoft.NET.Sdk.WindowsDesktop`.
- File-based apps always receive the dedicated analyzer profile. Headless extra global usings now evaluate after the project body and are added only when `ImplicitUsings` is `enable`/`true` and the consumer TFM is compatible with `net8.0`.
- In-project `HeadlessEnableStrictSystemTextJsonRuntimeDefaults=true` now evaluates after the consumer project body and adds the strict runtime switches only to inner builds compatible with `net9.0`.
- The nine analyzer packages are mandatory, private, SDK-owned dependencies. Their versions and asset metadata are reasserted after consumer item evaluation; duplicate consumer references cannot weaken or leak them, and conflicting Central Package Management entries are rejected instead of replacing the baseline. `Meziantou.Analyzer` package projects retain the self-reference exception.
- Analyzer execution, .NET analyzers, code-style enforcement, and the base, ConfigureAwait, file-app, and test editorconfig inputs are reasserted after the project body and included exactly once.
- The general and Newtonsoft.Json banned-symbol lists remain enabled by default but are independently configurable. `DisableSupportBannedSymbols=true` disables both lists, `IncludeDefaultBannedSymbols=false` disables the general list, and `BannedNewtonsoftJsonSymbols=false` disables only the Newtonsoft.Json list. The `Microsoft.CodeAnalysis.BannedApiAnalyzers` package remains mandatory analyzer infrastructure.
- SponsorLink and Moq analyzer cleanup remains enabled by default. `DisableSponsorLink=false` is the sole supported opt-out that retains those analyzers.
- CI provider variables are authoritative and `ContinuousIntegrationBuild=true` is the only manual activation input. CI now treats compiler, analyzer, nullable, and MSBuild warnings plus confirmed vulnerabilities (`NU1901`-`NU1904`) as errors; local Debug no longer defaults compiler warnings to errors, and Release alone no longer activates warning or vulnerability escalation.
- NuGet restore, pack, signing, and feed diagnostics remain warnings under the CI compiler/MSBuild gate. `NU1900` and `NU1905` remain warnings because they indicate unavailable audit data, while the visible `NETSDK1138` end-of-life diagnostic remains non-fatal so Headless does not turn an otherwise targetable TFM into a framework restriction.
- `Deterministic=true` is authoritative. Explicit consumer values are now preserved for `RootNamespace`, `AssemblyName`, `ImplicitUsings`, `GenerateDocumentationFile`, non-CI `CodeAnalysisTreatWarningsAsErrors`, `AccelerateBuildsInVisualStudio`, `NeutralLanguage`, `PreferredUILang`, `IsPackable`, and the Test SDK's `IsPublishable` default.
- CI locked restore is enabled only when `packages.lock.json` exists or a late `NuGetLockFilePath` identifies an existing lock file. Repositories without a lock file restore normally.
- NuGet audit is authoritative at `NuGetAudit=true`, `NuGetAuditMode=all`, and `NuGetAuditLevel=low`. Direct `PackageReference` consumers must bootstrap first-clean-restore audit, lock, and warning policy in repository props or CLI arguments because NuGet cannot import package build assets before that initial restore.
- `Headless.NET.Sdk.Test` is Microsoft Testing Platform only. It defaults library output to an executable MTP host and supplies restore-visible, private, SDK-owned crash dump, code coverage, hang dump, hot reload, retry, and TRX extensions in every consumption mode; consumers continue to choose and version their test framework.
- Base-SDK projects using `IsTestProject=true` or `IsTestHarnessProject=true` now receive test analysis, editorconfig, warning-relaxation, and non-packable defaults only. The complete MTP host, extension dependencies, and command-line defaults require `Headless.NET.Sdk.Test`.
- Test execution retains default TRX output, crash and hang dumps, a ten-minute hang timeout, a minimum-one-test guard, and CI coverage arguments. Analyzer suppression is now MTP-only and controlled by `OptimizeTestRun`; xUnit global-using injection now applies only to `xunit.v3.mtp-v2`.
- .NET 10 command hosts must select `Microsoft.Testing.Platform` through `global.json`; this command-host setting is independent of the test project's target framework.
- `Microsoft.Sbom.Targets` is now an exact, private, restore-visible dependency in every consumption mode. Headless owns its single targets import, generation remains opt-in through `GenerateSBOM=true`, and requesting generation without restored tooling produces a targeted pack error.
- Headless follows the Microsoft SDK default for `EmbedUntrackedSources` instead of forcing source embedding.
- Package metadata now uses the general-purpose, consumer-facing SDK-family description, the repository README and logo, exact repository branch/commit provenance, and no license metadata. The family is not limited to Headless Framework consumers and is distributed through GitHub Packages and NuGet.org.
- Tag and manual workflow runs publish only to GitHub Packages after hash verification and duplicate/visibility-state preflight; postflight accepts public or private package visibility and verifies the exact published family without retrying a package push.
- Published GitHub Releases publish only to NuGet.org after the complete build/test/platform gate, SHA-256 verification, an exact release-tag/package-version match, and approval in the protected `NuGet Release` environment. The release artifact set must contain exactly six `.nupkg` files and no `.snupkg` files; duplicate versions fail.
- Publication is serialized across the package family with queued runs and bounded job timeouts. A partial feed publication is not retried automatically.
- CI now builds and packs once, tests the immutable packed artifact set, retains warnings in final logs, isolates clean-consumer NuGet caches outside source globs, and exposes a single Linux/Windows/macOS final status.
- Repository restore now clears inherited sources, uses NuGet.org as its only package and audit source, and no longer authenticates to GitHub Packages. Dependabot applies a seven-day cooldown to NuGet, .NET SDK, and GitHub Actions updates.
- GitHub Actions are commit-SHA pinned, checkout credentials are not persisted, workload update notifications are disabled, and workload integrity checking is skipped for repository CI and publication jobs.
- Shipped MTP extension versions are checked against `Directory.Packages.props` and the package nuspec contracts instead of being regenerated during build.

### Removed

- Removed all `buildTransitive` package assets. Each project must opt in directly; project references no longer propagate Headless policy, while `buildMultiTargeting` remains for multi-targeting outer builds.
- Removed target-framework inference and the `HeadlessInferTargetFramework` and `DisableSupportTargetFrameworkInference` inputs. Missing target frameworks now fail through the selected Microsoft SDK.
- Removed the `HeadlessSingleFileApp` and `DisableSupportSingleFileApp` inputs. `FileBasedProgram=true` now selects the mandatory file-app profile automatically.
- Removed the `IsContinuousIntegration` input. Use a recognized CI provider environment or `ContinuousIntegrationBuild=true`.
- Removed the `DisableSupportImplicitAnalyzers`, `DisableSupportAnalyzerEditorConfigs`, `DisableSupportAnalyzerHygiene`, `DisableSupportNuGetAudit`, and `DisableSupportSbom` opt-outs. These named quality infrastructures are authoritative; the three banned-symbol controls remain supported.
- Removed the legacy `Disable_SponsorLink` alias. Use `DisableSponsorLink=false` to retain SponsorLink and Moq analyzers.
- Removed VSTest auto-detection and defaults, `UseMicrosoftTestingPlatform`, `OptimizeVsTestRun`, implicit `Microsoft.NET.Test.Sdk`, `GitHubActionsTestLogger`, VSTest blame/logger/coverage defaults, and xUnit v2/generic xUnit v3/TUnit implicit-using detection.
- Removed build-time test-tool version generation. The checked-in MTP extension pins are now verified against `Directory.Packages.props` and package dependency metadata.

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
