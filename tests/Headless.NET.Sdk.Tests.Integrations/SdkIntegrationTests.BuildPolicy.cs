using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;
using StructuredLoggerSerialization = Microsoft.Build.Logging.StructuredLogger.Serialization;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class SdkIntegrationTests
{
    [Fact]
    public async Task should_include_implicit_analyzer_packages_when_restoring_via_package_reference()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"Meziantou.Analyzer/", assets, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.CodeAnalysis.BannedApiAnalyzers/", assets, StringComparison.Ordinal);
        Assert.Contains("\"AsyncFixer/", assets, StringComparison.Ordinal);
        Assert.Contains("Meziantou.Analyzer.dll", assets, StringComparison.Ordinal);
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers.dll", assets, StringComparison.Ordinal);
        Assert.Contains("AsyncFixer.dll", assets, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_import_bundled_analyzer_editorconfig_when_building()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe",
            // This test asserts MA0047 surfaces as a warning, proving the bundled analyzer
            // editorconfig was imported. Keep warnings non-fatal explicitly for isolation.
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TreatWarningsAsErrors"] = "false",
            },
            additionalFiles: new Dictionary<string, string>
            {
                ["Sample.cs"] = """
Console.WriteLine();

class Foo { }
""",
            }
        );

        var result = await project.BuildAndCollectDiagnosticsAsync(
            $"-p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(
            result.GetBinLogFiles(),
            file => file.EndsWith("Headless.NET.Sdk.Analyzers.editorconfig", StringComparison.OrdinalIgnoreCase)
        );
        Assert.True(result.HasWarning("MA0047"), result.SarifSummary);
    }

    [Fact]
    public async Task should_not_report_configure_await_when_enforcement_is_disabled_by_default()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Repro.cs"] =
                    "namespace ConsumerProject; public static class Repro { public static async System.Threading.Tasks.Task M() => await System.Threading.Tasks.Task.Delay(1); }",
            }
        );

        // --no-incremental: analyzers do not reliably re-run on an incremental build, so force a full
        // build to make the (absent) CA2007 diagnostic deterministic.
        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("CA2007", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_report_configure_await_warning_when_enforcement_is_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            // This test asserts CA2007 surfaces as a warning, proving the opt-in enforcement
            // editorconfig was imported. Keep warnings non-fatal explicitly for isolation.
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEnforceConfigureAwait"] = "true",
                ["TreatWarningsAsErrors"] = "false",
            },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Repro.cs"] =
                    "namespace ConsumerProject; public static class Repro { public static async System.Threading.Tasks.Task M() => await System.Threading.Tasks.Task.Delay(1); }",
            }
        );

        // --no-incremental: analyzers do not reliably re-run on an incremental build, so force a full
        // build to make the CA2007 diagnostic deterministic.
        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("warning CA2007", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_use_expected_msbuild_property_defaults()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("LatestMajor", properties["RollForward"]);
        Assert.Equal("true", properties["PackAsTool"]);
        Assert.Equal("Headless.NET.Sdk", properties["HeadlessSdkName"]);
        Assert.Equal("Default", properties["HeadlessSdkProjectType"]);
        Assert.Equal("true", properties["IsPackable"]);
        Assert.Equal("false", properties["EnablePackageValidation"]);
        Assert.Equal("true", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.Contains("ConsumerProject.Tests.Unit", properties["InternalsVisibleTo"], StringComparison.Ordinal);

        await using var libraryProject = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );
        var libraryDefaults = await libraryProject.EvaluateHeadlessPropertiesAsync();
        Assert.Equal("true", libraryDefaults["EnablePackageValidation"]);

        var overrides = await libraryProject.EvaluateHeadlessPropertiesAsync("-p:EnablePackageValidation=false");
        Assert.Equal("false", overrides["EnablePackageValidation"]);
    }

    [Fact]
    public async Task should_skip_conventional_internals_visible_to_for_signed_projects()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["SignAssembly"] = "true" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.DoesNotContain("ConsumerProject.Tests.Unit", properties["InternalsVisibleTo"], StringComparison.Ordinal);
        Assert.Empty(properties["InternalsVisibleTo"]);
    }

    [Fact]
    public async Task should_allow_disabling_conventional_internals_visible_to_attributes()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEmitInternalsVisibleToAttributes"] = "false",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("false", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.Empty(properties["InternalsVisibleTo"]);
    }

    [Fact]
    public async Task should_exclude_lscache_files_from_default_items()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string> { ["LocalState.lscache"] = "cache" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.DoesNotContain("LocalState.lscache", properties["NoneItems"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_treat_msbuild_warnings_as_errors_on_continuous_integration()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:CI=true");

        Assert.Equal("true", properties["MSBuildTreatWarningsAsErrors"]);
        Assert.NotEqual("true", properties["RestoreLockedMode"]);
    }

    [Fact]
    public async Task should_enforce_locked_restore_when_on_continuous_integration()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            extraPackageReferences: new Dictionary<string, string>(StringComparer.Ordinal) { ["Humanizer"] = "2.14.1" }
        );

        var seedResult = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:CI=true -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=false -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(seedResult.ExitCode == 0, seedResult.Output);

        var projectContent = await File.ReadAllTextAsync(
            project.ProjectFilePath,
            TestContext.Current.CancellationToken
        );
        projectContent = projectContent.Replace(
            """<PackageReference Include="Humanizer" Version="2.14.1" />""",
            """<PackageReference Include="Humanizer" Version="2.13.14" />""",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            projectContent,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var lockedResult = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:CI=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, lockedResult.ExitCode);
        Assert.Contains("NU1004", lockedResult.Output, StringComparison.Ordinal);
    }

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
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("NETSDK1013", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_include_target_framework_gated_global_usings_when_building()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net8.0",
            additionalFiles: new Dictionary<string, string>
            {
                ["JsonConsumer.cs"] = """
namespace ConsumerProject;

public static class JsonConsumer
{
    public static string Serialize(object value) => JsonSerializer.Serialize(value);
}
""",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Theory]
    [InlineData("net8.0", false, false)]
    [InlineData("net8.0", false, true)]
    [InlineData("net9.0", true, false)]
    [InlineData("net9.0", true, true)]
    public async Task should_honor_in_project_strict_system_text_json_opt_in_by_target_framework(
        string targetFramework,
        bool shouldAddOptions,
        bool usePackageReference
    )
    {
        // RuntimeHostConfigurationOption.props -- off by default; opt-in adds STJ runtime switches
        // only when the consumer target framework supports them.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: targetFramework,
            outputType: "Exe",
            includePackageReference: usePackageReference,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEnableStrictSystemTextJsonRuntimeDefaults"] = "true",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var options = properties["RuntimeHostConfigurationOptions"];
        var requiredConstructorOption =
            "System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault=true";
        var nullableAnnotationsOption = "System.Text.Json.Serialization.RespectNullableAnnotationsDefault=true";

        if (shouldAddOptions)
        {
            Assert.Contains(requiredConstructorOption, options, StringComparison.Ordinal);
            Assert.Contains(nullableAnnotationsOption, options, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(requiredConstructorOption, options, StringComparison.Ordinal);
            Assert.DoesNotContain(nullableAnnotationsOption, options, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task should_leave_strict_system_text_json_runtime_options_disabled_by_default()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net9.0",
            outputType: "Exe",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        Assert.DoesNotContain(
            "RespectRequiredConstructorParametersDefault",
            properties["RuntimeHostConfigurationOptions"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task should_enable_container_support_when_consumed_as_web_sdk_on_github_actions()
    {
        // SupportWebContainer.targets -- only activates for Microsoft.NET.Sdk.Web on GitHub Actions.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: "Microsoft.NET.Sdk.Web",
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync(
            "-p:GITHUB_ACTIONS=true -p:GITHUB_REPOSITORY=xshaheen/headless-sdk -p:GITHUB_REF=refs/heads/main -p:GITHUB_RUN_NUMBER=123"
        );

        Assert.Equal("true", properties["EnableSdkContainerSupport"]);
        Assert.Equal("ghcr.io", properties["ContainerRegistry"]);
        Assert.Equal("xshaheen/headless-sdk", properties["ContainerRepository"]);
        Assert.Equal("1.0.123;latest", properties["ContainerImageTags"]);
    }

    [Fact]
    public async Task should_preserve_explicit_web_container_overrides_on_github_actions()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: "Microsoft.NET.Sdk.Web",
            outputType: "Exe",
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EnableSdkContainerSupport"] = "false",
                ["ContainerRegistry"] = "registry.example.test",
                ["ContainerRepository"] = "custom/repository",
                ["ContainerImageTagsMainVersionPrefix"] = "9.8",
                ["ContainerImageTagsIncludeLatest"] = "false",
                ["ContainerImageTags"] = "consumer-tag",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync(
            "-p:GITHUB_ACTIONS=true -p:GITHUB_REPOSITORY=xshaheen/headless-sdk -p:GITHUB_REF_NAME=main -p:GITHUB_RUN_NUMBER=42"
        );

        Assert.Equal("false", properties["EnableSdkContainerSupport"]);
        Assert.Equal("registry.example.test", properties["ContainerRegistry"]);
        Assert.Equal("custom/repository", properties["ContainerRepository"]);
        Assert.Equal("9.8", properties["ContainerImageTagsMainVersionPrefix"]);
        Assert.Equal("false", properties["ContainerImageTagsIncludeLatest"]);
        Assert.Equal("consumer-tag", properties["ContainerImageTags"]);
    }
}
