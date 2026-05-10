# Headless.NET.Sdk

`Headless.NET.Sdk` is an opinionated MSBuild SDK I built for my own .NET projects and the teams I work with. The repo is public and you're welcome to consume it, but the defaults reflect a strict house style — `Newtonsoft.Json` banned, `latest-all` analyzer level, MSBuild warnings as errors on CI, `RollForward=LatestMajor` for executables, implicit analyzer hygiene. If any of that doesn't fit your project, every default is overridable via the `Disable*` and `Headless*` properties documented in the Configuration Reference below.

The intent is simple: every project starts with the same strict baseline, then opts out only where the local project has a clear reason.

## What It Standardizes

- Build defaults: nullable reference types, implicit usings, latest C#, strict compiler features, deterministic output, static graph restore, and package validation.
- Quality gates: `AnalysisLevel=latest-all`, .NET analyzers, Meziantou, AsyncFixer, Asyncify, Microsoft.VisualStudio.Threading, SmartAnalyzers multithreading, Roslynator, ReflectionAnalyzers, ErrorProne.NET, banned API rules, NuGet audit, and code style enforcement.
- Test projects: explicit classification via `Headless.NET.Sdk.Test` or `IsTestableProject=true`, MTP or VSTest defaults, dumps on crash or hang, CI coverage, GitHub Actions logging, and faster `dotnet test` runs.
- CI behavior: provider detection, `ContinuousIntegrationBuild`, locked restore behavior, SBOM generation, and stricter warning handling.
- Packaging: default authors/company metadata, README/LICENSE/logo packing, Source Link, symbol packages, and repository metadata.
- App support: web container tagging on GitHub Actions, file-based app relaxations, optional target framework inference, and optional strict System.Text.Json runtime switches.
- Diagnostics: embeds editorconfig, banned-symbol files, and GitHub Actions environment details into binlogs.

## Package Family

