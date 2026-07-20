# Headless.NET.Sdk.BlazorWebAssembly

The Blazor WebAssembly wrapper: `Microsoft.NET.Sdk.BlazorWebAssembly` plus the complete Headless build baseline. It is available to any compatible .NET project and is not specific to Headless Framework.

## Use

Headless does not restrict `TargetFramework`; `Microsoft.NET.Sdk.BlazorWebAssembly` and the installed targeting packs or workloads determine compatibility.

```xml
<Project Sdk="Headless.NET.Sdk.BlazorWebAssembly/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Direct PackageReference consumption uses `Microsoft.NET.Sdk.BlazorWebAssembly`:

```xml
<PackageReference Include="Headless.NET.Sdk.BlazorWebAssembly" Version="x.y.z" PrivateAssets="all" />
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.BlazorWebAssembly@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

For a .NET 10 file app, this wrapper preserves the Headless Blazor identity over the base Microsoft
SDK. A single source file cannot author Blazor project items, and the specialized SDK assumes browser
workload references and Native AOT behavior that the generated file project cannot satisfy. Normal
Blazor WebAssembly projects retain their specialized Microsoft SDK behavior.

## Blazor WebAssembly contract

The package sets `HeadlessSdkProjectType=BlazorWebAssembly` and defaults `IsPackable=false`. `HeadlessSymbolFormat` defaults to `none`: embedded PDBs would otherwise survive into the browser `_framework` payload, increasing download size and exposing debug data.

The package preserves the mandatory Headless analyzer infrastructure, configurable banned-API policy, audit, CI, SBOM, and direct-opt-in policies. It is self-contained and ships no `buildTransitive` assets.
