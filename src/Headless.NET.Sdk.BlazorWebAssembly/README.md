# Headless.NET.Sdk.BlazorWebAssembly

The .NET 10 Blazor WebAssembly wrapper: `Microsoft.NET.Sdk.BlazorWebAssembly` plus the complete Headless build baseline.

> [!IMPORTANT]
> This is an internal package distributed through the `xshaheen` GitHub Packages feed. It is not published to NuGet.org. The repository currently has no license and grants no external use or redistribution rights.

## Use

```xml
<Project Sdk="Headless.NET.Sdk.BlazorWebAssembly/x.y.z">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk.BlazorWebAssembly`:

```xml
<PackageReference Include="Headless.NET.Sdk.BlazorWebAssembly" Version="x.y.z" PrivateAssets="all" />
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.BlazorWebAssembly@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Blazor WebAssembly contract

The package sets `HeadlessSdkProjectType=BlazorWebAssembly` and defaults `IsPackable=false`. `HeadlessSymbolFormat` defaults to `none`: embedded PDBs would otherwise survive into the browser `_framework` payload, increasing download size and exposing debug data.

The package preserves the mandatory Headless analyzer, banned-API, audit, CI, SBOM, and direct-opt-in policies. It is self-contained and ships no `buildTransitive` assets.
