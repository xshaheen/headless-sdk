# Headless.Sdk

`Headless.Sdk` is an opinionated MSBuild SDK and source-only package for Headless Framework projects. It centralizes the build defaults, analyzer setup, test behavior, package metadata, CI detection, and repository configuration files that otherwise drift across solutions.

The intent is simple: every project starts with the same strict baseline, then opts out only where the local project has a clear reason.

## What It Standardizes

- Build defaults: nullable reference types, implicit usings, latest C#, strict compiler features, deterministic output, static graph restore, and package validation.
- Quality gates: `AnalysisLevel=latest-all`, .NET analyzers, Meziantou analyzer, banned API rules, NuGet audit, and code style enforcement.
- Test projects: automatic test-project detection, MTP or VSTest defaults, dumps on crash or hang, CI coverage, GitHub Actions logging, and faster `dotnet test` runs.
- CI behavior: provider detection, `ContinuousIntegrationBuild`, locked restore behavior, SBOM generation, and stricter warning handling.
- Packaging: default authors/company/license metadata, README/LICENSE/logo packing, Source Link, symbol packages, and repository metadata.
- App support: web container tagging on GitHub Actions, optional npm restore, file-based app relaxations, optional target framework inference, and optional strict System.Text.Json runtime switches.
- Diagnostics: embeds editorconfig, banned-symbol files, npm lock files, and GitHub Actions environment details into binlogs.

## Usage

Choose the consumption style based on how early the defaults must run.

### Package Reference

Use this when you want the package imported through NuGet's normal `build/` assets.

```bash
dotnet add package Headless.Sdk --version x.y.z
```

```xml
<PackageReference Include="Headless.Sdk" Version="x.y.z" PrivateAssets="all" />
```

In this mode, NuGet imports `build/Headless.Sdk.props` and `build/Headless.Sdk.targets` through the normal package asset pipeline.

### MSBuild SDK

Use this when the defaults must be visible before the consumer's `Directory.Build.props`.

```jsonc
{
  "msbuild-sdks": {
    "Headless.Sdk": "x.y.z"
  }
}
```

```xml
<Project Sdk="Headless.Sdk">
</Project>
```

You can also pin the SDK directly in the project file:

```xml
<Project Sdk="Headless.Sdk/x.y.z">
</Project>
```

Or layer it on top of the .NET SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Headless.Sdk" Version="x.y.z" />
</Project>
```

### File-Based Apps

.NET 10+ file-based apps can use the SDK directive:

```csharp
#:sdk Headless.Sdk@x.y.z
Console.WriteLine("Hello!");
```

```bash
dotnet run Program.cs
```

## Import Model

`Headless.Sdk` ships assets in `sdk/`, `build/`, `buildMultiTargeting/`, and `buildTransitive/`.

| Mode | Import timing | Use when |
| --- | --- | --- |
| `PackageReference` | NuGet imports package build assets through the standard package flow. | You want the current default consumption path. |
| `<Project Sdk="Headless.Sdk">` | `sdk/Sdk.props` wires `build/Headless.Sdk.props` before `Directory.Build.props` and targets before `Microsoft.NET.Sdk` targets. | Repository-wide defaults need to be visible early. |
| `<Sdk Name="Headless.Sdk" />` | Imported as an additional SDK inside a `Microsoft.NET.Sdk` project. | You want normal .NET SDK behavior plus Headless defaults. |
| `#:sdk` | Imported by the file-based app SDK directive. | Single-file experiments or scripts should share the same defaults. |

## Configuration Reference

Many values apply only when the consuming project has not already set the property; others intentionally set the shared Headless baseline. Feature-level imports can also be disabled with the `DisableSupport*` switches listed below.

### General Build

| Property | Default | Effect |
| --- | --- | --- |
| `HeadlessSdkName` | `Headless.Sdk` | SDK identity used in generated assembly metadata. |
| `Configuration` | `Debug` | Default build configuration. |
| `Platform` | `AnyCPU` | Default platform. |
| `RootNamespace` | `$(MSBuildProjectName)` | Aligns namespace with project name. |
| `AssemblyName` | `$(MSBuildProjectName)` | Aligns assembly name with project name. |
| `LangVersion` | `latest` | Uses the latest available C# language version. |
| `Nullable` | `enable` | Enables nullable reference types. |
| `ImplicitUsings` | `enable` | Enables SDK implicit usings. |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. Missing XML docs are suppressed by default. |
| `Features` | `strict` | Enables strict compiler feature flags. |
| `Deterministic` | `true` | Produces reproducible builds when inputs match. |
| `RestoreUseStaticGraphEvaluation` | `true` | Uses static graph restore. |
| `RestoreSerializeGlobalProperties` | `true` | Serializes restore global properties. |
| `EnablePackageValidation` | `true` | Checks packages for breaking API changes. |
| `MSBuildTreatWarningsAsErrors` | `true` on CI or Release | Promotes MSBuild warnings to errors. |
| `RollForward` | `LatestMajor` for non-test apps | Allows apps to run on newer installed runtime majors. |
| `PackAsTool` | `true` for non-test, non-Web executables | Packages executable projects as .NET tools by default. |

