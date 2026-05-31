# Headless.NET.Sdk.BlazorWebAssembly

The Blazor WebAssembly project-type wrapper in the Headless family — `Microsoft.NET.Sdk.BlazorWebAssembly` plus the same opinionated Headless defaults. Use it for Blazor WebAssembly client apps.

## Install

As an MSBuild SDK:

```xml
<Project Sdk="Headless.NET.Sdk.BlazorWebAssembly/x.y.z">
</Project>
```

Or as a package reference alongside the Microsoft Blazor WebAssembly SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.BlazorWebAssembly" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## What it adds over the core SDK

Sets `HeadlessSdkProjectType=BlazorWebAssembly` and wraps `Microsoft.NET.Sdk.BlazorWebAssembly` so the WASM client app compiles and publishes correctly, while applying the full Headless baseline. `IsPackable` defaults to `false`, since Blazor WebAssembly apps are published, not packed.

## Opinionated defaults (overridable)

Inherits the full strict Headless baseline (banned `Newtonsoft.Json`, `latest-all` analyzers, warnings as errors on CI, and more). Every default is overridable via the `Disable*` and `Headless*` properties. See the [Configuration Reference in the main repo README](https://github.com/xshaheen/headless-sdk#configuration-reference) for the complete list.

## License

See [LICENSE](https://github.com/xshaheen/headless-sdk/blob/main/LICENSE).
