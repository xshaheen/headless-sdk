# Headless.NET.Sdk

`Headless.NET.Sdk` is an opinionated MSBuild SDK I built for my own .NET projects and the teams I work with. The repo is public and you're welcome to consume it, but the defaults reflect a strict house style — `Newtonsoft.Json` banned, `latest-all` analyzer level, MSBuild warnings as errors on CI, `RollForward=LatestMajor` for executables, implicit analyzer hygiene. If any of that doesn't fit your project, every default is overridable via the `Disable*` and `Headless*` properties documented in the Configuration Reference below.

The intent is simple: every project starts with the same strict baseline, then opts out only where the local project has a clear reason.

## What It Standardizes

- Build defaults: nullable reference types, implicit usings, latest C#, strict compiler features, deterministic output, static graph restore, and package validation.
- Quality gates: `AnalysisLevel=latest-all`, .NET analyzers, Meziantou, AsyncFixer, Asyncify, Microsoft.VisualStudio.Threading, SmartAnalyzers multithreading, Roslynator, ReflectionAnalyzers, ErrorProne.NET, banned API rules, NuGet audit, and code style enforcement.
- Test projects: explicit classification via `Headless.NET.Sdk.Test`, `IsTestProject=true`, or `IsTestHarnessProject=true`; MTP or VSTest defaults, dumps on crash or hang, CI coverage, GitHub Actions logging, and faster `dotnet test` runs.
- CI behavior: provider detection, `ContinuousIntegrationBuild`, locked restore behavior, and stricter warning handling.
- Packaging: default authors/company metadata, README/LICENSE/logo packing, Source Link, symbol packages, and repository metadata.
- App support: web container tagging on GitHub Actions, file-based app relaxations, optional target framework inference, and optional strict System.Text.Json runtime switches.
- Diagnostics: embeds editorconfig, banned-symbol files, and GitHub Actions environment details into binlogs.

## Package Family

