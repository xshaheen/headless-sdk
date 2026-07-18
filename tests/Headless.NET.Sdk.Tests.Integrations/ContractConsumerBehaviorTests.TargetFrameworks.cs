using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
{
    [Fact]
    public async Task should_require_an_explicit_target_framework()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: null,
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:HeadlessInferTargetFramework=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("TargetFramework", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("netstandard2.0")]
    [InlineData("net8.0")]
    [InlineData("net9.0")]
    public async Task should_not_restrict_consumer_target_frameworks(string targetFramework)
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: targetFramework,
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        var properties = await project.EvaluateHeadlessPropertiesAsync();
        Assert.Equal(targetFramework, properties["TargetFramework"]);
        Assert.Equal("Headless.NET.Sdk", properties["HeadlessSdkName"]);
    }

    [Fact]
    public async Task should_keep_end_of_life_target_framework_warnings_non_fatal_on_ci()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net6.0",
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:CI=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("NETSDK1138", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_keep_late_ci_end_of_life_warnings_visible_and_non_fatal(bool useDirectoryBuildProps)
    {
        var extraProperties = useDirectoryBuildProps
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["ContinuousIntegrationBuild"] = "true" };
        var additionalFiles = useDirectoryBuildProps
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Build.props"] =
                    "<Project><PropertyGroup><ContinuousIntegrationBuild>true</ContinuousIntegrationBuild></PropertyGroup></Project>",
            }
            : null;

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net6.0",
            includePackageReference: false,
            extraProperties: extraProperties,
            additionalFiles: additionalFiles
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("NETSDK1138", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("error NETSDK1138", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_restore_mandatory_dependencies_for_older_package_reference_consumers()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "netstandard1.0"
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertMandatoryAnalyzersInAssetsAsync(project);
        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"Microsoft.Sbom.Targets/", assets, StringComparison.Ordinal);
    }
}
