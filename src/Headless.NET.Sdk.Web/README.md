# Headless.NET.Sdk.Web

The Web project-type wrapper in the Headless family — `Microsoft.NET.Sdk.Web` plus the same opinionated Headless defaults. Use it for ASP.NET Core apps and Web APIs.

## Install

As an MSBuild SDK:

```xml
<Project Sdk="Headless.NET.Sdk.Web/x.y.z">
</Project>
```

Or as a package reference alongside the Microsoft Web SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Web" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## What it adds over the core SDK

Sets `HeadlessSdkProjectType=Web` and wraps `Microsoft.NET.Sdk.Web`. On top of the core Headless baseline it enables GitHub Actions container support: when a Web project builds on GitHub Actions, SDK container publishing turns on with `ghcr.io` as the registry, the image repository computed from the GitHub owner and repo name, and computed image tags (`latest` plus a versioned tag on `main`). `IsPackable` defaults to `false` for Web projects. Skip the container defaults with `DisableSupportWebContainer`.

## Opinionated defaults (overridable)

Inherits the full strict Headless baseline (banned `Newtonsoft.Json`, `latest-all` analyzers, warnings as errors on CI, and more). Every default is overridable via the `Disable*` and `Headless*` properties. See the [Configuration Reference in the main repo README](https://github.com/xshaheen/headless-sdk#configuration-reference) for the complete list.

## License

See [LICENSE](https://github.com/xshaheen/headless-sdk/blob/main/LICENSE).
