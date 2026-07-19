# Headless.NET.Sdk.WindowsDesktop

The WPF and Windows Forms wrapper: `Microsoft.NET.Sdk.WindowsDesktop` plus the complete Headless build baseline.

> [!IMPORTANT]
> This package is distributed through GitHub Packages and, after protected release approval, NuGet.org. It can be consumed by any compatible .NET project; it is not limited to Headless Framework. The repository currently has no license, so source availability does not itself grant legal rights to use, modify, or redistribute it.

## Use

Headless does not restrict `TargetFramework`; `Microsoft.NET.Sdk.WindowsDesktop` and the installed Windows targeting packs determine compatibility.

```xml
<Project Sdk="Headless.NET.Sdk.WindowsDesktop/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk.WindowsDesktop`:

```xml
<PackageReference Include="Headless.NET.Sdk.WindowsDesktop" Version="x.y.z" PrivateAssets="all" />
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.WindowsDesktop@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

For a .NET 10 file app, this wrapper preserves the Headless Windows Desktop identity over the base
Microsoft SDK. A single source file cannot author WPF or Windows Forms project items, and importing
the legacy Windows Desktop wrapper there would only produce NETSDK1137 and NETSDK1106. Normal
projects continue to use `Microsoft.NET.Sdk.WindowsDesktop` with `UseWPF` or `UseWindowsForms`.

## Windows Desktop contract

The package sets `HeadlessSdkProjectType=WindowsDesktop` and defaults `IsPackable=true`. Consumers still select `UseWPF` or `UseWindowsForms` and a compatible Windows TFM. Use `HeadlessEnforceConfigureAwait=true` when a library must surface `CA2007` for synchronization-context correctness.

The package preserves the mandatory Headless analyzer infrastructure, configurable banned-API policy, audit, CI, symbol, SBOM, and direct-opt-in policies. It is self-contained and ships no `buildTransitive` assets.
