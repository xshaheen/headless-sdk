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
    <PackageReference Include="xunit.v3.mtp-v2" Version="3.2.2" />
  </ItemGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Test" Version="x.y.z" PrivateAssets="all" />
    <PackageReference Include="xunit.v3.mtp-v2" Version="3.2.2" />
  </ItemGroup>
</Project>
```

When `dotnet test` runs under the .NET 10 SDK, the repository must select Microsoft Testing
Platform in `global.json`. Add this top-level entry alongside the existing `sdk` configuration:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

Without it, .NET 10 routes `dotnet test` through VSTest and the MTP project rejects the command.
This is a command-host requirement, not a restriction on the test project's `TargetFramework`.

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.Test@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Test contract

- Executable MTP host by default, with `IsTestProject=true`, `IsPackable=false`, and `IsPublishable=false`.
- Microsoft Testing Platform only; VSTest and `Microsoft.NET.Test.Sdk` are not injected.
- Restore-visible crash dump, hang dump, hot reload, retry, TRX, and coverage extensions.
- Default TRX output, crash and hang dumps, and a minimum expected test count.
- Coverage enabled on CI and analyzer work skipped during the test-build phase unless explicitly retained.
- Mandatory Headless analyzer infrastructure, configurable banned-API policy, and mandatory audit and CI policies with narrow test-code severity relaxations.

The package is self-contained and ships no `buildTransitive` assets. Shared harness libraries can instead use `IsTestHarnessProject=true` with the base SDK to receive test analysis defaults without becoming executable test hosts.

The SDK owns the versions of its six implicit MTP extensions. Central Package Management consumers must not declare `PackageVersion` entries for those extension IDs; test-framework versions remain consumer-owned and centrally manageable.
