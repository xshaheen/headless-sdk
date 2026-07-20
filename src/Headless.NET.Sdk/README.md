# Headless.NET.Sdk

The base Headless MSBuild SDK for libraries, console applications, and shared build policy. It wraps `Microsoft.NET.Sdk`; every satellite package carries this same baseline. It is available to any compatible .NET project and is not specific to Headless Framework.

## Use

All five family consumption modes are supported. Every MSBuild project must set `TargetFramework` explicitly, but Headless does not restrict its value; the selected Microsoft SDK and targeting packs determine compatibility.

```xml
<!-- Project SDK -->
<Project Sdk="Headless.NET.Sdk/x.y.z">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

```xml
<!-- Direct PackageReference -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

```xml
<!-- Additional SDK -->
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Headless.NET.Sdk" Version="x.y.z" />
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

The version can instead be resolved from `global.json`. .NET 10 file-based apps use:

```csharp
#:sdk Headless.NET.Sdk@x.y.z
```

See the repository [consumption-mode reference](https://github.com/xshaheen/headless-sdk#consumption-modes) for feed authentication and `global.json` configuration.

## Contract

- Direct opt-in only: no `buildTransitive` assets are shipped.
- Explicit target frameworks only; Headless does not infer a TFM.
- Mandatory nine-package analyzer baseline plus default-on, consumer-configurable general and Newtonsoft.Json banned APIs.
- CI-only compiler, analyzer, nullable, MSBuild, and vulnerability warning escalation.
- CI locked restore only when an existing lock file opts the project in.
- Direct and transitive NuGet audit; `NU1901`-`NU1904` fail CI while `NU1900` and `NU1905` remain warnings.
- Extra Headless global usings only when `ImplicitUsings` is enabled and the TFM is compatible with `net8.0`.
- Automatic analyzer profile for .NET 10 file-based apps.
- Opt-in SBOM generation with `GenerateSBOM=true`.
- Embedded symbols by default through `HeadlessSymbolFormat=embedded`.
- Targeted removal of the conflicting reference-pack `System.Runtime` facade when `System.Runtime.Experimental` supplies its own facade.

Named quality policies are authoritative. Other supported customization properties are documented in the [main README](https://github.com/xshaheen/headless-sdk).

Direct PackageReference consumers must also follow the main README's first-clean-restore bootstrap.
NuGet cannot load package `build` assets early enough for the package itself to govern that initial
restore; this limitation does not affect build, pack, analyzer, or later restore behavior.
