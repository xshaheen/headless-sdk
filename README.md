# Headless.Defaults

This is the `Headless.Defaults` package, which provides the standard configurations and settings for the Framework projects. Such as `.editorconfig`, build props and targets.
All of our projects are built on top of this package, so we can have a consistent build process and settings across all of our projects.

## Installation

To install this package, you can use the following command:

```bash
dotnet add package Headless.Defaults
```

or add the following package reference to your project file:

```xml
<PackageReference Include="Headless.Defaults" Version="x.x.x" PrivateAssets="all"/>
```

## Publish

```bash
dotnet pack --configuration Release --output ./artifacts/packages-results
cd ./artifacts/packages-results
dotnet nuget push ./*.nupkg --source https://nuget.pkg.github.com/xshaheen/index.json --skip-duplicate --api-key ghp_KEY
```