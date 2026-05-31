# Headless.NET.Sdk

The base SDK of the Headless family — an opinionated MSBuild SDK I built for my own .NET projects and the teams I work with. Use it for libraries and console apps; every other variant (Web, Test, Razor, BlazorWebAssembly, WindowsDesktop) wraps a matching Microsoft SDK and layers these same defaults on top. It wraps `Microsoft.NET.Sdk`.

## Install

As an MSBuild SDK (defaults visible before your `Directory.Build.props`):

```xml
<Project Sdk="Headless.NET.Sdk/x.y.z">
</Project>
```

Or as a normal package reference:

```bash
dotnet add package Headless.NET.Sdk --version x.y.z
```

```xml
<PackageReference Include="Headless.NET.Sdk" Version="x.y.z" PrivateAssets="all" />
```

Or layered on top of the .NET SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Headless.NET.Sdk" Version="x.y.z" />
</Project>
```

.NET 10+ file-based apps can use the SDK directive:

```csharp
#:sdk Headless.NET.Sdk@x.y.z
Console.WriteLine("Hello!");
```

## What it adds

This is the package that carries all the build assets the rest of the family re-packs. Sets `HeadlessSdkProjectType=Default`. The defaults reflect a strict house style: `Newtonsoft.Json` banned, `AnalysisLevel=latest-all`, MSBuild warnings as errors on CI, `RollForward=LatestMajor` for executables, nullable + implicit usings + latest C#, NuGet audit, SBOM on CI, Source Link, symbol packages, and a stack of implicit analyzers (Meziantou, AsyncFixer, Roslynator, and more).

## Opinionated defaults (overridable)

The intent is simple: every project starts with the same strict baseline, then opts out only where the local project has a clear reason. Every default is overridable via the `Disable*` and `Headless*` properties. See the full [Configuration Reference in the main repo README](https://github.com/xshaheen/headless-sdk#configuration-reference) for the complete list.

## License

See [LICENSE](https://github.com/xshaheen/headless-sdk/blob/main/LICENSE).