| Package | Wraps | Use for |
| --- | --- | --- |
| [`Headless.NET.Sdk`](https://www.nuget.org/packages/Headless.NET.Sdk) | `Microsoft.NET.Sdk` | Libraries and console apps — the base SDK every other variant builds on. |
| [`Headless.NET.Sdk.Web`](https://www.nuget.org/packages/Headless.NET.Sdk.Web) | `Microsoft.NET.Sdk.Web` | ASP.NET Core / Web APIs, with GitHub Actions container support. |
| [`Headless.NET.Sdk.Test`](https://www.nuget.org/packages/Headless.NET.Sdk.Test) | `Microsoft.NET.Sdk` | Test projects — forces `IsTestProject`. |
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
| `Headless.NET.Sdk.Test` | `Microsoft.NET.Sdk` | Forces `IsTestProject`. |
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

### Assembly Metadata

`SupportAssemblyAttributes.targets` emits a few assembly-level attributes into every consuming project.

| Behavior | Default | Effect |
| --- | --- | --- |
| `[assembly: CLSCompliant(false)]` | Emitted | Marks the assembly non-CLS-compliant. Set `HeadlessEmitClsCompliantAttribute=false` to skip it so you can declare your own `[assembly: CLSCompliant(...)]` (e.g. `true`) without a `CS0579` duplicate-attribute error. |
| `[assembly: AssemblyMetadata("Headless.NET.Sdk.SdkName"/"...ProjectType", ...)]` | Emitted | Records which Headless SDK variant and project type produced the assembly. |
| `InternalsVisibleTo` for `<Project>.Tests.Architecture`, `.Tests.Unit`, `.Tests.Integration`, `.Tests.Acceptance` | Added for unsigned non-test projects | Grants the conventionally named test assemblies access to internals. Harmless if those assemblies don't exist — it only bakes in the naming convention. Set `HeadlessEmitInternalsVisibleToAttributes=false` to skip these defaults. Signed projects skip them automatically because strong-named friend assemblies require public keys; add your own `InternalsVisibleTo` items when signing. |
| `[assembly: ExcludeFromCodeCoverage]` | Added for test projects (net5.0+) | Excludes the test assembly itself from coverage. |

### Analysis And API Hygiene

| Property | Default | Effect |
| --- | --- | --- |
| `AnalysisLevel` | `latest-all` | Enables the latest analyzer rule set. |
| `AnalysisMode` | `All` | Runs all analyzer categories. |
| `EnableNETAnalyzers` | `true` | Enables .NET analyzers. |
| `EnforceCodeStyleInBuild` | `true` | Enforces code style during build. |
| `ReportAnalyzer` | `true` | Includes analyzer timing/reporting data. |
| `RunAnalyzersDuringBuild` | `true` | Runs analyzers during builds. |
| `HeadlessEnforceConfigureAwait` | `false` | Suppresses `CA2007` by default since most Headless apps run without a `SynchronizationContext`. Set `true` to surface `CA2007` (`ConfigureAwait(false)`) as a warning — intended for library code consumed by `Headless.NET.Sdk.WindowsDesktop` apps, which carry a `SynchronizationContext`. |
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
| `RestoreLockedMode` | `true` on CI for MSBuild SDK consumption | Uses locked restore on CI when the project consumes Headless as an MSBuild SDK (`<Project Sdk="Headless.NET.Sdk/...">`, `global.json` MSBuild SDK, `<Sdk Name="Headless.NET.Sdk" />`, or `#:sdk`). Commit lock files or explicitly set `RestoreLockedMode=false` for restore-only CI jobs that are expected to update dependencies. PackageReference consumers should set restore lock policy in the project or `Directory.Build.props` because NuGet package build assets are not a reliable source for restore-time policy. |
| `NuGetAudit` | `true` | Enables NuGet vulnerability auditing. |
| `NuGetAuditMode` | `all` | Audits direct and transitive dependencies. |
| `NuGetAuditLevel` | `low` | Reports vulnerabilities at low severity and above. |
| `WarningsAsErrors` | Adds `NU1901`-`NU1904` on CI or Release | Promotes NuGet audit vulnerability warnings to errors. `NU1900` (audit source unreachable) is left as a warning so a connectivity blip does not fail the build. |
| `GenerateSBOM` | `false` | Opts into software bill of materials generation through `Microsoft.Sbom.Targets`. |

### Test Projects

Test classification is explicit. A project receives test defaults via either:

1. `<Project Sdk="Headless.NET.Sdk.Test">` — the Test SDK forces `IsTestProject=true`.
2. `<IsTestProject>true</IsTestProject>` in the consumer's csproj or `Directory.Build.props`.
3. `<IsTestHarnessProject>true</IsTestHarnessProject>` for shared test harness projects that should receive test defaults but must not execute as test hosts.

Name-based inference (`MyApp.Tests`, `.UnitTests`, etc.) is intentionally not supported — too many false positives and false negatives for a public SDK.

| Property | Default | Effect |
| --- | --- | --- |
| `IsTestHarnessProject` | `false` | Marks a shared test harness project. The SDK applies the same test defaults as `IsTestProject`, then forces `IsTestProject=false`, `IsTestingPlatformApplication=false`, and `GenerateRuntimeConfigurationFiles=true` so the harness does not execute as a test host. |
| `IsTestProject` | `false` (set to `true` for runnable test projects) | Marks the project for test host discovery and execution. Harness projects force it back to `false`. |
| `IsPublishable` | `false` for test projects | Prevents publishing test projects. |
| `IsPackable` | `false` for test projects | Prevents packing test projects and suppresses the non-packable pack warning so solution-level `dotnet pack` remains CI-safe. |
| `NoWarn` | Adds test-noise suppressions, including `CA1849`, `MA0042`, `MA0166`, `CA1861`, `CA1859`, and `CA1720` | Allows controlled blocking calls, direct time-based APIs, test-only array arguments, concrete-type suggestions, and type-name identifiers without per-file pragmas. |
| `EnableCodeCoverage` | `true` on CI | Enables coverage collection and applies the SDK runsettings exclusions for test assemblies, generated sources, and EF migrations. |
| `OptimizeVsTestRun` | `true` | Disables analyzers during `dotnet test`. Set `false` to keep analyzers enabled. |
| `UseMicrosoftTestingPlatform` | Auto | Uses MTP when `xunit.v3.mtp-v2` or `TUnit` is referenced. Force with `true` or `false`. |
| `EnableDefaultTestSettings` | `true` | Adds crash dumps, hang dumps, TRX output, loggers, and minimum-test expectations. |
| `VSTestBlame` | `true` | Enables VSTest blame. |
| `VSTestBlameCrash` | `true` | Enables crash dump collection. |
| `VSTestBlameHang` | `true` | Enables hang dump collection. |
| `VSTestBlameHangTimeout` | `10min` | Sets the VSTest hang timeout. |
| `VSTestLogger` | `trx;console%3bverbosity=normal` | Adds standard VSTest loggers. GitHub Actions VSTest projects get `GitHubActions` too; MTP projects use platform annotations. |

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
| `HeadlessSymbolFormat` | `embedded` (`none` for Blazor WASM) | Selects the debug-symbols policy. See [Symbols](#symbols). |
| `SearchReadmeFileAbove` | `false` | Searches parent directories for README files when enabled. |
| `DisableReadme` | unset | Set `true` to skip README package metadata and packing. |
| `DisablePackageLogo` | unset | Set `true` to skip package icon metadata and packing. |

### Symbols

`HeadlessSymbolFormat` owns the debug-symbols policy for non-test projects:

| Value | Effect |
| --- | --- |
| `embedded` (default) | `DebugType=embedded`, `IncludeSymbols=false`. The PDB ships inside the assembly, so symbols resolve on every feed — including GitHub Packages, which has no symbol server to serve a `.snupkg` from. No symbol package is produced. |
| `snupkg` | `IncludeSymbols=true`, `SymbolPackageFormat=snupkg` — the previous SDK default (portable PDB + `.snupkg` pair). `DebugType` is left untouched. |
| `none` | `IncludeSymbols=false`, `DebugType` left untouched. No symbols ship anywhere. |

A consumer-set `DebugType`, `IncludeSymbols`, or `SymbolPackageFormat` always wins over the policy.

Caveats:

- `dotnet pack --include-symbols` passes `IncludeSymbols=true` as a **global** MSBuild property,
  which overrides project-level properties (including this policy). Drop that flag when relying
  on the `embedded` default — otherwise you get a legacy `.symbols.nupkg` on top of the embedded
  PDB.
- `Microsoft.NET.Sdk` defaults `DebugType=portable` before project evaluation, so an explicit
  consumer `DebugType=portable` is indistinguishable from "unset" and the `embedded` mode rewrites
  it. To keep portable PDBs, set `HeadlessSymbolFormat=none` (or `snupkg`) instead.
- **Blazor WebAssembly exception:** WASM apps ship their assemblies to the browser, and an
  embedded PDB survives into the published `_framework` payload — leaking debug info and inflating
  the download. Portable PDBs are excluded from that payload, so projects on
  `Microsoft.NET.Sdk.BlazorWebAssembly` (or the `Headless.NET.Sdk.BlazorWebAssembly` wrapper)
  default to `none`.

### Repository Config Files

The SDK bundles convenience repo files (`.editorconfig`, `.csharpierignore`, `.gitignore`,
`.gitattributes`) that you can scaffold into your repository on demand. These are version-control
and formatting conveniences, not build inputs. The SDK no longer copies them during `Build` —
scaffolding runs only when you explicitly invoke the dedicated target:

```bash
dotnet build -t:HeadlessScaffoldConfigFiles
```

Scaffolding is create-if-absent: each file is written only when it does not already exist, so an
existing file you own is never overwritten. Pass `-p:HeadlessOverwriteConfigFiles=true` to force a
replacement.

With no `HeadlessCopy*` selector set, invoking the target scaffolds the full default set (all four
files). Set one or more selectors to scaffold only those files; the master flag selects all four.

| Property | Default | Effect |
| --- | --- | --- |
| `HeadlessOverwriteConfigFiles` | `false` | Forces overwrite of existing destination files when scaffolding. |
| `HeadlessConfigFilesDir` | unset | Destination directory. Falls back to `$(SolutionDir)`, then the project directory. |
| `HeadlessCopyDefaultConfigFilesToSolutionDir` | `false` | Selects `.editorconfig`, `.csharpierignore`, `.gitignore`, and `.gitattributes`. |
| `HeadlessCopyEditorConfigToSolutionDir` | `false` | Selects only the bundled `.editorconfig`. |
| `HeadlessCopyCSharpierIgnoreToSolutionDir` | Follows `HeadlessCopyDefaultConfigFilesToSolutionDir` | Selects the bundled `.csharpierignore`. |
| `HeadlessCopyGitIgnoreToSolutionDir` | Follows `HeadlessCopyDefaultConfigFilesToSolutionDir` | Selects the bundled `.gitignore`. |
| `HeadlessCopyGitAttributesToSolutionDir` | Follows `HeadlessCopyDefaultConfigFilesToSolutionDir` | Selects the bundled `.gitattributes`. |

Examples:

```bash
# Scaffold every config file that doesn't already exist:
dotnet build -t:HeadlessScaffoldConfigFiles

# Scaffold only .gitignore, forcing it to overwrite an existing file:
dotnet build -t:HeadlessScaffoldConfigFiles \
  -p:HeadlessCopyGitIgnoreToSolutionDir=true -p:HeadlessOverwriteConfigFiles=true
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
| `DisableSupportSbom` | Skips the opt-in SBOM generation support import. |
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

The test-tool versions injected into consumer test projects (`Microsoft.NET.Test.Sdk`, the `Microsoft.Testing.Extensions.*` packages, and `GitHubActionsTestLogger` for VSTest projects on GitHub Actions) cannot be governed by a consumer's Central Package Management, so they ship as concrete versions inside the SDK package. Their single source of truth is `Directory.Packages.props`: the `GenerateHeadlessTestToolVersions` target regenerates `build/SupportTestProjects.Versions.props` from those pins on every build (CI runs it explicitly before build/pack), and `VersionConsistencyTests` guards the result. To bump one, change only `Directory.Packages.props` (Dependabot does the same) and rebuild — the generated file follows automatically. A dedicated `Headless.NET.Sdk.TestToolVersions.Anchor` project references those packages (with `IncludeAssets=none`, so nothing flows into its build) purely so Dependabot — which only updates a central version a project actually references — opens the bump PRs.

## License

See [LICENSE](LICENSE).
