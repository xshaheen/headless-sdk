# Headless.NET.Sdk

`Headless.NET.Sdk` is an opinionated MSBuild SDK I built for my own .NET projects and the teams I work with. The repo is public and you're welcome to consume it, but the defaults reflect a strict house style — `Newtonsoft.Json` banned, `latest-all` analyzer level, MSBuild warnings as errors on CI, `RollForward=LatestMajor` for executables, implicit analyzer hygiene. If any of that doesn't fit your project, every default is overridable via the `Disable*` and `Headless*` properties documented in the [Configuration Reference](src/Headless.NET.Sdk/README.md#configuration-reference).

The intent is simple: every project starts with the same strict baseline, then opts out only where the local project has a clear reason.

## Package Family

| Package | Wraps | Use for |
| --- | --- | --- |
| [`Headless.NET.Sdk`](https://www.nuget.org/packages/Headless.NET.Sdk) | `Microsoft.NET.Sdk` | Libraries and console apps — the base SDK every other variant builds on. |
| [`Headless.NET.Sdk.Web`](https://www.nuget.org/packages/Headless.NET.Sdk.Web) | `Microsoft.NET.Sdk.Web` | ASP.NET Core / Web APIs, with GitHub-Actions container support. |
| [`Headless.NET.Sdk.Test`](https://www.nuget.org/packages/Headless.NET.Sdk.Test) | `Microsoft.NET.Sdk` | Test projects — forces `IsTestableProject` and `IsTestProject`. |
| [`Headless.NET.Sdk.Razor`](https://www.nuget.org/packages/Headless.NET.Sdk.Razor) | `Microsoft.NET.Sdk.Razor` | Razor class libraries. |
| [`Headless.NET.Sdk.BlazorWebAssembly`](https://www.nuget.org/packages/Headless.NET.Sdk.BlazorWebAssembly) | `Microsoft.NET.Sdk.BlazorWebAssembly` | Blazor WebAssembly client apps. |
| [`Headless.NET.Sdk.WindowsDesktop`](https://www.nuget.org/packages/Headless.NET.Sdk.WindowsDesktop) | `Microsoft.NET.Sdk.WindowsDesktop` | WPF and Windows Forms apps. |

## Quick Start

As an MSBuild SDK:

```xml
<Project Sdk="Headless.NET.Sdk/x.y.z">
</Project>
```

Or as a `PackageReference`:

```xml
<PackageReference Include="Headless.NET.Sdk" Version="x.y.z" PrivateAssets="all" />
```

All consumption modes (MSBuild SDK, `<Sdk Name=...>`, `#:sdk` for .NET 10 file-based apps), every configurable property, and every `Disable*` switch are documented in the **[package README](src/Headless.NET.Sdk/README.md)** — that's the same file shipped on each nuget.org package page.

## Repository Layout

```
src/
  Headless.NET.Sdk/                      # base SDK + canonical README packed with all packages
  Headless.NET.Sdk.Web/                  # project-type wrapper for Microsoft.NET.Sdk.Web
  Headless.NET.Sdk.Test/                 # ... for test projects
  Headless.NET.Sdk.Razor/                # ... for Razor class libraries
  Headless.NET.Sdk.BlazorWebAssembly/    # ... for Blazor WebAssembly
  Headless.NET.Sdk.WindowsDesktop/       # ... for WPF / WinForms
tests/
  Headless.NET.Sdk.Tests.Integrations/   # build + pack + restore integration tests
```

## Build And Publish

For contributors releasing the SDK itself (consumers do not need to run these):

```bash
dotnet build headless-sdk.slnx
dotnet pack --configuration Release --output ./artifacts/packages-results
dotnet nuget push ./artifacts/packages-results/*.nupkg \
  --source https://nuget.pkg.github.com/xshaheen/index.json \
  --skip-duplicate \
  --api-key "$NUGET_API_KEY"
```

## License

See [LICENSE](LICENSE).
