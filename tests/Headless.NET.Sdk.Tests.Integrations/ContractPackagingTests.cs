using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed class ContractPackagingTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_generate_an_spdx_sbom_inside_a_clean_consumer_package(bool usePackageReference)
    {
        // useHostFallbackFolder: false - this test's contract is that the SBOM tooling restores
        // into the clean consumer's own packages folder. A fallback-satisfied package is never
        // copied locally, and under PackageReference consumption restore evaluation cannot see
        // the package's build assets (ExcludeRestorePackageImports), so the restore-resolved
        // path property is unavailable there by design.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: usePackageReference,
            useHostFallbackFolder: false
        );
        var packageOutput = Path.Combine(project.RootDirectory, "sbom-packages");
        Directory.CreateDirectory(packageOutput);

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -p:GenerateSBOM=true -p:PackageVersion=1.0.0 -o {Quote(packageOutput)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        var packagePath = Assert.Single(
            Directory.EnumerateFiles(packageOutput, "*.nupkg", SearchOption.TopDirectoryOnly)
        );
        using var package = ZipFile.OpenRead(packagePath);
        Assert.Contains(package.Entries, entry =>
            string.Equals(entry.FullName, "_manifest/spdx_2.2/manifest.spdx.json", StringComparison.Ordinal)
        );
    }

    private const string SiblingVersion = "2.3.4";

    // A packs a ProjectReference to its sibling B. With the opt-in, the packed dependency on B is the
    // exact range of B's own version in every target framework group; without it, NuGet's default
    // ">= x.y.z" lower bound is kept. The multi-targeting rows pack from the outer build, which
    // imports Headless through buildMultiTargeting, so both pack entry points are covered.
    [Theory]
    [InlineData("project-sdk", "true", "[2.3.4]")]
    [InlineData("project-sdk", null, "2.3.4")]
    [InlineData("project-sdk", "false", "2.3.4")]
    [InlineData("project-sdk-multi-targeting", "true", "[2.3.4]")]
    [InlineData("project-sdk-multi-targeting", null, "2.3.4")]
    [InlineData("additional-sdk", "true", "[2.3.4]")]
    [InlineData("additional-sdk", null, "2.3.4")]
    [InlineData("global-json-sdk", "true", "[2.3.4]")]
    [InlineData("global-json-sdk", null, "2.3.4")]
    [InlineData("package-reference", "true", "[2.3.4]")]
    [InlineData("package-reference", null, "2.3.4")]
    [InlineData("package-reference", "false", "2.3.4")]
    [InlineData("package-reference-multi-targeting", "true", "[2.3.4]")]
    [InlineData("package-reference-multi-targeting", null, "2.3.4")]
    [InlineData("razor-project-sdk", "true", "[2.3.4]")]
    public async Task should_pack_the_sibling_dependency_range_when_pin_project_reference_versions_is_set(
        string consumptionMode,
        string? pinProjectReferenceVersions,
        string expectedRange
    )
    {
        var multiTargeting = consumptionMode.EndsWith("-multi-targeting", StringComparison.Ordinal);
        var targetFrameworks = multiTargeting
            ? "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>"
            : "<TargetFramework>net10.0</TargetFramework>";
        var (projectSdk, additionalSdk, sdkReference) = consumptionMode switch
        {
            "project-sdk" or "project-sdk-multi-targeting" => (
                $"Headless.NET.Sdk/{fixture.PackageVersion}",
                string.Empty,
                string.Empty
            ),
            // The targets ship in the shared build folder; one satellite row shows a satellite picks them up.
            "razor-project-sdk" => ($"Headless.NET.Sdk.Razor/{fixture.PackageVersion}", string.Empty, string.Empty),
            "global-json-sdk" => ("Headless.NET.Sdk", string.Empty, string.Empty),
            "additional-sdk" => (
                "Microsoft.NET.Sdk",
                $"""<Sdk Name="Headless.NET.Sdk" Version="{fixture.PackageVersion}" />""",
                string.Empty
            ),
            "package-reference" or "package-reference-multi-targeting" => (
                "Microsoft.NET.Sdk",
                string.Empty,
                $"""<PackageReference Include="Headless.NET.Sdk" Version="{fixture.PackageVersion}" PrivateAssets="all" />"""
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(consumptionMode), consumptionMode, null),
        };
        var pinProperty = pinProjectReferenceVersions is null
            ? string.Empty
            : $"<HeadlessPinProjectReferenceVersions>{pinProjectReferenceVersions}</HeadlessPinProjectReferenceVersions>";

        var additionalFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["B/B.csproj"] = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <Version>{{SiblingVersion}}</Version>
                  </PropertyGroup>
                </Project>
                """,
            ["B/ClassB.cs"] = "namespace B; public sealed class ClassB;",
        };

        if (consumptionMode.StartsWith("package-reference", StringComparison.Ordinal))
        {
            additionalFiles["Directory.Build.props"] = RestoreBootstrapDirectoryBuildProps;
        }

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            includePackageReference: false,
            additionalFiles: additionalFiles
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="{{projectSdk}}">
              {{additionalSdk}}
              <PropertyGroup>
                {{targetFrameworks}}
                <Version>1.2.3</Version>
                {{pinProperty}}
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="B/**" />
                {{sdkReference}}
                <ProjectReference Include="B/B.csproj" />
              </ItemGroup>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        if (string.Equals(consumptionMode, "global-json-sdk", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(
                Path.Combine(project.RootDirectory, "global.json"),
                $$"""
                {
                  {{TestRepository.ConsumerSdkPinJson}}"msbuild-sdks": {
                    "Headless.NET.Sdk": "{{fixture.PackageVersion}}"
                  }
                }
                """,
                TestContext.Current.CancellationToken
            );
        }

        var packageOutput = Path.Combine(project.RootDirectory, "pin-packages");
        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -o {Quote(packageOutput)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} /bl:{Quote(project.BinLogPath)}"
        );
        await project.AttachEvidenceWhenFailedAsync(result, consumptionMode);

        Assert.True(result.ExitCode == 0, result.Output);
        var groups = ReadDependencyGroups(Path.Combine(packageOutput, "ConsumerProject.1.2.3.nupkg"));
        string[] expectedFrameworks = multiTargeting ? ["net10.0", "net8.0"] : ["net10.0"];
        Assert.Equal(expectedFrameworks, groups.Keys.Order(StringComparer.Ordinal));
        Assert.All(groups, group =>
            Assert.Equal(expectedRange, Assert.Contains("B", (IReadOnlyDictionary<string, string>)group.Value))
        );
    }

    private static Dictionary<string, Dictionary<string, string>> ReadDependencyGroups(string packagePath)
    {
        using var package = ZipFile.OpenRead(packagePath);
        var nuspec = package.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspec.Open();
        return XDocument
            .Load(stream)
            .Descendants()
            .Where(element => element.Name.LocalName == "group")
            .ToDictionary(
                group =>
                    group.Attribute("targetFramework")?.Value
                    ?? throw new InvalidOperationException("Dependency group does not declare a target framework."),
                group =>
                    group
                        .Elements()
                        .Where(element => element.Name.LocalName == "dependency")
                        .ToDictionary(
                            element => element.Attribute("id")!.Value,
                            element => element.Attribute("version")!.Value,
                            StringComparer.Ordinal
                        ),
                StringComparer.Ordinal
            );
    }
}