| Package | Wraps | Use for |
| --- | --- | --- |
| [`Headless.NET.Sdk`](https://www.nuget.org/packages/Headless.NET.Sdk) | `Microsoft.NET.Sdk` | Libraries and console apps — the base SDK every other variant builds on. |
| [`Headless.NET.Sdk.Web`](https://www.nuget.org/packages/Headless.NET.Sdk.Web) | `Microsoft.NET.Sdk.Web` | ASP.NET Core / Web APIs, with GitHub Actions container support. |
| [`Headless.NET.Sdk.Test`](https://www.nuget.org/packages/Headless.NET.Sdk.Test) | `Microsoft.NET.Sdk` | Test projects — forces `IsTestableProject` and `IsTestProject`. |
| [`Headless.NET.Sdk.Razor`](https://www.nuget.org/packages/Headless.NET.Sdk.Razor) | `Microsoft.NET.Sdk.Razor` | Razor class libraries. |
| [`Headless.NET.Sdk.BlazorWebAssembly`](https://www.nuget.org/packages/Headless.NET.Sdk.BlazorWebAssembly) | `Microsoft.NET.Sdk.BlazorWebAssembly` | Blazor WebAssembly client apps. |
| [`Headless.NET.Sdk.WindowsDesktop`](https://www.nuget.org/packages/Headless.NET.Sdk.WindowsDesktop) | `Microsoft.NET.Sdk.WindowsDesktop` | WPF and Windows Forms apps. |

## Usage

Choose the consumption style based on how early the defaults must run.

### Package Reference

Use this when you want the package imported through NuGet's normal `build/` assets.

```bash
dotnet add package Headless.NET.Sdk --version x.y.z
```

```xml
<PackageReference Include="Headless.NET.Sdk" Version="x.y.z" PrivateAssets="all" />
```

In this mode, NuGet imports `build/Headless.NET.Sdk.props` and `build/Headless.NET.Sdk.targets` through the normal package asset pipeline.
Project-type packages can also be used by matching them with the corresponding Microsoft SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Web" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### MSBuild SDK

Use this when the defaults must be visible before the consumer's `Directory.Build.props`.

```jsonc
{
  "msbuild-sdks": {
    "Headless.NET.Sdk": "x.y.z",
    "Headless.NET.Sdk.Web": "x.y.z",
    "Headless.NET.Sdk.Test": "x.y.z",
    "Headless.NET.Sdk.Razor": "x.y.z",
    "Headless.NET.Sdk.BlazorWebAssembly": "x.y.z",
    "Headless.NET.Sdk.WindowsDesktop": "x.y.z"
  }
}
```

```xml
<Project Sdk="Headless.NET.Sdk">
</Project>
```

Project-type SDK packages wrap the matching Microsoft SDK while applying the same Headless defaults:

| Headless SDK | Base SDK | Extra behavior |
| --- | --- | --- |
| `Headless.NET.Sdk` | `Microsoft.NET.Sdk` | Default library/console SDK. |
| `Headless.NET.Sdk.Web` | `Microsoft.NET.Sdk.Web` | Web SDK with Headless defaults and web container support. |
| `Headless.NET.Sdk.Test` | `Microsoft.NET.Sdk` | Forces `IsTestableProject` and `IsTestProject`. |
| `Headless.NET.Sdk.Razor` | `Microsoft.NET.Sdk.Razor` | Razor SDK with Headless defaults. |
| `Headless.NET.Sdk.BlazorWebAssembly` | `Microsoft.NET.Sdk.BlazorWebAssembly` | Blazor WebAssembly SDK with Headless defaults. |
| `Headless.NET.Sdk.WindowsDesktop` | `Microsoft.NET.Sdk.WindowsDesktop` | Windows Desktop SDK with Headless defaults. |

```xml
<Project Sdk="Headless.NET.Sdk.Web/x.y.z">
</Project>
```

You can also pin the SDK directly in the project file:

```xml
<Project Sdk="Headless.NET.Sdk/x.y.z">
</Project>
```

Or layer it on top of the .NET SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Headless.NET.Sdk" Version="x.y.z" />
</Project>
```

### File-Based Apps

.NET 10+ file-based apps can use the SDK directive:

```csharp
#:sdk Headless.NET.Sdk@x.y.z
Console.WriteLine("Hello!");
```

```bash
dotnet run Program.cs
```

## Import Model

`Headless.NET.Sdk` ships assets in `sdk/`, `build/`, `buildMultiTargeting/`, and `buildTransitive/`.

| Mode | Import timing | Use when |
| --- | --- | --- |
| `PackageReference` | NuGet imports package build assets through the standard package flow. Project-type packages set Headless identity, but the project still chooses the matching Microsoft SDK. | You want the current default consumption path. |
| `<Project Sdk="Headless.NET.Sdk">` | `sdk/Sdk.props` wires `build/Headless.NET.Sdk.props` before `Directory.Build.props` and targets before `Microsoft.NET.Sdk` targets. | Repository-wide defaults need to be visible early. |
| `<Project Sdk="Headless.NET.Sdk.Web">` and project-type SDKs | The wrapper SDK imports its matching Microsoft SDK and wires the corresponding `build/Headless.NET.Sdk.*.props` and `.targets` files. | You want defaults plus the right base SDK from a single SDK name. |
| `<Sdk Name="Headless.NET.Sdk" />` | Imported as an additional SDK inside a `Microsoft.NET.Sdk` project. | You want normal .NET SDK behavior plus Headless defaults. |
| `#:sdk` | Imported by the file-based app SDK directive. | Single-file experiments or scripts should share the same defaults. |

## Configuration Reference

Many values apply only when the consuming project has not already set the property; others intentionally set the shared Headless baseline. Feature-level imports can also be disabled with the `DisableSupport*` switches listed below.

### General Build

| Property | Default | Effect |
| --- | --- | --- |
| `HeadlessSdkName` | `Headless.NET.Sdk` | SDK identity used in generated assembly metadata. |
| `HeadlessSdkProjectType` | `Default` | Project-type identity used in generated assembly metadata. |
| `Configuration` | `Debug` | Default build configuration. |
| `Platform` | `AnyCPU` | Default platform. |
| `RootNamespace` | `$(MSBuildProjectName)` | Aligns namespace with project name. |
| `AssemblyName` | `$(MSBuildProjectName)` | Aligns assembly name with project name. |
| `LangVersion` | `latest` | Uses the latest available C# language version. |
| `Nullable` | `enable` | Enables nullable reference types. |
| `ImplicitUsings` | `enable` | Enables SDK implicit usings. |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. Missing XML docs are suppressed by default. |
| `DisableDocumentationWarnings` | `true` | Suppresses `CS1573` and `CS1591`. Set `false` to enforce documentation warnings. |
| `Features` | `strict` | Enables strict compiler feature flags. |
| `Deterministic` | `true` | Produces reproducible builds when inputs match. |
| `RestoreUseStaticGraphEvaluation` | `true` | Uses static graph restore. |
| `RestoreSerializeGlobalProperties` | `true` | Serializes restore global properties. |
| `EnablePackageValidation` | `true` | Checks packages for breaking API changes. |
| `MSBuildTreatWarningsAsErrors` | `true` on CI or Release | Promotes MSBuild warnings to errors. |
| `RollForward` | `LatestMajor` for non-test apps | Allows apps to run on newer installed runtime majors. |
| `IsPackable` | Matches the selected SDK | Defaults to `true` for `Headless.NET.Sdk`, Razor, and Windows Desktop; `false` for Web and Blazor WebAssembly. Test projects force `false`. |
| `HeadlessSuppressNonPackablePackWarning` | `true` | Suppresses NuGet's non-packable project warning when `IsPackable=false`, keeping solution-level `dotnet pack` CI-safe. |
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
| Implicit analyzer packages | Enabled | Adds Meziantou, AsyncFixer, Asyncify, Microsoft.VisualStudio.Threading.Analyzers, SmartAnalyzers.MultithreadingAnalyzer, Roslynator.Analyzers, ReflectionAnalyzers, ErrorProne.NET.CoreAnalyzers, and banned API analyzers. |
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

Test classification is explicit. A project becomes a test project via either:

1. `<Project Sdk="Headless.NET.Sdk.Test">` — the Test SDK forces `IsTestableProject=true` and `IsTestProject=true`.
2. `<IsTestableProject>true</IsTestableProject>` in the consumer's csproj or `Directory.Build.props`.

Name-based inference (`MyApp.Tests`, `.UnitTests`, etc.) is intentionally not supported — too many false positives and false negatives for a public SDK.

| Property | Default | Effect |
| --- | --- | --- |
| `IsTestableProject` | `false` (set to `true` by `Headless.NET.Sdk.Test` or by the consumer) | Marks projects that should receive test defaults. |
| `IsTestProject` | `false` (set to `true` for testable projects via `Headless.NET.Sdk.Test`) | Marks the project for test tooling. |
| `IsPublishable` | `false` for test projects | Prevents publishing test projects. |
| `IsPackable` | `false` for test projects | Prevents packing test projects and suppresses the non-packable pack warning so solution-level `dotnet pack` remains CI-safe. |
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

### Packaging Metadata

| Property | Default | Effect |
| --- | --- | --- |
| `PackageId` | `$(MSBuildProjectName)` | Default package ID. |
| `Title` | `$(MSBuildProjectName)` | Default package title. |
| `Company` | `Mahmoud Shaheen` | Default company. |
| `Authors` | `Mahmoud Shaheen` | Default authors. |
| `PackageLicenseFile` | Auto-detected | Uses `LICENSE` or `LICENSE.txt` when found. No license expression is invented by default. |
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
| `DisableSupportAnalyzerEditorConfigs` | Skips bundled analyzer editorconfig imports. |
| `DisableSupportBannedSymbols` | Skips banned-symbol additional files. |
| `DisableSupportWebContainer` | Skips GitHub Actions web container defaults. |
| `DisableSupportAnalyzerHygiene` | Skips analyzer cleanup such as SponsorLink removal. |
| `DisableSupportSingleFileApp` | Skips file-based app analyzer relaxations. |
| `DisableSupportTargetFrameworkInference` | Skips target framework inference support. |
| `DisableSupportSbom` | Skips SBOM generation support. |
| `DisableSupportEmbedBinlog` | Skips binlog enrichment. |
| `DisableSupportCopyright` | Skips copyright target imports. |
| `DisableSupportNuGetAudit` | Skips NuGet audit target imports. |

## Repository Layout

```
src/
  Headless.NET.Sdk/                      # base SDK + canonical README packed with all packages
  Headless.NET.Sdk.Web/                  # project-type wrapper for Microsoft.NET.Sdk.Web
  Headless.NET.Sdk.Test/                 # ... for test projects
  Headless.NET.Sdk.Razor/                # ... for Razor class libraries
  Headless.NET.Sdk.BlazorWebAssembly/    # ... for Blazor WebAssembly
  Headless.NET.Sdk.WindowsDesktop/       # ... for WPF / WinForms
tests/
  Headless.NET.Sdk.Tests.Integrations/   # build + pack + restore integration tests
```

## Build And Publish

For contributors releasing the SDK itself (consumers do not need to run these):

CI runs on pull requests and pushes to `main`. Package publishing runs on version-like tags such as `1.2.3` and from manual workflow dispatch, then pushes the built `.nupkg` files to GitHub Packages.

```bash
dotnet build headless-sdk.slnx
dotnet pack --configuration Release --output ./artifacts/packages-results
dotnet nuget push ./artifacts/packages-results/*.nupkg \
  --source https://nuget.pkg.github.com/xshaheen/index.json \
  --skip-duplicate \
  --api-key "$NUGET_API_KEY"
```

## License

See [LICENSE](LICENSE).
