# Headless.NET.Sdk

`Headless.NET.Sdk` is an opinionated MSBuild SDK family for .NET projects. It is consumer-facing build infrastructure that standardizes evaluation order, restore policy, analyzers, packaging, and test execution across any compatible .NET repository.

> [!IMPORTANT]
> The packages are distributed through GitHub Packages and NuGet.org. NuGet.org publication requires a published GitHub Release plus approval in the protected `NuGet Release` environment. The SDK family is not specific to Headless Framework. This repository currently has no license; source availability does not itself grant legal rights to use, modify, or redistribute its contents.

## Support contract

- The package family is built and validated with the repository-pinned .NET 10 SDK. Headless does not restrict consumer target frameworks; compatibility is determined by the selected Microsoft SDK and its installed targeting packs or workloads.
- Every MSBuild project must declare its target framework explicitly. Headless does not infer one.
- All five consumption modes below are first-class. Build, pack, analyzer, and application policy
  is identical; the documented first-clean-restore bootstrap is required for PackageReference mode.
- Package assets apply only to the project that opts in. The packages do not ship `buildTransitive` assets.
- Multi-targeting outer builds remain supported through `buildMultiTargeting`; inner builds receive the normal `build` contract exactly once.
- Named quality policies are authoritative. The analyzer infrastructure and CI quality gates are not consumer opt-outs; the two shipped banned-symbol lists retain the documented whole-policy and per-list opt-outs.

## Package family

| Package | Microsoft SDK | Intended project |
| --- | --- | --- |
| `Headless.NET.Sdk` | `Microsoft.NET.Sdk` | Libraries, console apps, and the shared baseline |
| `Headless.NET.Sdk.Web` | `Microsoft.NET.Sdk.Web` | ASP.NET Core and Web API apps |
| `Headless.NET.Sdk.Test` | `Microsoft.NET.Sdk` | Microsoft Testing Platform test projects |
| `Headless.NET.Sdk.Razor` | `Microsoft.NET.Sdk.Razor` | Razor class libraries |
| `Headless.NET.Sdk.BlazorWebAssembly` | `Microsoft.NET.Sdk.BlazorWebAssembly` | Blazor WebAssembly apps |
| `Headless.NET.Sdk.WindowsDesktop` | `Microsoft.NET.Sdk.WindowsDesktop` | WPF and Windows Forms apps |

Satellite packages are self-contained: each carries the shared base assets plus its project-type wrapper, so resolving a satellite does not depend on a separate Headless SDK package version.

## Feed setup

Stable releases on NuGet.org require no additional package source. To consume a GitHub Packages build, authenticate with an account or token that can read the package, then add the account feed to the consumer's `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="github" value="https://nuget.pkg.github.com/xshaheen/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="github">
      <package pattern="Headless.NET.Sdk*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
  <auditSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </auditSources>
