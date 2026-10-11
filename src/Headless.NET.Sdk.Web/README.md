# Headless.NET.Sdk.Web

The ASP.NET Core and Web API wrapper: `Microsoft.NET.Sdk.Web` plus the complete Headless build baseline. It is available to any compatible .NET project and is not specific to Headless Framework.

## Use

Headless does not restrict `TargetFramework`; `Microsoft.NET.Sdk.Web` and the installed targeting packs determine compatibility.

As a project SDK:

```xml
<Project Sdk="Headless.NET.Sdk.Web/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Or opt a `Microsoft.NET.Sdk.Web` project in directly:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Web" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Additional-SDK, `global.json` MSBuild SDK resolution, and .NET 10 `#:sdk Headless.NET.Sdk.Web@x.y.z` consumption are also supported. See the [family consumption reference](https://github.com/xshaheen/headless-sdk#consumption-modes).

## Web contract

The package sets `HeadlessSdkProjectType=Web`, defaults `IsPackable=false`, and preserves the mandatory Headless analyzer infrastructure, configurable banned-API policy, audit, CI, symbol, and direct-opt-in policies.

On GitHub Actions, Web projects default to SDK container publishing through `ghcr.io`. The image repository is `<owner>/<repository>/<assembly>` in kebab case, so several Web projects in one repository publish separate images. On `main`, a project with its own `$(Version)` is tagged with that version and `latest`; without one, it gets the configured version prefix plus the run number. Other refs receive preview tags. Set `DisableSupportWebContainer=true` only when the project owns its container policy.

Web projects keep the .NET default `RollForward`, unlike other Headless executables, so a server never silently starts on an untested major runtime.

The package is self-contained and does not rely on a separate `Headless.NET.Sdk` package. It ships no `buildTransitive` assets.
