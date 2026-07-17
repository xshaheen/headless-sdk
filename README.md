# Headless.NET.Sdk

`Headless.NET.Sdk` is the internal MSBuild SDK baseline for Headless Framework repositories. It is consumer-facing build infrastructure: the package family standardizes evaluation order, restore policy, analyzers, packaging, and test execution across .NET projects.

> [!IMPORTANT]
> The packages are distributed only through the `xshaheen` GitHub Packages feed. They are not published to NuGet.org and are not offered as a public package contract. This repository currently has no license; no external right to use, modify, or redistribute its contents is granted.

## Support contract

- .NET 10 is the only supported SDK and target framework.
- Every MSBuild project must declare its target framework explicitly. Headless does not infer one.
- All five consumption modes below are first-class. Build, pack, analyzer, and application policy
  is identical; the documented first-clean-restore bootstrap is required for PackageReference mode.
- Package assets apply only to the project that opts in. The packages do not ship `buildTransitive` assets.
- Multi-targeting outer builds remain supported through `buildMultiTargeting`; inner builds receive the normal `build` contract exactly once.
- Named quality policies are authoritative. In particular, the analyzer/banned-API baseline and CI quality gates are not consumer opt-outs.

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

Authenticate to GitHub Packages with an account or token that can read the package, then add the account feed to the consumer's `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="github" value="https://nuget.pkg.github.com/xshaheen/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <auditSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </auditSources>
</configuration>
```

Do not commit credentials to the repository. Supply them through the supported NuGet credential provider or the CI secret store.

## Consumption modes

Replace `x.y.z` and the package name with the required family member.

### 1. PackageReference

Use the matching Microsoft SDK and add Headless directly:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
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
    <TargetFramework>net10.0</TargetFramework>
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
    <TargetFramework>net10.0</TargetFramework>
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
    <TargetFramework>net10.0</TargetFramework>
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

The bundled general and Newtonsoft.Json banned-symbol lists are also mandatory. This is a deliberate house policy, not a convenience default. Rule severities and narrow project-type relaxations remain defined by the shipped analyzer configurations.

Headless adds its extra global usings only when `ImplicitUsings` is `enable` or `true`. Setting `ImplicitUsings=disable` prevents both the Microsoft implicit-usings feature and the Headless additions.

## CI, restore, and vulnerability policy

Headless detects common CI provider environment variables. Consumers can also activate CI behavior with `ContinuousIntegrationBuild=true`; there is no separate `IsContinuousIntegration` input.

On CI, the SDK authoritatively enables:

- compiler, nullable, code-analysis, and MSBuild warnings as errors;
- preview and end-of-life target-framework warnings;
- `NU1901`, `NU1902`, `NU1903`, and `NU1904` as errors.

`NU1900` and its companion `NU1905` remain warnings because they report missing audit data rather
than a confirmed vulnerability. NuGet audit is always enabled with `NuGetAuditMode=all` and
`NuGetAuditLevel=low`, covering direct and transitive dependencies.

Locked restore is enforced on CI only when the project has opted in by committing `packages.lock.json`, or when `NuGetLockFilePath` identifies an existing lock file. Projects without a lock file are restored normally.

## Test SDK contract

`Headless.NET.Sdk.Test` is Microsoft Testing Platform only. It defaults test hosts to `OutputType=Exe`, forces `IsTestProject=true`, `IsPackable=false`, and `IsPublishable=false`, and supplies restore-visible MTP extensions for crash dumps, hang dumps, hot reload, retry, TRX reporting, and coverage. Default execution includes TRX output, crash and hang dumps, and a minimum expected test count; coverage is enabled on CI.

The test framework remains consumer-selected. For example:

```xml
<Project Sdk="Headless.NET.Sdk.Test/x.y.z">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" Version="3.2.2" />
  </ItemGroup>
</Project>
```

The Test SDK owns the versions of its six implicit MTP extension references. Central Package Management consumers must not add `PackageVersion` entries for those extension IDs; NuGet rejects central versions for SDK-defined implicit references. The consumer's test-framework version remains centrally manageable.

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
- `HeadlessEnableStrictSystemTextJsonRuntimeDefaults=true` enables the .NET runtime switches for required constructor parameters and nullable annotations. It is off by default because these process-wide switches can break third-party serializers.
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

The publish workflow promotes the exact packages produced by its build job, verifies SHA-256 hashes before upload, and fails on duplicate package versions. Publishing targets GitHub Packages only.

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

No license has been selected. Copyright is reserved; do not treat source availability as permission to use, modify, or redistribute the repository or its packages outside the authorized internal environment.