### Analysis And API Hygiene

| Property | Default | Effect |
| --- | --- | --- |
| `AnalysisLevel` | `latest-all` | Enables the latest analyzer rule set. |
| `AnalysisMode` | `All` | Runs all analyzer categories. |
| `EnableNETAnalyzers` | `true` | Enables .NET analyzers. |
| `EnforceCodeStyleInBuild` | `true` | Enforces code style during build. |
| `ReportAnalyzer` | `true` | Includes analyzer timing/reporting data. |
| `RunAnalyzersDuringBuild` | `true` | Runs analyzers during builds. |
| `IncludeDefaultBannedSymbols` | `true` | Adds the bundled banned API list. Set `false` to skip it. |
| `BannedNewtonsoftJsonSymbols` | `true` | Bans Newtonsoft.Json APIs by default. Set `false` to keep them. |
| `DisableSponsorLink` | `true` unless set to `false` | Removes SponsorLink and Moq analyzers. |
| `Disable_SponsorLink` | Alias | Meziantou-compatible alias for `DisableSponsorLink`. |

### CI, Audit, And Supply Chain

| Property | Default | Effect |
| --- | --- | --- |
| `IsContinuousIntegration` | Auto-detected | Detects GitHub Actions, Azure Pipelines, GitLab CI, TeamCity, AppVeyor, Travis, CircleCI, AWS CodeBuild, Jenkins, Google Cloud Build, JetBrains Space, and generic `CI=true`. |
| `ContinuousIntegrationBuild` | `true` when CI is detected | Enables .NET SDK CI build behavior. |
| `RestoreLockedMode` | `true` on CI | Uses locked restore on CI. |
| `NuGetAudit` | `true` | Enables NuGet vulnerability auditing. |
| `NuGetAuditMode` | `all` | Audits direct and transitive dependencies. |
| `NuGetAuditLevel` | `low` | Reports vulnerabilities at low severity and above. |
| `WarningsAsErrors` | Adds `NU1900`-`NU1904` on CI or Release | Promotes NuGet audit warnings to errors. |
| `GenerateSBOM` | `true` on CI | Generates a software bill of materials. |

### Test Projects

Projects whose names contain `.Tests.` are treated as test projects.

| Property | Default | Effect |
| --- | --- | --- |
| `IsTestableProject` | `true` when project name contains `.Tests.` | Marks projects that should receive test defaults. |
| `IsTestProject` | `true` for testable projects | Marks the project for test tooling. |
| `IsPublishable` | `false` for test projects | Prevents publishing test projects. |
| `IsPackable` | `false` for test projects | Prevents packing test projects. |
| `EnableCodeCoverage` | `true` on CI | Enables coverage collection. |
| `OptimizeVsTestRun` | `true` | Disables analyzers during `dotnet test`. Set `false` to keep analyzers enabled. |
| `UseMicrosoftTestingPlatform` | Auto | Uses MTP when `xunit.v3.mtp-v2` or `TUnit` is referenced. Force with `true` or `false`. |
| `EnableDefaultTestSettings` | `true` | Adds crash dumps, hang dumps, TRX output, loggers, and minimum-test expectations. |
| `VSTestBlame` | `true` | Enables VSTest blame. |
| `VSTestBlameCrash` | `true` | Enables crash dump collection. |
| `VSTestBlameHang` | `true` | Enables hang dump collection. |
| `VSTestBlameHangTimeout` | `10min` | Sets the VSTest hang timeout. |
| `VSTestLogger` | `trx;console%3bverbosity=normal` | Adds standard VSTest loggers. GitHub Actions gets `GitHubActions` too. |

### Web Containers

Container defaults only activate for `Microsoft.NET.Sdk.Web` projects running on GitHub Actions.

| Property | Default | Effect |
| --- | --- | --- |
| `EnableSdkContainerSupport` | `true` on GitHub Actions Web projects | Enables SDK container publishing support. |
| `ContainerRegistry` | `ghcr.io` | Uses GitHub Container Registry. |
| `ContainerRepository` | GitHub owner plus kebab-case repo name | Computes the default image repository. |
| `ContainerImageTagsMainVersionPrefix` | `1.0` | Prefix for main-branch image tags. |
| `ContainerImageTagsIncludeLatest` | `true` | Adds `latest` on main. |
| `ContainerImageTags` | Computed | Uses `<prefix>.<run-number>;latest` on main and `0.0.1-preview.<sha>` elsewhere. |

### Npm Restore

Npm restore is opt-in.

