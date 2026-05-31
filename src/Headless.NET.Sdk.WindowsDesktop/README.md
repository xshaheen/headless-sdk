# Headless.NET.Sdk.WindowsDesktop

The Windows Desktop project-type wrapper in the Headless family — `Microsoft.NET.Sdk.WindowsDesktop` plus the same opinionated Headless defaults. Use it for WPF and Windows Forms apps.

## Install

As an MSBuild SDK:

```xml
<Project Sdk="Headless.NET.Sdk.WindowsDesktop/x.y.z">
</Project>
```

Or as a package reference alongside the Microsoft Windows Desktop SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.WindowsDesktop" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## What it adds over the core SDK

Sets `HeadlessSdkProjectType=WindowsDesktop` and wraps `Microsoft.NET.Sdk.WindowsDesktop` so WPF and Windows Forms projects compile correctly (you still set `UseWPF` / `UseWindowsForms` as needed), while applying the full Headless baseline. `IsPackable` defaults to `true` for Windows Desktop projects.

## Opinionated defaults (overridable)

Inherits the full strict Headless baseline (banned `Newtonsoft.Json`, `latest-all` analyzers, warnings as errors on CI, and more). Every default is overridable via the `Disable*` and `Headless*` properties. See the [Configuration Reference in the main repo README](https://github.com/xshaheen/headless-sdk#configuration-reference) for the complete list.

## License

See [LICENSE](https://github.com/xshaheen/headless-sdk/blob/main/LICENSE).
