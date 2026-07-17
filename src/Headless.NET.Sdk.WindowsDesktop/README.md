# Headless.NET.Sdk.WindowsDesktop

The .NET 10 WPF and Windows Forms wrapper: `Microsoft.NET.Sdk.WindowsDesktop` plus the complete Headless build baseline.

> [!IMPORTANT]
> This is an internal package distributed through the `xshaheen` GitHub Packages feed. It is not published to NuGet.org. The repository currently has no license and grants no external use or redistribution rights.

## Use

```xml
<Project Sdk="Headless.NET.Sdk.WindowsDesktop/x.y.z">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk.WindowsDesktop`:

```xml
<PackageReference Include="Headless.NET.Sdk.WindowsDesktop" Version="x.y.z" PrivateAssets="all" />
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.WindowsDesktop@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Windows Desktop contract

The package sets `HeadlessSdkProjectType=WindowsDesktop` and defaults `IsPackable=true`. Consumers still select `UseWPF` or `UseWindowsForms` and a compatible Windows TFM. Use `HeadlessEnforceConfigureAwait=true` when a library must surface `CA2007` for synchronization-context correctness.

The package preserves the mandatory Headless analyzer, banned-API, audit, CI, symbol, SBOM, and direct-opt-in policies. It is self-contained and ships no `buildTransitive` assets.
