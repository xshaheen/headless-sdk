# Headless.Sdk

`Headless.Sdk` is the MSBuild SDK / source-only package that provides standard configurations and settings for the Framework projects: `.editorconfig`, build props/targets, analyzers, banned-API rules, opinionated test defaults.

All Framework projects build on top of this package for a consistent build/test pipeline.

## Installation

Three consumption modes are supported.

### 1. As a `<PackageReference>` (current default)

```bash
dotnet add package Headless.Sdk
```

```xml
<PackageReference Include="Headless.Sdk" Version="x.x.x" PrivateAssets="all"/>
```

NuGet auto-imports `build/Headless.Sdk.props` and `build/Headless.Sdk.targets` after `Directory.Build.props`.

### 2. As an MSBuild SDK (`global.json` + project Sdk)

```jsonc
{
  "msbuild-sdks": {
    "Headless.Sdk": "x.x.x"
  }
}
```

```xml
<Project Sdk="Headless.Sdk">
</Project>
```

In this mode the SDK runs **before** `Directory.Build.props` via `CustomBeforeDirectoryBuildProps` + `BeforeMicrosoftNETSdkTargets`, so its defaults are visible everywhere.

You can also pin the version in the csproj:

```xml
<Project Sdk="Headless.Sdk/x.x.x">
</Project>
```

Or layer it on top of `Microsoft.NET.Sdk`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Headless.Sdk" Version="x.x.x" />
</Project>
```

### 3. .NET 10+ file-based apps (`#:sdk` directive)

```csharp
#:sdk Headless.Sdk@x.x.x
Console.WriteLine("Hello!");
```

```bash
dotnet run Program.cs
```

## Features

- Opinionated defaults: nullable, implicit usings, deterministic builds, latest C#, `Features=strict`
- `AnalysisLevel=latest-all` with `EnforceCodeStyleInBuild`
- Implicit analyzers (`Meziantou.Analyzer`, `Microsoft.CodeAnalysis.BannedApiAnalyzers`)
- Shipped `BannedSymbols.txt` (`DateTime.Now`, `InvariantCulture` comparisons, `System.Tuple`, non-UTC IO, etc.) plus a Newtonsoft.Json banlist
- NuGet audit + central package transitive pinning friendly
- Container support for `Microsoft.NET.Sdk.Web` projects on GitHub Actions (auto `ghcr.io` registry, image tags from run number / SHA)
- CI auto-detection for the major build agents
- Test-project defaults: MTP auto-detection, crash/hang dumps, code coverage on CI, `GitHubActionsTestLogger` on GitHub
- Disables analyzers during `dotnet test` for fast feedback (override via `OptimizeVsTestRun=false`)
- Auto `Copyright`, README/LICENSE/logo packing, optional strict System.Text.Json runtime defaults
- Optional packaged config bootstrap for `.editorconfig`, `.csharpierignore`, `.gitignore`, and `.gitattributes`
- VSTest coverage runsettings that treat zero tests as an error and exclude test-log infrastructure from coverage
- Optional npm restore integration for projects that opt in with `HeadlessEnableNpmRestore=true`
- File-based app analyzer relaxations when `FileBasedProgram=true` or `HeadlessSingleFileApp=true`
- Optional target framework inference when `HeadlessInferTargetFramework=true`
- Assembly metadata that records the SDK identity in compiled outputs
- SBOM generation (`Microsoft.Sbom.Targets`) auto-enabled on CI
- Faster restore (`RestoreUseStaticGraphEvaluation`, `RestoreSerializeGlobalProperties`)
- `EnablePackageValidation=true` to catch breaking API changes between releases
- `Deterministic=true` for reproducible builds
- `MSBuildTreatWarningsAsErrors=true` on CI / Release
- `RollForward=LatestMajor` for non-test apps so they run on newer runtime majors
- Embeds editorconfigs, banned-symbols files, and GitHub Actions env vars in `.binlog` for post-mortem CI debugging

## Toggles

