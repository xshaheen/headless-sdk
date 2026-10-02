using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractCoverageTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    [Fact]
    public void should_package_the_sdk_owned_coverage_policy_contract()
    {
        using var package = ZipFile.OpenRead(fixture.GetPackagePath("Headless.NET.Sdk.Test"));

        var runsettings = XDocument.Parse(ReadPackageText(package, "configurations/default.runsettings"));
        Assert.Equal("false", Assert.Single(runsettings.Descendants("IncludeTestAssembly")).Value);

        var moduleExclusions = runsettings
            .Descendants("ModulePaths")
            .Single()
            .Descendants("ModulePath")
            .Select(element => element.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(@".*\.Tests\.[^.]+\.dll$", moduleExclusions);
        Assert.Contains(@".*\.Testing\.dll$", moduleExclusions);
        Assert.Contains(@".*\.Testing\.[^.]+\.dll$", moduleExclusions);

        var sourceExclusions = runsettings
            .Descendants("Sources")
            .Single()
            .Descendants("Source")
            .Select(element => element.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(@".*\.g\.cs$", sourceExclusions);
        Assert.Contains(@".*\.generated\.cs$", sourceExclusions);
        Assert.Contains(@".*\.Designer\.cs$", sourceExclusions);

        var generatedPathPattern = Assert.Single(
            sourceExclusions,
            pattern => pattern.Contains("obj", StringComparison.Ordinal)
        );
        var generatedPathRegex = new Regex(generatedPathPattern, RegexOptions.CultureInvariant);
        Assert.Matches(generatedPathRegex, "/repo/project/obj/generated/Output.cs");
        Assert.Matches(generatedPathRegex, "/repo/project/obj/Debug/net10.0/generated/Generator/Output.cs");
        Assert.Matches(generatedPathRegex, @"C:\repo\project\obj\Debug\net10.0\generated\Generator\Output.cs");
        Assert.DoesNotMatch(generatedPathRegex, "/repo/project/src/Order.cs");
        Assert.DoesNotMatch(generatedPathRegex, "/repo/project/src/Order.Partial.cs");
        Assert.DoesNotMatch(generatedPathRegex, @"C:\repo\project\src\Order.Partial.cs");

        var attributeExclusions = runsettings
            .Descendants("Attributes")
            .Single()
            .Descendants("Attribute")
            .Select(element => element.Value);
        Assert.Contains("System.CodeDom.Compiler.GeneratedCodeAttribute", attributeExclusions);

        var functionExclusions = runsettings
            .Descendants("Functions")
            .Single()
            .Descendants("Function")
            .Select(element => element.Value);
        Assert.Contains(@"^.*\.Migrations\..*$", functionExclusions);

        var testTargets = XDocument.Parse(ReadPackageText(package, "build/SupportTestProjects.targets"));
        var coverageSettingsProperty = Assert.Single(testTargets.Descendants("HeadlessCoverageSettingsPath"));
        Assert.Contains("configurations", coverageSettingsProperty.Value, StringComparison.Ordinal);
        Assert.Contains("default.runsettings", coverageSettingsProperty.Value, StringComparison.Ordinal);
        Assert.Equal("'$(HeadlessCoverageSettingsPath)' == ''", coverageSettingsProperty.Attribute("Condition")?.Value);

        var coverageSettingsArgument = Assert.Single(
            testTargets.Descendants("TestingPlatformCommandLineArguments"),
            element => element.Value.Contains("--coverage-settings", StringComparison.Ordinal)
        );
        Assert.Contains("$(HeadlessCoverageSettingsPath)", coverageSettingsArgument.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("MSBuildThisFileDirectory", coverageSettingsArgument.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("project-sdk")]
    [InlineData("global-json-sdk")]
    [InlineData("additional-sdk")]
    [InlineData("package-reference")]
    public async Task should_resolve_packaged_coverage_settings_and_inject_one_argument_pair(string consumptionMode)
    {
        var packageReference = string.Equals(consumptionMode, "package-reference", StringComparison.Ordinal);
        var sdk = packageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk.Test/{fixture.PackageVersion}";
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: sdk,
            targetFramework: "net8.0",
            includePackageReference: packageReference,
            packageReferenceId: "Headless.NET.Sdk.Test",
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["EnableCodeCoverage"] = "true" }
        );

        if (string.Equals(consumptionMode, "additional-sdk", StringComparison.Ordinal))
        {
            await WriteProjectAsync(
                project,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Sdk Name="Headless.NET.Sdk.Test" Version="{{fixture.PackageVersion}}" />
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <EnableCodeCoverage>true</EnableCodeCoverage>
                  </PropertyGroup>
                </Project>
                """,
                new Dictionary<string, string>(StringComparer.Ordinal)
            );
        }
        else if (string.Equals(consumptionMode, "global-json-sdk", StringComparison.Ordinal))
        {
            await WriteProjectAsync(
                project,
                """
                <Project Sdk="Headless.NET.Sdk.Test">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <EnableCodeCoverage>true</EnableCodeCoverage>
                  </PropertyGroup>
                </Project>
                """,
                new Dictionary<string, string>(StringComparer.Ordinal)
            );
            await WriteGlobalJsonAsync(project);
        }

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(restore.ExitCode == 0, restore.Output);

        var settingsEvaluation = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -getProperty:HeadlessCoverageSettingsPath -nologo -v:quiet"
        );
        Assert.True(settingsEvaluation.ExitCode == 0, settingsEvaluation.Output);

        var coverageSettingsPath = settingsEvaluation.Output.Trim();
        Assert.False(string.IsNullOrWhiteSpace(coverageSettingsPath));
        Assert.True(Path.IsPathFullyQualified(coverageSettingsPath), coverageSettingsPath);
        Assert.True(File.Exists(coverageSettingsPath), coverageSettingsPath);

        using var package = ZipFile.OpenRead(fixture.GetPackagePath("Headless.NET.Sdk.Test"));
        var packagedSettings = ReadPackageText(package, "configurations/default.runsettings");
        var resolvedSettings = await File.ReadAllTextAsync(coverageSettingsPath, TestContext.Current.CancellationToken);
        Assert.Equal(XDocument.Parse(packagedSettings).ToString(), XDocument.Parse(resolvedSettings).ToString());

        var argumentsEvaluation = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -getProperty:TestingPlatformCommandLineArguments -nologo -v:quiet"
        );
        Assert.True(argumentsEvaluation.ExitCode == 0, argumentsEvaluation.Output);

        var arguments = argumentsEvaluation.Output.Trim();
        Assert.False(string.IsNullOrWhiteSpace(arguments));
        Assert.Single(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), token => token == "--coverage");
        Assert.Single(CoverageSettingsArgument.Matches(arguments).Cast<Match>());
        Assert.Contains($"--coverage-settings \"{coverageSettingsPath}\"", arguments, StringComparison.Ordinal);
    }

    private async Task WriteGlobalJsonAsync(ConsumerProject project)
    {
        var repositoryRoot = TestRepository.FindRoot("coverage property global.json contract test");
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
                "rollForward": "disable",
                "allowPrerelease": false
              },
              "msbuild-sdks": {
                "Headless.NET.Sdk.Test": "{{fixture.PackageVersion}}"
              }
            }
            """,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );
    }

    private static string ReadPackageText(ZipArchive package, string path)
    {
        var entry = package.GetEntry(path);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"(?<!\S)--coverage-settings(?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex CoverageSettingsArgument { get; }
}
