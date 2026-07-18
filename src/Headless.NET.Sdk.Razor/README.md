# Headless.NET.Sdk.Razor

The Razor class-library wrapper: `Microsoft.NET.Sdk.Razor` plus the complete Headless build baseline.

> [!IMPORTANT]
> This package is currently distributed through the `xshaheen` GitHub Packages feed and is not published to NuGet.org. It can be consumed by any compatible .NET project; it is not limited to Headless Framework. The repository currently has no license, so source availability does not itself grant legal rights to use, modify, or redistribute it.

## Use

Headless does not restrict `TargetFramework`; `Microsoft.NET.Sdk.Razor` and the installed targeting packs determine compatibility.

```xml
<Project Sdk="Headless.NET.Sdk.Razor/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
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
