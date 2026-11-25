# Headless.NET.Sdk.Test

The Microsoft Testing Platform wrapper: `Microsoft.NET.Sdk` plus the complete Headless build baseline and test classification. It is available to any compatible .NET project and is not specific to Headless Framework.

## Use

Headless does not restrict `TargetFramework`; the Microsoft SDK, Microsoft Testing Platform, and the consumer-selected test framework determine compatibility.

The SDK supplies the MTP host extensions; the consumer chooses its test framework:

```xml
<Project Sdk="Headless.NET.Sdk.Test/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.1" />
  </ItemGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk` and declares `OutputType`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Test" Version="x.y.z" PrivateAssets="all" />
    <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.1" />
  </ItemGroup>
</Project>
```

NuGet restore does not load package build assets, so in this mode restore never sees the package's
`OutputType=Exe` default. Without the declaration, restore plans a library, skips the apphost pack
for the target framework, and the build fails with MSB3030 when the SDK does not bundle that
apphost (for example, `net8.0` built by the .NET 10 SDK). The SDK forms set the default before
restore and need no declaration.

When `dotnet test` runs under the .NET 10 or .NET 11 SDK, the repository must select Microsoft Testing
Platform in `global.json`. Add this top-level entry alongside the existing `sdk` configuration:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

Without it, these SDKs route `dotnet test` through VSTest and the MTP project rejects the command. The `DOTNET_TEST_RUNNER` environment variable (.NET 11 SDK) overrides the `global.json` entry for one session.
This is a command-host requirement, not a restriction on the test project's `TargetFramework`.

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 or later `#:sdk Headless.NET.Sdk.Test@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Coverage settings

The package owns the coverage denominator policy in
`configurations/default.runsettings`. `HeadlessCoverageSettingsPath` evaluates to the absolute path
of that packaged file. When a project-evaluated test run sets `EnableCodeCoverage=true`, the SDK
adds `--coverage` and exactly one `--coverage-settings` argument using that property.

Microsoft Testing Platform's `--test-modules` mode runs prebuilt assemblies without project
evaluation, so it cannot receive SDK-injected `TestingPlatformCommandLineArguments`. External build
tooling should query the project, capture the single-line result, and pass the value explicitly:

```text
dotnet msbuild tests/MyProject.Tests/MyProject.Tests.csproj -getProperty:HeadlessCoverageSettingsPath -nologo -v:quiet
dotnet test --test-modules 'tests/**/bin/Release/**/*.Tests.dll' --root-directory <repository-root> --minimum-expected-tests 1 --coverage --coverage-settings "<value-from-the-command-above>"
```

Resolve the property from one representative test project after restore. Treat the result as a
fully qualified path and verify that it exists before invoking the module runner; do not copy or
maintain a consumer-owned runsettings file.

On a Linux or macOS build host, a build that leaves `EnableCodeCoverage` unset or `false` keeps the coverage extension but does not
copy its native profilers (`runtimes/*/native`, about 43 MB per test project) into the output or
list them in `*.deps.json`. `--coverage` stays a valid run-time option. With the packaged coverage
settings on Linux and macOS, coverage instruments managed modules statically and does not load those
profilers, so a module runner that passes `--coverage` to such a build collects the same coverage.
Windows build hosts still copy the profilers, because Windows coverage may load them.

## Test contract

- Executable MTP host by default (PackageReference consumers declare `OutputType=Exe`; see above), with `IsTestProject=true`, `IsPackable=false`, and `IsPublishable=false`.
- Microsoft Testing Platform only; VSTest and `Microsoft.NET.Test.Sdk` are not injected.
- Restore-visible crash dump, hang dump, hot reload, retry, TRX, and coverage extensions.
- Default TRX output, crash and hang dumps, and `MinimumExpectedTests=1`. Set it to a positive value to change the MTP guard, or `0` to omit only the SDK-supplied argument; omission does not guarantee that a zero-test run succeeds. `EnableDefaultTestSettings=false` removes all SDK-owned MTP arguments.
- A direct supported xUnit v3 package reference adds `XUNIT_GENERATED_DISABLE_WARNINGS` once while preserving other constants; xUnit v3 4.0.1 and later then suppress warnings in their generated code. Set `EnableXunitEntryPointDisableWarnings=false` to opt out; test-project classification alone does not enable it.
- Coverage enabled on CI, with `HeadlessCoverageSettingsPath` exposing the packaged denominator policy, and analyzer work skipped during the test-build phase unless explicitly retained.
- Mandatory Headless analyzer infrastructure, configurable banned-API policy, and mandatory audit and CI policies with narrow test-code severity relaxations.

The package is self-contained and ships no `buildTransitive` assets.

A shared harness library, which test projects reference but which holds no tests of its own, sets `IsTestHarnessProject=true` with this SDK or the base SDK. It receives the test analysis defaults and builds as a library: the SDK sets `OutputType=Library`, `IsTestProject=false`, and `IsTestingPlatformApplication=false`, overriding an explicit value, so a solution-wide `dotnet test` does not try to run it. Reference `xunit.v3.extensibility.core` from a harness, not `xunit.v3` or an `xunit.v3.mtp-*` runner package, because xUnit's runner packages fail any build that is not an executable.

The SDK owns the versions of its six implicit MTP extensions. Central Package Management consumers must not declare `PackageVersion` entries for those extension IDs; test-framework versions remain consumer-owned and centrally manageable.
