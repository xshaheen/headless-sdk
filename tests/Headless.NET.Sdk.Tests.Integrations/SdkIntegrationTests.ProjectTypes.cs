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
    public async Task should_set_test_project_properties_when_using_test_project_type_sdk()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            targetFramework: "net8.0",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("Headless.NET.Sdk.Test", properties["HeadlessSdkName"]);
        Assert.Equal("Test", properties["HeadlessSdkProjectType"]);
        Assert.Equal("true", properties["IsTestProject"]);
        Assert.Equal("false", properties["IsPackable"]);

        var noWarn = properties["NoWarn"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("CA1849", noWarn);
        Assert.Contains("MA0042", noWarn);
        Assert.Contains("MA0166", noWarn);
        Assert.Contains("CA1861", noWarn);
        Assert.Contains("CA1859", noWarn);
        Assert.Contains("CA1720", noWarn);
    }

    [Fact]
    public async Task should_set_test_project_properties_when_is_test_project_is_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["IsTestProject"] = "true" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["IsTestProject"]);
        Assert.Equal("false", properties["IsTestHarnessProject"]);
        Assert.Equal("false", properties["IsPackable"]);
        Assert.Equal(string.Empty, properties["HeadlessSymbolFormat"]);
        Assert.DoesNotContain("xshaheen", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains(
            "Headless.NET.Sdk.Tests.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );

        var noWarn = properties["NoWarn"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("CA1849", noWarn);
        Assert.Contains("MA0042", noWarn);
        Assert.Contains("MA0166", noWarn);
        Assert.Contains("CA1861", noWarn);
        Assert.Contains("CA1859", noWarn);
        Assert.Contains("CA1720", noWarn);

        Assert.Empty(properties["TestingPlatformCommandLineArguments"]);
    }

    [Fact]
    public async Task should_set_test_harness_project_properties_when_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IsTestHarnessProject"] = "true",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["IsTestHarnessProject"]);
        Assert.Equal("false", properties["IsTestProject"]);
        Assert.Empty(properties["IsTestingPlatformApplication"]);
        Assert.Empty(properties["GenerateRuntimeConfigurationFiles"]);
        Assert.Equal("false", properties["IsPackable"]);
        Assert.Equal(string.Empty, properties["HeadlessSymbolFormat"]);
        Assert.DoesNotContain("xshaheen", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains(
            "Headless.NET.Sdk.Tests.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );

        var noWarn = properties["NoWarn"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("CA1849", noWarn);
        Assert.Contains("MA0042", noWarn);
        Assert.Contains("MA0166", noWarn);
        Assert.Contains("CA1861", noWarn);
        Assert.Contains("CA1859", noWarn);
        Assert.Contains("CA1720", noWarn);

        Assert.Empty(properties["TestingPlatformCommandLineArguments"]);
    }

    [Fact]
    public async Task should_set_mtp_command_line_arguments_when_using_test_project_type_sdk()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false
        );

        // Defaults: MTP args must be exposed as static evaluated properties. The new dotnet test
        // experience reads TestingPlatformCommandLineArguments during evaluation and never runs the
        // legacy _MTPBuild target -- a regression guard against re-introducing a target-based
        // assignment that silently drops every platform argument.
        var defaults = await project.EvaluateHeadlessPropertiesAsync();
        var args = defaults["TestingPlatformCommandLineArguments"];
        Assert.Contains("--report-trx", args, StringComparison.Ordinal);
        Assert.Contains("--crashdump", args, StringComparison.Ordinal);
        Assert.Contains("--hangdump", args, StringComparison.Ordinal);
        Assert.Contains("--minimum-expected-tests 1", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--coverage", args, StringComparison.Ordinal);

        // With coverage enabled, add the MTP coverage and settings arguments.
        var withCoverage = await project.EvaluateHeadlessPropertiesAsync("-p:EnableCodeCoverage=true");
        var coverageArgs = withCoverage["TestingPlatformCommandLineArguments"];
        Assert.Contains("--coverage", coverageArgs, StringComparison.Ordinal);
        Assert.Contains("--coverage-settings", coverageArgs, StringComparison.Ordinal);
        Assert.Contains("default.runsettings", coverageArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_reference_all_mtp_extensions_unconditionally()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("Microsoft.Testing.Extensions.CodeCoverage", packageReferences);
        Assert.Contains("Microsoft.Testing.Extensions.CrashDump", packageReferences);
        Assert.Contains("Microsoft.Testing.Extensions.HangDump", packageReferences);
        Assert.Contains("Microsoft.Testing.Extensions.HotReload", packageReferences);
        Assert.Contains("Microsoft.Testing.Extensions.Retry", packageReferences);
        Assert.Contains("Microsoft.Testing.Extensions.TrxReport", packageReferences);
    }

    [Theory]
    [InlineData("Headless.NET.Sdk.Web", "Microsoft.NET.Sdk.Web", "Web", "false")]
    [InlineData("Headless.NET.Sdk.Test", "Microsoft.NET.Sdk", "Test", "true")]
    [InlineData("Headless.NET.Sdk.Razor", "Microsoft.NET.Sdk.Razor", "Razor", "false")]
    [InlineData(
        "Headless.NET.Sdk.BlazorWebAssembly",
        "Microsoft.NET.Sdk.BlazorWebAssembly",
        "BlazorWebAssembly",
        "false"
    )]
    [InlineData("Headless.NET.Sdk.WindowsDesktop", "Microsoft.NET.Sdk.WindowsDesktop", "WindowsDesktop", "false")]
    public async Task should_set_project_type_properties_when_using_project_type_package_reference(
        string packageId,
        string sdkName,
        string projectType,
        string isTestProject
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: sdkName,
            targetFramework: "net8.0",
            packageReferenceId: packageId
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(packageId, properties["HeadlessSdkName"]);
        Assert.Equal(projectType, properties["HeadlessSdkProjectType"]);
        Assert.Equal(isTestProject, properties["IsTestProject"]);
    }

    [Theory]
    [InlineData("Headless.NET.Sdk.Web", "Web", "false")]
    [InlineData("Headless.NET.Sdk.Razor", "Razor", "true")]
    [InlineData("Headless.NET.Sdk.BlazorWebAssembly", "BlazorWebAssembly", "false")]
    [InlineData("Headless.NET.Sdk.WindowsDesktop", "WindowsDesktop", "true")]
    public async Task should_set_project_type_properties_when_using_project_type_sdk(
        string sdkName,
        string projectType,
        string isPackable
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"{sdkName}/{fixture.PackageVersion}",
            targetFramework: "net8.0",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(sdkName, properties["HeadlessSdkName"]);
        Assert.Equal(projectType, properties["HeadlessSdkProjectType"]);
        Assert.Equal(isPackable, properties["IsPackable"]);
    }

    [Theory]
    [InlineData("Headless.NET.Sdk.Web")]
    [InlineData("Headless.NET.Sdk.BlazorWebAssembly")]
    public async Task should_preserve_explicit_packable_override_when_using_project_type_sdk(string sdkName)
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"{sdkName}/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["IsPackable"] = "true" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["IsPackable"]);
    }

    [Theory]
    [InlineData("Headless.NET.Sdk.Web")]
    [InlineData("Headless.NET.Sdk.BlazorWebAssembly")]
    [InlineData("Headless.NET.Sdk.Test")]
    public async Task should_skip_pack_for_default_non_packable_project_type_sdk_when_warnings_are_errors(
        string sdkName
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"{sdkName}/{fixture.PackageVersion}",
            includePackageReference: false
        );
        Directory.CreateDirectory(project.PackagesDirectory);

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -c Release -o {Quote(project.PackagesDirectory)} -p:MSBuildTreatWarningsAsErrors=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(Directory.EnumerateFiles(project.PackagesDirectory, "*.nupkg", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task should_build_successfully_when_consumed_via_sdk_import()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Theory]
    [InlineData("Microsoft.NET.Sdk", true)]
    [InlineData("Headless.NET.Sdk/{version}", false)]
    public async Task should_build_successfully_when_consumer_uses_central_package_management(
        string sdkTemplate,
        bool includePackageReference
    )
    {
        var sdk = sdkTemplate.Replace("{version}", fixture.PackageVersion, StringComparison.Ordinal);
        var packageVersionItems = includePackageReference
            ? $@"<PackageVersion Include=""Headless.NET.Sdk"" Version=""{fixture.PackageVersion}"" />"
            : string.Empty;

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: sdk,
            includePackageReference: includePackageReference,
            useCentralPackageManagement: true,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Packages.props"] = CreateCentralPackageManagementProps(packageVersionItems),
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("NU1008", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_enforce_test_sdk_owned_mtp_versions_with_central_package_management(
        bool addCentralExtensionVersion
    )
    {
        var centralExtension = addCentralExtensionVersion
            ? $@"<PackageVersion Include=""Microsoft.Testing.Extensions.CrashDump"" Version=""{TestRepository.ReadCentralPackageVersion("Microsoft.Testing.Extensions.CrashDump")}"" />"
            : string.Empty;

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false,
            useCentralPackageManagement: true,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Packages.props"] = CreateCentralPackageManagementProps(centralExtension),
            }
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        if (addCentralExtensionVersion)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.True(result.Output.Contains("NU1009", StringComparison.Ordinal), result.Output);
        }
        else
        {
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.DoesNotContain("NU1009", result.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_reject_central_overrides_for_sdk_owned_analyzers(bool useSdkConsumption)
    {
        var headlessVersion = useSdkConsumption
            ? string.Empty
            : $@"<PackageVersion Include=""Headless.NET.Sdk"" Version=""{fixture.PackageVersion}"" />";
        var analyzerVersion = useSdkConsumption ? "3.0.75" : "1.0.102";
        var centralVersions =
            $@"{headlessVersion}
<PackageVersion Include=""Meziantou.Analyzer"" Version=""{analyzerVersion}"" />";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: useSdkConsumption ? $"Headless.NET.Sdk/{fixture.PackageVersion}" : "Microsoft.NET.Sdk",
            includePackageReference: !useSdkConsumption,
            useCentralPackageManagement: true,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Packages.props"] = CreateCentralPackageManagementProps(centralVersions),
            }
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Meziantou.Analyzer", result.Output, StringComparison.Ordinal);
        if (useSdkConsumption)
        {
            Assert.True(result.Output.Contains("NU1009", StringComparison.Ordinal), result.Output);
        }
        else
        {
            Assert.True(
                result.Output.Contains("NU1107", StringComparison.Ordinal)
                    || result.Output.Contains("NU1109", StringComparison.Ordinal),
                result.Output
            );
        }
    }

    [Fact]
    public async Task should_skip_tool_packaging_when_project_is_a_web_project()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: "Microsoft.NET.Sdk.Web",
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(string.Empty, properties["PackAsTool"]);
    }

    [Fact]
    public async Task should_include_bundled_analyzer_editorconfig_when_using_defaults()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Contains(
            "Headless.NET.Sdk.Analyzers.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task should_include_test_analyzer_editorconfig_when_using_test_project_type_sdk()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Contains(
            "Headless.NET.Sdk.Analyzers.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Headless.NET.Sdk.Tests.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task should_keep_bundled_analyzer_editorconfig_when_legacy_opt_out_is_set()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:DisableSupportAnalyzerEditorConfigs=true");

        Assert.Contains(
            "Headless.NET.Sdk.Analyzers.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
    }
}
