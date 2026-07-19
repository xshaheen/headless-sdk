using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
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
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
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
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
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

        var repositoryRoot = TestRepository.FindRoot("global.json SDK resolution contract test");
        using var repositoryGlobalJson = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(repositoryRoot, "global.json"),
                TestContext.Current.CancellationToken
            )
        );
        var sdkVersion = repositoryGlobalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sdkVersion));
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
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
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
            $"build {Quote(project.ProjectFilePath)} --no-incremental -m:1 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
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
}