| Property | Default | Effect |
|---|---|---|
| `IncludeDefaultBannedSymbols` | `true` | Adds the core banned-API list. Set `false` to disable. |
| `BannedNewtonsoftJsonSymbols` | `true` | Bans Newtonsoft.Json types. Set `false` to keep them. |
| `DisableSupportImplicitAnalyzers` | unset | Set `true` to skip the implicit analyzer import. |
| `DisableImplicitAnalyzers` | unset | Set `true` to skip the implicit analyzer references after the import is loaded. |
| `DisableSupportBannedSymbols` | unset | Set `true` to skip the banned-symbols import entirely. |
| `DisableSupportWebContainer` | unset | Set `true` to skip Web SDK container automation. |
| `DisableSupportAnalyzerHygiene` | unset | Set `true` to skip analyzer hygiene targets. |
| `DisableSupportSingleFileApp` | unset | Set `true` to skip file-based app analyzer relaxations. |
| `DisableSupportTargetFrameworkInference` | unset | Set `true` to skip target-framework inference support. |
| `DisableSupportNpm` | unset | Set `true` to skip npm restore targets. |
| `DisableSponsorLink` | `true` | Removes SponsorLink/Moq analyzers when not set to `false`. |
| `Disable_SponsorLink` | `true` | Meziantou-compatible alias for `DisableSponsorLink`. Set `false` to keep those analyzers. |
| `HeadlessSingleFileApp` | auto for file-based apps | Adds relaxed analyzer settings for single-file/file-based apps. |
| `HeadlessInferTargetFramework` | `false` | Set `true` to infer `TargetFramework` from the current .NET SDK when a project omits `TargetFramework`/`TargetFrameworks`. |
| `HeadlessEnableNpmRestore` | `false` | Set `true` to run npm restore for a project-local `package.json`. Explicit `NpmPackageFile` items also participate. |
| `EnableDefaultNpmPackageFile` | `true` when npm restore is enabled | Set `false` to prevent automatic `package.json` discovery. |
| `NpmRestoreLockedMode` | `true` on CI/locked restore | Uses `npm ci` when true, otherwise `npm install`. |
| `OptimizeVsTestRun` | `true` | Disables analyzers during `dotnet test`. Set `false` to keep them. |
| `EnableCodeCoverage` | `true` on CI | Enables coverage collection for test projects on CI. |
| `UseMicrosoftTestingPlatform` | auto | Auto-detected from `xunit.v3.mtp-v2` or `TUnit`. Force with `true` / `false`. |
| `EnableDefaultTestSettings` | `true` | Adds default crash/hang dumps and loggers for test projects. |
| `HeadlessEnableStrictSystemTextJsonRuntimeDefaults` | `false` | Opt in to strict STJ runtime defaults on net9+. |
| `HeadlessCopyEditorConfigToSolutionDir` | `false` | Opt in to copy the bundled `.editorconfig` to the solution root. |
| `HeadlessCopyDefaultConfigFilesToSolutionDir` | `false` | Opt in to copy bundled `.editorconfig`, `.csharpierignore`, `.gitignore`, and `.gitattributes` to the solution root. |
| `HeadlessCopyCSharpierIgnoreToSolutionDir` | `false` | Opt in to copy the bundled `.csharpierignore`. Enabled by `HeadlessCopyDefaultConfigFilesToSolutionDir`. |
| `HeadlessCopyGitIgnoreToSolutionDir` | `false` | Opt in to copy the bundled `.gitignore`. Enabled by `HeadlessCopyDefaultConfigFilesToSolutionDir`. |
| `HeadlessCopyGitAttributesToSolutionDir` | `false` | Opt in to copy the bundled `.gitattributes`. Enabled by `HeadlessCopyDefaultConfigFilesToSolutionDir`. |
| `DisableReadme` | unset | Set `true` to skip automatic README package metadata and packing. |
| `DisablePackageLogo` | unset | Set `true` to skip automatic package icon metadata and packing. |
| `SearchReadmeFileAbove` | `false` | Set `true` to search parent directories for README files to pack. |
| `PackAsTool` | `true` for non-test, non-Web executables | Override per project when executable packages are not .NET tools. |
| `GenerateSBOM` | `true` on CI | Generate a Software Bill of Materials. Set `false` to skip. |
| `DisableSupportSbom` | unset | Set `true` to skip the SBOM import entirely. |
| `DisableSupportEmbedBinlog` | unset | Set `true` to skip embedding context in `.binlog`. |
| `EnablePackageValidation` | `true` | Validate breaking changes vs the previous package version. |
| `RollForward` | `LatestMajor` | For non-test apps. Override per project. |
| `MSBuildTreatWarningsAsErrors` | `true` on CI / Release | MSBuild-level warnings become errors. |

## EditorConfig

The package no longer overwrites `$(SolutionDir).editorconfig` during normal builds.

If you want Headless.Sdk to manage the solution-level `.editorconfig`, opt in explicitly:

```xml
<PropertyGroup>
  <HeadlessCopyEditorConfigToSolutionDir>true</HeadlessCopyEditorConfigToSolutionDir>
</PropertyGroup>
```
