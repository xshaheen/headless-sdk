using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed class ContractConsumptionTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    [Fact]
    public async Task should_import_quality_assets_once_when_sdk_and_package_reference_are_both_used()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: true
        );

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(restore.ExitCode == 0, restore.Output);
        Assert.DoesNotContain("NU1504", restore.Output, StringComparison.Ordinal);

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var analyzerPackage in HeadlessSdkPackageFixture.MandatoryAnalyzerPackageIds)
        {
            Assert.Equal(
                1,
                packageReferences.Count(id => string.Equals(id, analyzerPackage, StringComparison.Ordinal))
            );
        }

        var editorConfigs = properties["EditorConfigFiles"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            1,
            editorConfigs.Count(path =>
                path.EndsWith("Headless.NET.Sdk.Analyzers.editorconfig", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    [Fact]
    public async Task should_support_additional_sdk_consumption_with_a_satellite_package()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net8.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <Sdk Name="Headless.NET.Sdk.Razor" Version="{{fixture.PackageVersion}}" />
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="AssertHeadlessContract" BeforeTargets="Build">
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk.Razor'"
                  Text="Expected the Headless Razor SDK identity, got '$(HeadlessSdkName)'."
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
        await AssertMandatoryAnalyzersInAssetsAsync(project);

        var preprocessedProjectPath = Path.Combine(project.RootDirectory, "preprocessed.xml");
        var preprocess = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} /pp:{Quote(preprocessedProjectPath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -nologo"
        );
        Assert.True(preprocess.ExitCode == 0, preprocess.Output);

        var preprocessedProject = await File.ReadAllTextAsync(
            preprocessedProjectPath,
            TestContext.Current.CancellationToken
        );
        const string HeadlessTargetMarker = "<_HeadlessSdkBuildTargetsImported>true</_HeadlessSdkBuildTargetsImported>";
        const string MicrosoftTargetMarker = "<EnableDynamicLoading Condition=";
        var headlessTargetIndex = preprocessedProject.IndexOf(HeadlessTargetMarker, StringComparison.Ordinal);
        var microsoftTargetIndex = preprocessedProject.IndexOf(MicrosoftTargetMarker, StringComparison.Ordinal);

        Assert.True(headlessTargetIndex >= 0, "The preprocessed project did not contain the Headless targets.");
        Assert.True(microsoftTargetIndex >= 0, "The preprocessed project did not contain Microsoft.NET.Sdk.targets.");
        Assert.True(
            headlessTargetIndex < microsoftTargetIndex,
            "Headless targets must evaluate before Microsoft.NET.Sdk.targets in additional-SDK mode."
        );
        Assert.Equal(
            -1,
            preprocessedProject.IndexOf(
                HeadlessTargetMarker,
                headlessTargetIndex + HeadlessTargetMarker.Length,
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public async Task should_resolve_a_versionless_sdk_from_global_json()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net9.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            """
            <Project Sdk="Headless.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="AssertHeadlessContract" BeforeTargets="Build">
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'"
                  Text="Expected the Headless SDK identity, got '$(HeadlessSdkName)'."
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );

        var sdkVersion = TestRepository.ConsumerSdkVersion;
        await File.WriteAllTextAsync(
            Path.Combine(project.RootDirectory, "global.json"),
            $$"""
            {
              "sdk": {
                "version": "{{sdkVersion}}",
                "rollForward": "latestFeature",
                "allowPrerelease": false
              },
              "msbuild-sdks": {
                "Headless.NET.Sdk": "{{fixture.PackageVersion}}"
              }
            }
            """,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
        await AssertMandatoryAnalyzersInAssetsAsync(project);
    }

    [Fact]
    public async Task should_not_leak_headless_build_assets_to_a_referencing_project()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["A/A.csproj"] = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Headless.NET.Sdk" Version="{{fixture.PackageVersion}}" />
                  </ItemGroup>
                </Project>
                """,
                ["A/ClassA.cs"] = "namespace A; public sealed class ClassA;",
            }
        );
        await WriteProjectAsync(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="A/A.csproj" />
              </ItemGroup>
              <Target Name="AssertNoTransitiveHeadlessBuildAssets" BeforeTargets="Build">
                <PropertyGroup>
                  <_ReferencingProjectEditorConfigFiles>@(EditorConfigFiles, '|')</_ReferencingProjectEditorConfigFiles>
                </PropertyGroup>
                <Error Condition="'$(HeadlessSdkName)' != ''" Text="Headless SDK identity leaked transitively." />
                <Error Condition="'$(_HeadlessSdkBuildPropsImported)' != ''" Text="Headless props leaked transitively." />
                <Error Condition="'$(_HeadlessSdkBuildTargetsImported)' != ''" Text="Headless targets leaked transitively." />
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/referencing-project-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PropsImported=$(_HeadlessSdkBuildPropsImported)~TargetsImported=$(_HeadlessSdkBuildTargetsImported)~EditorConfigFiles=$(_ReferencingProjectEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -m:1 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        var contract = await File.ReadAllTextAsync(
            Path.Combine(project.RootDirectory, "referencing-project-contract.txt"),
            TestContext.Current.CancellationToken
        );
        Assert.StartsWith(
            "SdkName=~PropsImported=~TargetsImported=~EditorConfigFiles=",
            contract.Trim(),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Headless.NET.Sdk.Analyzers.editorconfig", contract, StringComparison.OrdinalIgnoreCase);
    }

    // The Test SDK README's PackageReference example, copied as written with the root README's
    // restore bootstrap, must build. NuGet evaluates a project for restore with
    // ExcludeRestorePackageImports=true, so the package's OutputType=Exe default is invisible there;
    // the example declares it, or restore plans a library and skips the net8.0 apphost pack that a
    // .NET 10 SDK does not bundle. The restore-time evaluation fails the same way on any host, while
    // the build fails only where that apphost pack is missing.
    [Fact]
    public async Task should_build_the_readme_package_reference_example_when_a_test_project_copies_it()
    {
        var repositoryRoot = TestRepository.FindRoot("README PackageReference example");
        var projectContent = ReadReadmeXmlBlock(
                Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk.Test", "README.md"),
                """<Project Sdk="Microsoft.NET.Sdk">""",
                """<PackageReference Include="Headless.NET.Sdk.Test" """
            )
            .Replace("x.y.z", fixture.PackageVersion, StringComparison.Ordinal);
        var restoreBootstrap = ReadReadmeXmlBlock(
            Path.Combine(repositoryRoot, "README.md"),
            "<Project>",
            "<NuGetAuditMode>"
        );

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            includePackageReference: false,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Build.props"] = restoreBootstrap,
            }
        );
        await WriteProjectAsync(
            project,
            projectContent,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ReadmeTests.cs"] = """
                using Xunit;

                namespace ConsumerProject;

                public sealed class ReadmeTests
                {
                    [Fact]
                    public void adds() => Assert.Equal(2, 1 + 1);
                }
                """,
            }
        );

        var restoreView = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -getProperty:OutputType -p:ExcludeRestorePackageImports=true -nologo -v:quiet"
        );
        Assert.True(restoreView.ExitCode == 0, restoreView.Output);
        Assert.Equal("Exe", restoreView.Output.Trim());

        var build = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(build.ExitCode == 0, build.Output);
    }

    private static string ReadReadmeXmlBlock(string readmePath, string firstLine, string marker)
    {
        var blocks = Regex
            .Matches(File.ReadAllText(readmePath), @"```xml\r?\n(?<body>.*?)\r?\n```", RegexOptions.Singleline)
            .Select(match => match.Groups["body"].Value)
            .Where(body =>
                body.StartsWith(firstLine, StringComparison.Ordinal) && body.Contains(marker, StringComparison.Ordinal)
            )
            .ToList();

        return Assert.Single(blocks);
    }
}
