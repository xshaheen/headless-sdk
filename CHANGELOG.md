# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

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