</configuration>
```

Do not commit credentials to the repository. Supply them through the supported NuGet credential provider or the CI secret store.
The more-specific `Headless.NET.Sdk*` mapping keeps the family on GitHub Packages; the `*` fallback
routes all other direct and transitive packages to nuget.org and satisfies Central Package Management's
multi-source mapping requirement.

## Consumption modes

Replace `x.y.z` and the package name with the required family member.

### 1. PackageReference

Use the matching Microsoft SDK and add Headless directly:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

For example, `Headless.NET.Sdk.Web` is paired with `Microsoft.NET.Sdk.Web`. Because no `buildTransitive` assets ship, referenced projects are unaffected unless they also opt in.

NuGet cannot import a package's `build` assets until the first restore has created
`obj/*.nuget.g.props` and `obj/*.nuget.g.targets`. Consequently, a clean project's first
PackageReference restore cannot receive restore-time lock and audit-warning policy from Headless.
Repositories using this mode must bootstrap those restore settings in `Directory.Build.props` (or
equivalent restore CLI arguments):

```xml
<Project>
  <PropertyGroup>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <RestoreLockedMode
      Condition="Exists('$(MSBuildProjectDirectory)/packages.lock.json')"
    >true</RestoreLockedMode>
    <WarningsAsErrors Condition="'$(CI)' == 'true'">$(WarningsAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsAsErrors>
    <WarningsNotAsErrors Condition="'$(CI)' == 'true'">$(WarningsNotAsErrors);NU1900;NU1905</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

This limitation is specific to the first clean PackageReference restore. MSBuild SDK resolution
loads Headless props before restore, and all modes receive the same post-restore project policy.

### 2. Project SDK

Pin the SDK in the project declaration:

```xml
<Project Sdk="Headless.NET.Sdk/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

The version may be omitted only when SDK resolution supplies it through `global.json`.

### 3. Additional SDK

Layer Headless over an already selected Microsoft SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Headless.NET.Sdk" Version="x.y.z" />

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

### 4. `global.json` MSBuild SDK resolution

Pin one or more family members centrally:

```json
{
  "sdk": {
    "version": "10.0.301",
    "rollForward": "disable"
  },
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

Projects can then use the versionless form:

```xml
<Project Sdk="Headless.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

### 5. .NET 10 file-based apps

Use the SDK directive. The .NET 10 file-app host supplies the file app's target framework; the Headless target-framework inference feature is not involved.

```csharp
#:sdk Headless.NET.Sdk@x.y.z

Console.WriteLine("Hello from Headless");
```

```bash
dotnet run App.cs
```

File-based apps always receive the shipped file-app analyzer profile. The relaxation is automatic because the single-file source model differs from a normal project; there is no Headless opt-out switch for it.

All six family members accept `#:sdk`. The Blazor WebAssembly and Windows Desktop wrappers preserve
their Headless identity over the base Microsoft SDK for file apps because a single source file cannot
author Blazor, WPF, or Windows Forms project items. This also avoids browser workload, Native AOT, and
legacy Windows Desktop assumptions that the generated file project cannot satisfy. Normal project
consumption retains the specialized Microsoft SDKs and their full behavior.

## Build and analysis baseline

The SDK establishes nullable reference types, implicit usings, latest C#, deterministic output, static-graph restore, XML documentation output, `AnalysisLevel=latest-all`, all analyzer categories, and code-style enforcement.

The following analyzer packages are injected as private, implicit dependencies for every consumer:

- `Meziantou.Analyzer`
- `Microsoft.CodeAnalysis.BannedApiAnalyzers`
- `AsyncFixer`
- `Asyncify`
- `Microsoft.VisualStudio.Threading.Analyzers`
- `SmartAnalyzers.MultithreadingAnalyzer`
- `Roslynator.Analyzers`
- `ReflectionAnalyzers`
- `ErrorProne.NET.CoreAnalyzers`

The sole self-reference exception is a project whose evaluated `PackageId` is
`Meziantou.Analyzer`; Headless omits that one analyzer reference so the analyzer package can use
the SDK without depending on itself. The other eight analyzer references and all mandatory policy
still apply.

The bundled general and Newtonsoft.Json banned-symbol lists are enabled by default. Consumers can disable the complete banned-symbol policy with `DisableSupportBannedSymbols=true`, or disable either list independently through `IncludeDefaultBannedSymbols=false` and `BannedNewtonsoftJsonSymbols=false`. The `Microsoft.CodeAnalysis.BannedApiAnalyzers` package remains part of the analyzer infrastructure.

The SDK owns the versions of all nine implicit analyzer references. Central Package Management
consumers must not add `PackageVersion` entries for those analyzer IDs. SDK-form consumption rejects
them as SDK-defined implicit references with NU1009; PackageReference consumption rejects conflicting
central versions against the package family's exact dependency ranges.

Headless adds its extra global usings only when `ImplicitUsings` is `enable` or `true` and the target framework is compatible with `net8.0`. Older or custom TFMs remain valid consumers without receiving namespaces that may not exist in their reference assemblies. Setting `ImplicitUsings=disable` prevents both the Microsoft implicit-usings feature and the Headless additions.

### Supported customization properties

The following properties are the supported consumer configuration surface. An empty value receives
the listed default; explicit values win unless the behavior is identified as mandatory below.

| Property | Default | Contract |
| --- | --- | --- |
| `DisableDocumentationWarnings` | `true` | Set `false` to report CS1573 and CS1591 while keeping XML documentation generation enabled. |
| `HeadlessEnforceConfigureAwait` | `false` | Set `true` to enable CA2007 through the shipped analyzer profile. |
| `DisableSponsorLink` | enabled unless `false` | Set `false` to retain SponsorLink and Moq analyzers that Headless removes by default. |
| `DisableSupportBannedSymbols` | `false` | Set `true` to omit both shipped banned-symbol lists. The banned-API analyzer package remains available. |
| `IncludeDefaultBannedSymbols` | `true` | Set `false` to omit the general .NET banned-symbol list. |
| `BannedNewtonsoftJsonSymbols` | `true` | Set `false` to permit Newtonsoft.Json APIs while retaining the general list. |
| `HeadlessEmitInternalsVisibleToAttributes` | `true` | Set `false` when the project owns its friend-assembly list. |
| `HeadlessEmitClsCompliantAttribute` | `true` | Set `false` when the project supplies its own `CLSCompliant` attribute. |
| `HeadlessEnableStrictSystemTextJsonRuntimeDefaults` | `false` | Enables the two process-wide strict System.Text.Json runtime switches for TFMs compatible with `net9.0`. |
| `HeadlessSymbolFormat` | `embedded` (`none` for Blazor WebAssembly) | Accepts `embedded`, `snupkg`, or `none`. |
| `EnablePackageValidation` | `true` (`false` for `PackAsTool`) | Enables Microsoft package validation for ordinary packages; Microsoft disables API compatibility validation for tool packages. Consumers may explicitly disable it. |
| `GenerateSBOM` | `false` | Generates an SPDX SBOM inside the package when enabled. |
| `IsTestHarnessProject` | `false` | Applies test-library defaults without creating an executable test host. |
| `EnableCodeCoverage` | `true` on CI, otherwise unset | Adds MTP coverage arguments when `true`. |
| `EnableDefaultTestSettings` | enabled unless `false` | Set `false` to own all MTP command-line defaults. |
| `OptimizeTestRun` | enabled unless `false` | Set `false` to keep analyzers enabled during MTP's test-build phase. |
| `DisableSupportPackageInformation` | `false` | Set `true` to opt out of Headless package metadata and symbol policy. |
| `SearchReadmeFileAbove` | `false` | Searches parent directories for a package README. |
| `DisableReadme` | `false` | Prevents automatic package README discovery and packing. |
| `DisablePackageLogo` | `false` | Prevents automatic `logo.png` discovery and packing. |
| `DisableSupportCopyright` | `false` | Prevents Headless copyright generation. |
| `DisableSupportEmbedBinlog` | `false` | Prevents Headless configuration inputs from being embedded in binlogs. |
| `DisableSupportWebContainer` | `false` | Prevents Web container defaults from being evaluated. |
| `EnableSdkContainerSupport` | `true` for Web projects on GitHub Actions | Enables the Microsoft SDK container targets; an explicit value wins. |
| `ContainerRegistry` | `ghcr.io` for Web projects on GitHub Actions | Overrides the target container registry. |
| `ContainerRepository` | GitHub owner plus kebab-case repository name | Overrides the target container repository. |
| `ContainerImageTagsMainVersionPrefix` | `1.0` | Sets the prefix used for main-branch run-number tags. |
| `ContainerImageTagsIncludeLatest` | `true` | Set `false` to omit `latest` from main-branch image tags. |
| `ContainerImageTags` | `prefix.run-number;latest` on main, otherwise `0.0.1-preview.sha` | Overrides the complete semicolon-separated image tag set. |
| `HeadlessSuppressNonPackablePackWarning` | `true` | Set `false` to retain the Microsoft warning for packing a non-packable project. |
| `HeadlessConfigFilesDir` | solution directory, then project directory | Overrides the explicit scaffold target's destination. |
| `HeadlessCopyDefaultConfigFilesToSolutionDir` | `false` | Selects all scaffold files when the target is explicitly invoked. |
| `HeadlessCopyEditorConfigToSolutionDir` | `false` | Selects only `.editorconfig`. |
| `HeadlessCopyCSharpierIgnoreToSolutionDir` | master selector | Selects only `.csharpierignore`. |
| `HeadlessCopyGitIgnoreToSolutionDir` | master selector | Selects only `.gitignore`. |
| `HeadlessCopyGitAttributesToSolutionDir` | master selector | Selects only `.gitattributes`. |
| `HeadlessOverwriteConfigFiles` | `false` | Allows the explicit scaffold target to replace existing files. |

The explicit target framework, nine analyzer packages, analyzer configuration, CI warning gate,
NuGet audit policy, and SDK-owned MTP extension
versions are mandatory policy. Legacy analyzer/configuration opt-out names do not disable them.

## CI, restore, and vulnerability policy

Headless detects common CI provider environment variables. Consumers can also activate CI behavior with `ContinuousIntegrationBuild=true`; there is no separate `IsContinuousIntegration` input.

On CI, the SDK authoritatively enables:

- compiler, nullable, code-analysis, and MSBuild warnings as errors;
- preview target-framework warnings;
- `NU1901`, `NU1902`, `NU1903`, and `NU1904` as errors.

The Microsoft `NETSDK1138` warning remains visible but non-fatal on CI. End-of-life frameworks do
not receive security fixes, but Headless does not reject them when the selected Microsoft SDK can
still target them.

`NU1900` and its companion `NU1905` remain warnings because they report missing audit data rather
than a confirmed vulnerability. NuGet audit is always enabled with `NuGetAuditMode=all` and
`NuGetAuditLevel=low`, covering direct and transitive dependencies.

Locked restore is enforced on CI only when the project has opted in by committing `packages.lock.json`, or when `NuGetLockFilePath` identifies an existing lock file. Projects without a lock file are restored normally.

## Test SDK contract

`Headless.NET.Sdk.Test` is Microsoft Testing Platform only. It defaults test hosts to `OutputType=Exe`, `IsTestProject=true`, `IsPackable=false`, and `IsPublishable=false`, and supplies restore-visible MTP extensions for crash dumps, hang dumps, hot reload, retry, TRX reporting, and coverage. Default execution includes TRX output, crash and hang dumps, and a minimum expected test count; coverage is enabled on CI.

The test framework remains consumer-selected. For example:

```xml
<Project Sdk="Headless.NET.Sdk.Test/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" Version="3.2.2" />
  </ItemGroup>
</Project>
```

When the command-line host is the .NET 10 SDK, Microsoft Testing Platform also requires the
repository `global.json` to select the MTP runner. Add this top-level entry alongside the existing
`sdk` configuration:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

Without that entry, .NET 10 routes `dotnet test` through VSTest and Microsoft Testing Platform
rejects the invocation before discovering tests. This host requirement does not restrict the test
project's `TargetFramework`.

The Test SDK owns the versions of its six implicit MTP extension references. Central Package Management consumers must not add `PackageVersion` entries for those extension IDs; NuGet rejects central versions for SDK-defined implicit references with NU1009. The consumer's test-framework version remains centrally manageable.

VSTest classification, `Microsoft.NET.Test.Sdk`, and VSTest logger defaults are not part of this contract. Shared test harness libraries can use `IsTestHarnessProject=true` to receive test analysis defaults without becoming executable test hosts.

## Packaging and supply-chain behavior

### SBOM

SBOM generation is a reliable opt-in:

```xml
<PropertyGroup>
  <GenerateSBOM>true</GenerateSBOM>
</PropertyGroup>
```

The package family carries the `Microsoft.Sbom.Targets` dependency so opting in changes generation behavior, not restore availability. The default is `false`.

Headless owns the transitive `Microsoft.Sbom.Targets` version; consumers do not add a direct or central reference for it.

### Symbols

`HeadlessSymbolFormat` controls non-test symbol packaging:

| Value | Behavior |
| --- | --- |
| `embedded` | Default. Uses embedded PDBs and does not create a symbol package. |
| `snupkg` | Produces portable symbols in a `.snupkg`. |
| `none` | Does not package symbols. This is the Blazor WebAssembly default to avoid shipping debug data to browsers. |

Explicit consumer `IncludeSymbols` and `SymbolPackageFormat` values remain authoritative. For portable PDBs, use `HeadlessSymbolFormat=snupkg`; the Microsoft SDK initializes `DebugType=portable` before Headless can distinguish an explicit value from the default.

Headless follows the Microsoft SDK default for `EmbedUntrackedSources` and does not set it.

### Package metadata

Headless sets repository metadata and discovers a project README, icon, license file, and third-party notices when present. It does not inject the Headless author's name into consumer `Authors` or `Company` properties. This repository deliberately ships its own packages without license metadata until a license is chosen.

## Runtime and application behavior

- Non-test applications default to `RollForward=LatestMajor`.
- Non-Web executable projects default to .NET tool packaging.
- Web projects on GitHub Actions receive SDK container defaults for `ghcr.io` and deterministic branch/tag calculation. Set `DisableSupportWebContainer=true` only when the project owns its container policy.
- `HeadlessEnableStrictSystemTextJsonRuntimeDefaults=true` enables the .NET runtime switches for required constructor parameters and nullable annotations on TFMs compatible with `net9.0`. It is off by default because these process-wide switches can break third-party serializers.
- When `System.Runtime.Experimental` is referenced, Headless removes only the conflicting `System.Runtime.dll` facade from `Microsoft.NETCore.App.Ref`. The experimental package carries its own facade; leaving both references can make conflict resolution select the wrong compile-time assembly. This targeted correction is intentionally always active.

## Assembly metadata and repository scaffolding

The SDK emits Headless SDK identity metadata and, for unsigned non-test projects, conventional `InternalsVisibleTo` entries for `.Tests.Architecture`, `.Tests.Unit`, `.Tests.Integration`, and `.Tests.Acceptance`. Set `HeadlessEmitInternalsVisibleToAttributes=false` when the project owns its friend-assembly list.

Repository configuration files are scaffolded only on explicit request and existing files are preserved:

```bash
dotnet build -t:HeadlessScaffoldConfigFiles
```

Use the `HeadlessCopy*` selectors for individual files and `HeadlessOverwriteConfigFiles=true` only when replacement is intended.

## Building and publishing this repository

The repository uses the .NET SDK pinned by `global.json`:

```bash
dotnet restore headless-sdk.slnx
dotnet build headless-sdk.slnx --configuration Release --no-restore -p:GeneratePackageOnBuild=false
dotnet pack headless-sdk.slnx --configuration Release --no-restore --no-build --output ./artifacts/packages-results
HEADLESS_PACKAGES_DIR="$PWD/artifacts/packages-results" \
  dotnet test headless-sdk.slnx --configuration Release --no-restore --no-build
```

The publish workflow promotes the exact packages produced by its build job, verifies SHA-256 hashes before upload, requires Linux, Windows, and macOS validation, and fails on duplicate package versions. Tag and manual runs publish to GitHub Packages. A published GitHub Release builds the same validated package family for NuGet.org, then pauses for approval in the protected `NuGet Release` environment before any push. Neither feed provides an atomic multi-package transaction: if a release stops after publishing only part of the family, abandon that version, fix the cause, and publish a new version. Never retry the same partial version or bypass a publication gate.

## Repository layout

```text
src/
  Headless.NET.Sdk/
  Headless.NET.Sdk.Web/
  Headless.NET.Sdk.Test/
  Headless.NET.Sdk.Razor/
  Headless.NET.Sdk.BlazorWebAssembly/
  Headless.NET.Sdk.WindowsDesktop/
tests/
  Headless.NET.Sdk.Tests.Integrations/
```

## License status

No license has been selected. Copyright is reserved; source availability does not itself grant legal rights to use, modify, or redistribute the repository or its packages.
