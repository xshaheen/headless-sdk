# Headless.NET.Sdk.Razor

The .NET 10 Razor class-library wrapper: `Microsoft.NET.Sdk.Razor` plus the complete Headless build baseline.

> [!IMPORTANT]
> This is an internal package distributed through the `xshaheen` GitHub Packages feed. It is not published to NuGet.org. The repository currently has no license and grants no external use or redistribution rights.

## Use

```xml
<Project Sdk="Headless.NET.Sdk.Razor/x.y.z">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk.Razor`:

```xml
<PackageReference Include="Headless.NET.Sdk.Razor" Version="x.y.z" PrivateAssets="all" />
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.Razor@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Razor contract

The package sets `HeadlessSdkProjectType=Razor`, defaults `IsPackable=true`, and preserves the mandatory Headless analyzer, banned-API, audit, CI, symbol, SBOM, and direct-opt-in policies. It is self-contained and ships no `buildTransitive` assets.