| Property or item | Default | Effect |
| --- | --- | --- |
| `HeadlessEnableNpmRestore` | `false` | Enables npm restore integration. |
| `EnableDefaultNpmPackageFile` | Enabled unless `false` | Adds `$(MSBuildProjectDirectory)/package.json` as an `NpmPackageFile` when present. |
| `NpmPackageFile` | Explicit item | Adds custom package files to restore. |
| `NpmRestoreLockedMode` | `true` on CI or locked restore | Uses `npm ci`; otherwise uses `npm install`. |
| `HeadlessNpmInstall` | Target | Runs `npm install --no-fund --no-audit` for configured package files. |

### Packaging Metadata

| Property | Default | Effect |
| --- | --- | --- |
| `PackageId` | `$(MSBuildProjectName)` | Default package ID. |
| `Title` | `$(MSBuildProjectName)` | Default package title. |
| `Company` | `Mahmoud Shaheen` | Default company. |
| `Authors` | `Mahmoud Shaheen` | Default authors. |
| `PackageLicenseExpression` | `MIT` | Default package license when no license file is set. |
| `PublishRepositoryUrl` | `true` | Publishes repository metadata. |
| `RepositoryType` | `git` | Marks the repository type. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources in PDBs. |
| `IncludeSymbols` | `true` | Produces symbol packages. |
| `SymbolPackageFormat` | `snupkg` | Uses modern symbol packages. |
| `SearchReadmeFileAbove` | `false` | Searches parent directories for README files when enabled. |
| `DisableReadme` | unset | Set `true` to skip README package metadata and packing. |
| `DisablePackageLogo` | unset | Set `true` to skip package icon metadata and packing. |

### Repository Config Files

The SDK does not overwrite solution-level config files unless explicitly requested.

| Property | Default | Effect |
| --- | --- | --- |
| `HeadlessCopyDefaultConfigFilesToSolutionDir` | `false` | Copies `.editorconfig`, `.csharpierignore`, `.gitignore`, and `.gitattributes` to `$(SolutionDir)`. |
| `HeadlessCopyEditorConfigToSolutionDir` | `false` | Copies only the bundled `.editorconfig`. |
| `HeadlessCopyCSharpierIgnoreToSolutionDir` | Follows `HeadlessCopyDefaultConfigFilesToSolutionDir` | Copies the bundled `.csharpierignore`. |
| `HeadlessCopyGitIgnoreToSolutionDir` | Follows `HeadlessCopyDefaultConfigFilesToSolutionDir` | Copies the bundled `.gitignore`. |
| `HeadlessCopyGitAttributesToSolutionDir` | Follows `HeadlessCopyDefaultConfigFilesToSolutionDir` | Copies the bundled `.gitattributes`. |

Example:

```xml
<PropertyGroup>
  <HeadlessCopyDefaultConfigFilesToSolutionDir>true</HeadlessCopyDefaultConfigFilesToSolutionDir>
</PropertyGroup>
```

### Optional Runtime Defaults

| Property | Default | Effect |
| --- | --- | --- |
| `HeadlessSingleFileApp` | `true` when `FileBasedProgram=true` | Applies analyzer relaxations for file-based apps. |
| `HeadlessInferTargetFramework` | `false` | Infers `TargetFramework` from the installed .NET SDK when the project omits target frameworks. |
| `HeadlessEnableStrictSystemTextJsonRuntimeDefaults` | `false` | On net9+, opts into strict System.Text.Json constructor and nullable runtime switches. |

### Import Switches

Use these when a consumer needs to remove a whole feature area.

| Property | Effect |
| --- | --- |
| `DisableSupportPackageInformation` | Skips package metadata defaults. |
| `DisableSupportImplicitAnalyzers` | Skips implicit analyzer package references. |
| `DisableImplicitAnalyzers` | Keeps the import but skips analyzer references. |
| `DisableSupportBannedSymbols` | Skips banned-symbol additional files. |
| `DisableSupportWebContainer` | Skips GitHub Actions web container defaults. |
| `DisableSupportAnalyzerHygiene` | Skips analyzer cleanup such as SponsorLink removal. |
| `DisableSupportSingleFileApp` | Skips file-based app analyzer relaxations. |
| `DisableSupportTargetFrameworkInference` | Skips target framework inference support. |
| `DisableSupportNpm` | Skips npm restore targets. |
| `DisableSupportSbom` | Skips SBOM generation support. |
| `DisableSupportEmbedBinlog` | Skips binlog enrichment. |
| `DisableSupportCopyrightTargets` | Skips copyright target imports. |
| `DisableSupportNuGetAuditTargets` | Skips NuGet audit target imports. |

## Build And Publish

```bash
dotnet pack --configuration Release --output ./artifacts/packages-results
dotnet nuget push ./artifacts/packages-results/*.nupkg \
  --source https://nuget.pkg.github.com/xshaheen/index.json \
  --skip-duplicate \
  --api-key "$NUGET_API_KEY"
```
