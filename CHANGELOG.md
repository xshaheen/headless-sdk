# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.0.120] - 2026-06-28

### Fixed

- Scaffolded `.editorconfig` files now carry the ReSharper/Rider severity alignment that `jb inspectcode` reads, while the SDK-injected analyzer configuration stays limited to compiler-consumed Roslyn settings.

## [0.0.119] - 2026-06-28

### Fixed

- Test-project analyzer suppressions (`CA1849`, `MA0042`, `MA0166`, `CA1861`, `CA1859`, `CA1720`, and the rest of the test `NoWarn` set) now apply when `IsTestableProject` is set in `Directory.Build.props` or the project file under MSBuild SDK consumption. The suppressions moved from `SupportGeneral.props` to `SupportGeneral.targets` so a consumer-set value is visible; the `Headless.NET.Sdk.Test` SDK was already unaffected.
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
- Added a test-project analyzer configuration that is injected when `IsTestableProject` is true, relaxing common test-only analyzer false positives without weakening production defaults.
- Added on-demand scaffolding for repo config files (`.editorconfig`, `.csharpierignore`, `.gitignore`, and `.gitattributes`) while preserving existing files unless `HeadlessOverwriteConfigFiles` is set.

### Changed

- Moved code-style, analyzer severity, and naming opinions into the SDK-injected analyzer baseline so consumers get those defaults without copying a project `.editorconfig`.
- Reduced the optional `.editorconfig` scaffold to editor and file-formatting settings, keeping analyzer policy in the SDK-owned global analyzer config.
- Modernized the scaffolded CSharpier, Git attributes, and Git ignore templates.

### Fixed

- Kept the injected global analyzer config sectionless so Release builds no longer fail with Roslyn `InvalidGlobalSectionName` diagnostics.
- Removed the inert `MA0048` severity entry from the analyzer baseline while keeping that rule suppressed through `NoWarn`.

`0.0.113` was superseded by `0.0.114` after its tag-push publish workflow failed before package publication.
