# Headless.NET.Sdk.Razor

The Razor project-type wrapper in the Headless family — `Microsoft.NET.Sdk.Razor` plus the same opinionated Headless defaults. Use it for Razor class libraries.

## Install

As an MSBuild SDK:

```xml
<Project Sdk="Headless.NET.Sdk.Razor/x.y.z">
</Project>
```

Or as a package reference alongside the Microsoft Razor SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Razor" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## What it adds over the core SDK

Sets `HeadlessSdkProjectType=Razor` and wraps `Microsoft.NET.Sdk.Razor` so Razor class libraries compile correctly, while applying the full Headless baseline. `IsPackable` defaults to `true` for Razor projects, since Razor class libraries are usually shipped as packages.

## Opinionated defaults (overridable)

Inherits the full strict Headless baseline (banned `Newtonsoft.Json`, `latest-all` analyzers, warnings as errors on CI, and more). Every default is overridable via the `Disable*` and `Headless*` properties. See the [Configuration Reference in the main repo README](https://github.com/xshaheen/headless-sdk#configuration-reference) for the complete list.

## License

See [LICENSE](https://github.com/xshaheen/headless-sdk/blob/main/LICENSE).
