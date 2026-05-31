# Headless.NET.Sdk.Test

The Test project-type wrapper in the Headless family — `Microsoft.NET.Sdk` plus the Headless defaults, with test classification forced on. Use it for test projects.

## Install

As an MSBuild SDK:

```xml
<Project Sdk="Headless.NET.Sdk.Test/x.y.z">
</Project>
```

Or as a package reference alongside the Microsoft SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Headless.NET.Sdk.Test" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## What it adds over the core SDK

Sets `HeadlessSdkProjectType=Test` and forces `IsTestableProject=true` and `IsTestProject=true`, so the project receives the full test toolchain without name-based guessing. (Name inference like `MyApp.Tests` is intentionally not supported — too many false positives for a public SDK. If you don't want the Test SDK, set `<IsTestableProject>true</IsTestableProject>` yourself.) Test projects force `IsPackable=false` and `IsPublishable=false`, add crash/hang dumps, TRX output and loggers, enable code coverage on CI, switch to Microsoft Testing Platform when `xunit.v3.mtp-v2` or `TUnit` is referenced, disable analyzers during `dotnet test`, and relax a few test-noise warnings (`CA1849`, `MA0042`, `MA0166`).

## Opinionated defaults (overridable)

Inherits the full strict Headless baseline. Every default is overridable via the `Disable*` and `Headless*` properties. See the [Configuration Reference in the main repo README](https://github.com/xshaheen/headless-sdk#configuration-reference) for the complete list, including the full Test Projects section.

## License

See [LICENSE](https://github.com/xshaheen/headless-sdk/blob/main/LICENSE).
