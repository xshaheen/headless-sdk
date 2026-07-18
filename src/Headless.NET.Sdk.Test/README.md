# Headless.NET.Sdk.Test

The .NET 10 Microsoft Testing Platform wrapper: `Microsoft.NET.Sdk` plus the complete Headless build baseline and test classification.

> [!IMPORTANT]
> This package is currently distributed through the `xshaheen` GitHub Packages feed and is not published to NuGet.org. It can be consumed by any compatible .NET project; it is not limited to Headless Framework. The repository currently has no license, so source availability does not itself grant legal rights to use, modify, or redistribute it.

## Use

The SDK supplies the MTP host extensions; the consumer chooses its test framework:

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

Direct PackageReference consumption uses `Microsoft.NET.Sdk`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Test" Version="x.y.z" PrivateAssets="all" />
    <PackageReference Include="xunit.v3.mtp-v2" Version="3.2.2" />
  </ItemGroup>
</Project>
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.Test@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Test contract

- Executable MTP host by default, with `IsTestProject=true`, `IsPackable=false`, and `IsPublishable=false`.
- Microsoft Testing Platform only; VSTest and `Microsoft.NET.Test.Sdk` are not injected.
- Restore-visible crash dump, hang dump, hot reload, retry, TRX, and coverage extensions.
- Default TRX output, crash and hang dumps, and a minimum expected test count.
- Coverage enabled on CI and analyzer work skipped during the test-build phase unless explicitly retained.
- Mandatory Headless analyzer, banned-API, audit, and CI policies with narrow test-code severity relaxations.

The package is self-contained and ships no `buildTransitive` assets. Shared harness libraries can instead use `IsTestHarnessProject=true` with the base SDK to receive test analysis defaults without becoming executable test hosts.

The SDK owns the versions of its six implicit MTP extensions. Central Package Management consumers must not declare `PackageVersion` entries for those extension IDs; test-framework versions remain consumer-owned and centrally manageable.
