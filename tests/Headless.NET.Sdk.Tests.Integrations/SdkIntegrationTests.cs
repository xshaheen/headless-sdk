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

[CollectionDefinition(nameof(HeadlessSdkPackageCollection))]
public sealed class HeadlessSdkPackageCollection : ICollectionFixture<HeadlessSdkPackageFixture>;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class SdkIntegrationTests(HeadlessSdkPackageFixture fixture)
{
    [Fact]
    public async Task should_pack_without_error_when_no_logo_is_provided()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            warningsAsErrors: "CS1591"
        );

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -c Release -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("NU5046", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_report_documentation_warnings_when_explicitly_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            warningsAsErrors: "CS1591",
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DisableDocumentationWarnings"] = "false",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CS1591", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_not_overwrite_existing_editorconfig_when_using_defaults()
    {
        const string ExistingEditorConfig = """
root = true

[*.cs]
indent_size = 2
""";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            editorConfigContent: ExistingEditorConfig
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(
            NormalizeLineEndings(ExistingEditorConfig),
            NormalizeLineEndings(
                await File.ReadAllTextAsync(project.EditorConfigPath, TestContext.Current.CancellationToken)
            )
        );
    }

    [Fact]
    public async Task should_copy_editorconfig_when_scaffold_target_invoked_with_editorconfig_selector()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            enableEditorConfigCopy: true
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(
            File.Exists(project.EditorConfigPath),
            "Expected the scaffold target to create .editorconfig when the selector is set."
        );

        var copiedEditorConfig = await File.ReadAllTextAsync(
            project.EditorConfigPath,
            TestContext.Current.CancellationToken
        );
        Assert.Contains("# Common Settings", copiedEditorConfig, StringComparison.Ordinal);

        // Selecting only .editorconfig must not pull in the other files.
        Assert.False(
            File.Exists(project.CSharpierIgnorePath),
            "Did not expect .csharpierignore for editorconfig-only selector."
        );
        Assert.False(File.Exists(project.GitIgnorePath), "Did not expect .gitignore for editorconfig-only selector.");
        Assert.False(
            File.Exists(project.GitAttributesPath),
            "Did not expect .gitattributes for editorconfig-only selector."
        );
    }

    [Fact]
    public async Task should_copy_default_config_files_when_scaffold_target_invoked_with_master_selector()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            enableDefaultConfigFilesCopy: true
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(project.EditorConfigPath), "Expected the scaffold target to create .editorconfig.");
        Assert.True(
            File.Exists(project.CSharpierIgnorePath),
            "Expected the scaffold target to create .csharpierignore."
        );
        Assert.True(File.Exists(project.GitIgnorePath), "Expected the scaffold target to create .gitignore.");
        Assert.True(File.Exists(project.GitAttributesPath), "Expected the scaffold target to create .gitattributes.");

        var csharpierIgnore = await File.ReadAllTextAsync(
            project.CSharpierIgnorePath,
            TestContext.Current.CancellationToken
        );
        Assert.Contains("**/*.verified.*", csharpierIgnore, StringComparison.Ordinal);
        Assert.Contains("**/*.received.*", csharpierIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void should_pack_nuget_audit_targets_with_fixed_warnings_as_errors_expression()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var entry = package.GetEntry("build/SupportNuGetAudit.targets");

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open());
        var content = reader.ReadToEnd();

        var warningsAsErrors = XDocument
            .Parse(content)
            .Root!.Elements("PropertyGroup")
            .Elements("WarningsAsErrors")
            .Single()
            .Value;

        // Vulnerability severities (NU1901-NU1904) are escalated to errors on CI/Release, prefixed by
        // the existing $(WarningsAsErrors) so consumer values are preserved.
        Assert.Contains("$(WarningsAsErrors);NU1901;NU1902;NU1903;NU1904", warningsAsErrors, StringComparison.Ordinal);
        // NU1900 (audit source unreachable) must NOT be escalated: a connectivity blip should not fail the build.
        Assert.DoesNotContain("NU1900", warningsAsErrors, StringComparison.Ordinal);
    }

    [Fact]
    public void should_suppress_test_noise_warnings_when_project_is_a_test_project()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        // The IsTestableProject-conditioned NoWarn lives in SupportGeneral.targets (not .props) so a
        // consumer-set IsTestableProject (Directory.Build.props/csproj) is visible under MSBuild SDK
        // consumption, where build props load before Directory.Build.props.
        var content = ReadPackageEntry(package, "build/SupportGeneral.targets");
        var document = XDocument.Parse(content);
        var testNoWarn = document
            .Root!.Elements("PropertyGroup")
            .Elements("NoWarn")
            .Single(element =>
                string.Equals(
                    element.Attribute("Condition")?.Value,
                    "'$(IsTestableProject)' == 'true'",
                    StringComparison.Ordinal
                )
            )
            .Value;

        Assert.Contains("CA1849", testNoWarn, StringComparison.Ordinal);
        Assert.Contains("MA0042", testNoWarn, StringComparison.Ordinal);
        Assert.Contains("MA0166", testNoWarn, StringComparison.Ordinal);
        Assert.Contains("CA1861", testNoWarn, StringComparison.Ordinal);
        Assert.Contains("CA1859", testNoWarn, StringComparison.Ordinal);
        Assert.Contains("CA1720", testNoWarn, StringComparison.Ordinal);
    }

    [Fact]
    public void should_pack_analyzer_hygiene_and_coverage_settings()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        var implicitAnalyzers = ReadPackageEntry(package, "build/SupportImplicitAnalyzers.props");
        var implicitAnalyzerReferences = XDocument
            .Parse(implicitAnalyzers)
            .Descendants("PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")?.Value ?? string.Empty,
                element => element,
                StringComparer.Ordinal
            );

        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "Meziantou.Analyzer");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "Microsoft.CodeAnalysis.BannedApiAnalyzers");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "AsyncFixer");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "Asyncify");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "Microsoft.VisualStudio.Threading.Analyzers");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "SmartAnalyzers.MultithreadingAnalyzer");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "Roslynator.Analyzers");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "ReflectionAnalyzers");
        AssertImplicitAnalyzerReference(implicitAnalyzerReferences, "ErrorProne.NET.CoreAnalyzers");

        var analyzerHygiene = ReadPackageEntry(package, "build/SupportAnalyzerHygiene.targets");
        Assert.Contains("HeadlessDisableSponsorLinkAnalyzers", analyzerHygiene, StringComparison.Ordinal);
        Assert.Contains("DisableSponsorLink", analyzerHygiene, StringComparison.Ordinal);
        Assert.Contains("Disable_SponsorLink", analyzerHygiene, StringComparison.Ordinal);

        var analyzerEditorConfigs = ReadPackageEntry(package, "build/SupportAnalyzerEditorConfigs.props");
        Assert.Contains("Headless.NET.Sdk.Analyzers.editorconfig", analyzerEditorConfigs, StringComparison.Ordinal);
        Assert.NotNull(package.GetEntry("configurations/Headless.NET.Sdk.Analyzers.editorconfig"));
        Assert.NotNull(package.GetEntry("configurations/Headless.NET.Sdk.Tests.editorconfig"));

        var regularAnalyzerConfig = ReadPackageEntry(package, "configurations/Headless.NET.Sdk.Analyzers.editorconfig");
        var testAnalyzerConfig = ReadPackageEntry(package, "configurations/Headless.NET.Sdk.Tests.editorconfig");

        Assert.Contains("dotnet_diagnostic.CA2227.severity = silent", regularAnalyzerConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1716.severity = none", regularAnalyzerConfig, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet_diagnostic.CA1045.severity = suggestion",
            regularAnalyzerConfig,
            StringComparison.Ordinal
        );
        Assert.Contains("dotnet_diagnostic.CA1028.severity = none", testAnalyzerConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA2201.severity = none", testAnalyzerConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA2227.severity = none", testAnalyzerConfig, StringComparison.Ordinal);

        var testTargets = ReadPackageEntry(package, "build/SupportTestProjects.targets");
        Assert.Contains("configurations/default.runsettings", testTargets, StringComparison.Ordinal);
        // MTP coverage settings use --coverage-settings; --settings is a dotnet test CLI option the
        // MTP runner rejects as unknown. (Behavioral check that the args actually flow to an MTP
        // consumer lives in MsBuildSetsMtpCommandLineArgumentsForTestSdk.)
        Assert.Contains("--coverage-settings", testTargets, StringComparison.Ordinal);

        var runsettings = ReadPackageEntry(package, "configurations/default.runsettings");
        Assert.Contains("<TreatNoTestsAsError>true</TreatNoTestsAsError>", runsettings, StringComparison.Ordinal);
        Assert.Contains(@"GitHubActionsTestLogger\.dll", runsettings, StringComparison.Ordinal);
        Assert.Contains(@".*\.Tests\.[^.]+\.dll$", runsettings, StringComparison.Ordinal);
        Assert.Contains(@".*\.Testing\.[^.]+\.dll$", runsettings, StringComparison.Ordinal);
        Assert.Contains(@".*\.g\.cs$", runsettings, StringComparison.Ordinal);
        Assert.Contains(@"^.*\.Migrations\..*$", runsettings, StringComparison.Ordinal);

        var generalTargets = ReadPackageEntry(package, "build/SupportGeneral.targets");
        Assert.Contains("DisableDocumentationWarnings", generalTargets, StringComparison.Ordinal);
        Assert.Contains("CS1573;CS1591", generalTargets, StringComparison.Ordinal);
        Assert.Contains("Headless.NET.Sdk.Tests.editorconfig", generalTargets, StringComparison.Ordinal);
    }

    [Fact]
    public void should_flow_implicit_analyzers_as_transitive_dependencies_when_packed()
    {
        // PackageReference consumers receive the implicit analyzers via transitive nuspec deps,
        // not via SupportImplicitAnalyzers.props -- props in build/ are imported during MSBuild
        // evaluation, AFTER NuGet restore has resolved the package graph. The analyzers must
        // therefore appear in the SDK's nuspec as <dependency> entries, with include="all" so
        // analyzer assets flow into the consumer's compile context. DevelopmentDependency=true on
        // the SDK itself prevents two-hop leakage (a consumer's nupkg won't list Headless.NET.Sdk
        // as a dep, so the analyzers don't reach grandparent consumers). Removing the
        // <PackageReference Update=... PrivateAssets="none" IncludeAssets="all"> block in
        // Headless.NET.Sdk.csproj would break analyzer delivery on the PackageReference path.
        var implicitAnalyzerIds = new[]
        {
            "Meziantou.Analyzer",
            "Microsoft.CodeAnalysis.BannedApiAnalyzers",
            "AsyncFixer",
            "Asyncify",
            "Microsoft.VisualStudio.Threading.Analyzers",
            "SmartAnalyzers.MultithreadingAnalyzer",
            "Roslynator.Analyzers",
            "ReflectionAnalyzers",
            "ErrorProne.NET.CoreAnalyzers",
        };

        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var nuspec = ReadPackageEntry(package, "Headless.NET.Sdk.nuspec");
        var dependencies = XDocument
            .Parse(nuspec)
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "dependency", StringComparison.Ordinal))
            .ToDictionary(
                element => element.Attribute("id")?.Value ?? string.Empty,
                element => element,
                StringComparer.Ordinal
            );

        foreach (var id in implicitAnalyzerIds)
        {
            Assert.True(dependencies.TryGetValue(id, out var dependency), $"Missing transitive dependency: {id}.");
            Assert.Equal("All", dependency.Attribute("include")?.Value);
        }
    }

    [Fact]
    public void should_pack_single_file_target_framework_and_sdk_metadata_support()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        Assert.NotNull(package.GetEntry("build/SupportSingleFileApp.props"));
        Assert.NotNull(package.GetEntry("build/SupportTargetFrameworkInference.props"));
        Assert.Null(package.GetEntry("build/SupportNpm.targets"));
        Assert.NotNull(package.GetEntry("configurations/Headless.NET.Sdk.SingleFileApp.editorconfig"));

        var bannedNewtonsoftJson = ReadPackageEntry(package, "configurations/BannedSymbols.NewtonsoftJson.txt");
        Assert.Contains("N:Newtonsoft.Json.Linq", bannedNewtonsoftJson, StringComparison.Ordinal);
        Assert.Contains("N:Newtonsoft.Json.Serialization", bannedNewtonsoftJson, StringComparison.Ordinal);

        var assemblyAttributes = ReadPackageEntry(package, "build/SupportAssemblyAttributes.targets");
        Assert.Contains("Headless.NET.Sdk.SdkName", assemblyAttributes, StringComparison.Ordinal);
    }

    [Fact]
    public void should_pack_sdk_wrappers_and_build_assets_for_project_type_packages()
    {
        var expectedPackages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Headless.NET.Sdk.Web"] = "Microsoft.NET.Sdk.Web",
            ["Headless.NET.Sdk.Test"] = "Microsoft.NET.Sdk",
            ["Headless.NET.Sdk.Razor"] = "Microsoft.NET.Sdk.Razor",
            ["Headless.NET.Sdk.BlazorWebAssembly"] = "Microsoft.NET.Sdk.BlazorWebAssembly",
            ["Headless.NET.Sdk.WindowsDesktop"] = "Microsoft.NET.Sdk.WindowsDesktop",
        };

        foreach (var (packageId, baseSdk) in expectedPackages)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));

            Assert.NotNull(package.GetEntry("sdk/Sdk.props"));
            Assert.NotNull(package.GetEntry("sdk/Sdk.targets"));
            Assert.NotNull(package.GetEntry($"build/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"build/{packageId}.targets"));
            Assert.NotNull(package.GetEntry($"buildMultiTargeting/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"buildMultiTargeting/{packageId}.targets"));
            Assert.NotNull(package.GetEntry($"buildTransitive/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"buildTransitive/{packageId}.targets"));
            Assert.NotNull(package.GetEntry("build/SupportGeneral.props"));
            Assert.NotNull(package.GetEntry("configurations/editorconfig.txt"));

            var sdkProps = ReadPackageEntry(package, "sdk/Sdk.props");
            Assert.Contains(
                $"<HeadlessSdkName Condition=\"'$(HeadlessSdkName)' == ''\">{packageId}</HeadlessSdkName>",
                sdkProps,
                StringComparison.Ordinal
            );
            Assert.Contains($"Sdk=\"{baseSdk}\"", sdkProps, StringComparison.Ordinal);

            var buildProps = ReadPackageEntry(package, $"build/{packageId}.props");
            Assert.Contains(
                $"<HeadlessSdkName Condition=\"'$(HeadlessSdkName)' == ''\">{packageId}</HeadlessSdkName>",
                buildProps,
                StringComparison.Ordinal
            );
        }
    }

    [Fact]
    public void should_only_use_implicit_package_references_in_packed_build_assets()
    {
        var packageIds = new[]
        {
            "Headless.NET.Sdk",
            "Headless.NET.Sdk.Web",
            "Headless.NET.Sdk.Test",
            "Headless.NET.Sdk.Razor",
            "Headless.NET.Sdk.BlazorWebAssembly",
            "Headless.NET.Sdk.WindowsDesktop",
        };

        foreach (var packageId in packageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));

            foreach (var entry in package.Entries.Where(IsBuildAsset))
            {
                using var stream = entry.Open();
                var document = XDocument.Load(stream);

                foreach (var packageReference in document.Descendants("PackageReference"))
                {
                    Assert.Equal("true", packageReference.Attribute("IsImplicitlyDefined")?.Value);
                }
            }
        }
    }

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
            // The Headless SDK defaults TreatWarningsAsErrors=true in Debug; this test asserts MA0047
            // surfaces as a *warning* (proving the bundled analyzer editorconfig was imported), so keep
            // warnings non-fatal here -- the warnings-as-error policy is covered by its own tests.
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
            // The Headless SDK defaults TreatWarningsAsErrors=true in Debug; this test asserts CA2007
            // surfaces as a *warning* (proving the opt-in enforcement editorconfig was imported), so keep
            // warnings non-fatal here -- the warnings-as-error policy is covered by its own tests.
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
        Assert.Equal("true", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.Contains("ConsumerProject.Tests.Unit", properties["InternalsVisibleTo"], StringComparison.Ordinal);
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
    public async Task should_treat_warnings_as_errors_when_on_continuous_integration()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:CI=true");

        Assert.Equal("true", properties["MSBuildTreatWarningsAsErrors"]);
        Assert.Equal("true", properties["RestoreLockedMode"]);
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
    public async Task should_infer_target_framework_when_explicitly_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: null,
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:HeadlessInferTargetFramework=true");

        Assert.StartsWith("net", properties["TargetFramework"], StringComparison.Ordinal);
        Assert.NotEqual("net", properties["TargetFramework"]);
    }

    [Fact]
    public async Task should_include_target_framework_gated_global_usings_when_building()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
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

    [Fact]
    public async Task should_set_test_project_properties_when_using_test_project_type_sdk()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("Headless.NET.Sdk.Test", properties["HeadlessSdkName"]);
        Assert.Equal("Test", properties["HeadlessSdkProjectType"]);
        Assert.Equal("true", properties["IsTestableProject"]);
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
                ["UseMicrosoftTestingPlatform"] = "true",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["IsTestHarnessProject"]);
        Assert.Equal("true", properties["IsTestableProject"]);
        Assert.Equal("false", properties["IsTestProject"]);
        Assert.Equal("false", properties["IsTestingPlatformApplication"]);
        Assert.Equal("true", properties["GenerateRuntimeConfigurationFiles"]);
        Assert.Equal("false", properties["IsPackable"]);
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

        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Microsoft.Testing.Extensions.TrxReport", packageReferences);
        Assert.DoesNotContain("Microsoft.NET.Test.Sdk", packageReferences);
        Assert.Contains("--report-trx", properties["TestingPlatformCommandLineArguments"], StringComparison.Ordinal);
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

        // With coverage enabled: add --coverage plus the MTP --coverage-settings flag
        // (not the VSTest --settings option, which the MTP runner rejects as unknown).
        var withCoverage = await project.EvaluateHeadlessPropertiesAsync("-p:EnableCodeCoverage=true");
        var coverageArgs = withCoverage["TestingPlatformCommandLineArguments"];
        Assert.Contains("--coverage", coverageArgs, StringComparison.Ordinal);
        Assert.Contains("--coverage-settings", coverageArgs, StringComparison.Ordinal);
        Assert.Contains("default.runsettings", coverageArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_not_reference_github_actions_logger_when_mtp_is_used_on_github_actions()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UseMicrosoftTestingPlatform"] = "true",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:GITHUB_ACTIONS=true");
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("Microsoft.Testing.Extensions.TrxReport", packageReferences);
        Assert.DoesNotContain("Microsoft.NET.Test.Sdk", packageReferences);
        Assert.DoesNotContain("GitHubActionsTestLogger", packageReferences);
    }

    [Fact]
    public async Task should_reference_github_actions_logger_for_vstest_on_github_actions()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UseMicrosoftTestingPlatform"] = "false",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:GITHUB_ACTIONS=true");
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("Microsoft.NET.Test.Sdk", packageReferences);
        Assert.DoesNotContain("Microsoft.Testing.Extensions.TrxReport", packageReferences);
        Assert.Contains("GitHubActionsTestLogger", packageReferences);
        Assert.Contains("GitHubActions", properties["VSTestLogger"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_reference_github_actions_logger_for_vstest_without_github_actions()
    {
        // The PackageReference is unconditional with respect to GITHUB_ACTIONS so the restore
        // graph (and consumer lock files) never depend on CI environment variables. Only the
        // VSTestLogger activation is CI-gated.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UseMicrosoftTestingPlatform"] = "false",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("GitHubActionsTestLogger", packageReferences);
        Assert.DoesNotContain("GitHubActions", properties["VSTestLogger"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_produce_identical_lock_files_when_restoring_with_and_without_github_actions()
    {
        // Regression guard for CI-dependent restore graphs: GitHubActionsTestLogger used to be
        // referenced only when GITHUB_ACTIONS=true, so lock files committed from CI restores were
        // rewritten by every local restore (recurring dirty packages.lock.json noise).
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UseMicrosoftTestingPlatform"] = "false",
                // SupportSbom.targets auto-adds Microsoft.Sbom.Targets on CI (a deliberate,
                // pre-existing CI gate outside this test's scope); pin it off so the comparison
                // isolates the GitHubActionsTestLogger restore-graph behavior.
                ["GenerateSBOM"] = "false",
            }
        );

        var lockFilePath = Path.Combine(project.RootDirectory, "packages.lock.json");
        const string RestoreArguments = "-p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=false";

        var ciResult = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:GITHUB_ACTIONS=true {RestoreArguments} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(ciResult.ExitCode == 0, ciResult.Output);
        var ciLockFile = await File.ReadAllTextAsync(lockFilePath, TestContext.Current.CancellationToken);

        // --force so the second restore re-evaluates the graph instead of no-opping on the
        // cached assets, which would leave a stale lock file behind and make the test vacuous.
        var localResult = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} --force {RestoreArguments} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(localResult.ExitCode == 0, localResult.Output);
        var localLockFile = await File.ReadAllTextAsync(lockFilePath, TestContext.Current.CancellationToken);

        Assert.Contains("GitHubActionsTestLogger", ciLockFile, StringComparison.Ordinal);
        Assert.Contains("GitHubActionsTestLogger", localLockFile, StringComparison.Ordinal);
        Assert.Equal(ciLockFile, localLockFile);
    }

    [Theory]
    [InlineData("Headless.NET.Sdk.Web", "Microsoft.NET.Sdk.Web", "Web", "false", "false")]
    [InlineData("Headless.NET.Sdk.Test", "Microsoft.NET.Sdk", "Test", "true", "true")]
    [InlineData("Headless.NET.Sdk.Razor", "Microsoft.NET.Sdk.Razor", "Razor", "false", "false")]
    [InlineData(
        "Headless.NET.Sdk.BlazorWebAssembly",
        "Microsoft.NET.Sdk.BlazorWebAssembly",
        "BlazorWebAssembly",
        "false",
        "false"
    )]
    [InlineData(
        "Headless.NET.Sdk.WindowsDesktop",
        "Microsoft.NET.Sdk.WindowsDesktop",
        "WindowsDesktop",
        "false",
        "false"
    )]
    public async Task should_set_project_type_properties_when_using_project_type_package_reference(
        string packageId,
        string sdkName,
        string projectType,
        string isTestableProject,
        string isTestProject
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: sdkName,
            packageReferenceId: packageId
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(packageId, properties["HeadlessSdkName"]);
        Assert.Equal(projectType, properties["HeadlessSdkProjectType"]);
        Assert.Equal(isTestableProject, properties["IsTestableProject"]);
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
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(sdkName, properties["HeadlessSdkName"]);
        Assert.Equal(projectType, properties["HeadlessSdkProjectType"]);
        Assert.Equal("false", properties["IsTestableProject"]);
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
                ["Directory.Packages.props"] = $$"""
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
                  </PropertyGroup>
                  <ItemGroup>
                    {{packageVersionItems}}
                  </ItemGroup>
                </Project>
                """,
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("NU1008", result.Output, StringComparison.Ordinal);
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
    public async Task should_include_single_file_editorconfig_when_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:HeadlessSingleFileApp=true");

        Assert.Equal("true", properties["HeadlessSingleFileApp"]);
        Assert.Contains(
            "Headless.NET.Sdk.SingleFileApp.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
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
    public async Task should_disable_bundled_analyzer_editorconfig_when_explicitly_disabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:DisableSupportAnalyzerEditorConfigs=true");

        Assert.DoesNotContain(
            "Headless.NET.Sdk.Analyzers.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task should_include_readme_license_and_third_party_notices_when_packed()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string>
            {
                ["ReadMe.md"] = "# Consumer readme",
                ["LICENSE.txt"] = "MIT",
                ["THIRD-PARTY-NOTICES.TXT"] = "Notices",
            }
        );

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -c Release -o {Quote(project.PackagesDirectory)} -p:PackageVersion=1.2.3 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        using var package = ZipFile.OpenRead(Path.Combine(project.PackagesDirectory, "ConsumerProject.1.2.3.nupkg"));
        Assert.NotNull(package.GetEntry("README.md"));
        Assert.NotNull(package.GetEntry("LICENSE.txt"));
        Assert.NotNull(package.GetEntry("THIRD-PARTY-NOTICES.TXT"));

        var nuspec = ReadPackageEntry(package, "ConsumerProject.nuspec");
        Assert.Contains("<license type=\"file\">LICENSE.txt</license>", nuspec, StringComparison.Ordinal);
        Assert.DoesNotContain("<license type=\"expression\">MIT</license>", nuspec, StringComparison.Ordinal);
        Assert.Contains("<readme>README.md</readme>", nuspec, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_prefix_package_tags_with_author_tag_when_not_a_test_project()
    {
        // SupportPackageInformation.props prepends "xshaheen;" to PackageTags for non-test projects.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.StartsWith("xshaheen", properties["PackageTags"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_set_package_information_defaults_when_not_a_test_project()
    {
        // SupportPackageInformation defaults publish/symbol metadata for packable projects. The
        // symbols policy defaults to embedded PDBs (works on feeds without a symbol server, such
        // as GitHub Packages) with no symbol package.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["PublishRepositoryUrl"]);
        Assert.Equal("git", properties["RepositoryType"]);
        Assert.Equal("embedded", properties["HeadlessSymbolFormat"]);
        Assert.Equal("embedded", properties["DebugType"]);
        Assert.Equal("false", properties["IncludeSymbols"]);
    }

    [Fact]
    public async Task should_use_snupkg_symbol_packages_when_symbol_format_is_snupkg()
    {
        // HeadlessSymbolFormat=snupkg restores the previous SDK behavior: portable PDB shipped
        // as a .snupkg symbol package; DebugType is left at the base SDK default.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessSymbolFormat"] = "snupkg",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("snupkg", properties["HeadlessSymbolFormat"]);
        Assert.Equal("portable", properties["DebugType"]);
        Assert.Equal("true", properties["IncludeSymbols"]);
        Assert.Equal("snupkg", properties["SymbolPackageFormat"]);
    }

    [Fact]
    public async Task should_leave_debug_type_untouched_when_symbol_format_is_none()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessSymbolFormat"] = "none",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("none", properties["HeadlessSymbolFormat"]);
        Assert.Equal("portable", properties["DebugType"]);
        Assert.Equal("false", properties["IncludeSymbols"]);
    }

    [Fact]
    public async Task should_respect_consumer_debug_type_when_symbol_format_is_embedded()
    {
        // A consumer-set DebugType always wins over the embedded default.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["DebugType"] = "full" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("embedded", properties["HeadlessSymbolFormat"]);
        Assert.Equal("full", properties["DebugType"]);
        Assert.Equal("false", properties["IncludeSymbols"]);
    }

    [Fact]
    public async Task should_respect_consumer_include_symbols_when_symbol_format_is_embedded()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["IncludeSymbols"] = "true" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("embedded", properties["DebugType"]);
        Assert.Equal("true", properties["IncludeSymbols"]);
    }

    [Fact]
    public async Task should_respect_consumer_symbol_package_format_when_symbol_format_is_snupkg()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessSymbolFormat"] = "snupkg",
                ["SymbolPackageFormat"] = "symbols.nupkg",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["IncludeSymbols"]);
        Assert.Equal("symbols.nupkg", properties["SymbolPackageFormat"]);
    }

    [Fact]
    public async Task should_not_fight_analyzer_packaging_pattern_when_using_default_symbol_format()
    {
        // Analyzer/source-generator packages set IncludeBuildOutput=false + IncludeSymbols=false;
        // the symbols policy must not overwrite either.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IncludeBuildOutput"] = "false",
                ["IncludeSymbols"] = "false",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("embedded", properties["HeadlessSymbolFormat"]);
        Assert.Equal("false", properties["IncludeSymbols"]);
    }

    [Theory]
    [InlineData("Headless.NET.Sdk.BlazorWebAssembly/{version}", false)]
    [InlineData("Microsoft.NET.Sdk.BlazorWebAssembly", true)]
    public async Task should_default_symbol_format_to_none_when_project_is_blazor_web_assembly(
        string sdkTemplate,
        bool includePackageReference
    )
    {
        // Blazor WASM ships its assemblies to the browser and an embedded PDB survives into the
        // published _framework payload, so the symbols policy defaults those projects to 'none'
        // (portable PDBs are excluded from the payload). Detection uses the base SDK's
        // UsingMicrosoftNETSdkBlazorWebAssembly, so both consumption modes get the exception.
        var sdk = sdkTemplate.Replace("{version}", fixture.PackageVersion, StringComparison.Ordinal);

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: sdk,
            includePackageReference: includePackageReference
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("none", properties["HeadlessSymbolFormat"]);
        Assert.Equal("portable", properties["DebugType"]);
        Assert.Equal("false", properties["IncludeSymbols"]);
    }

    [Fact]
    public async Task should_not_apply_symbol_policy_when_project_is_a_test_project()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(string.Empty, properties["HeadlessSymbolFormat"]);
        Assert.Equal("portable", properties["DebugType"]);
        Assert.Equal(string.Empty, properties["IncludeSymbols"]);
    }

    [Fact]
    public async Task should_pack_embedded_pdb_without_symbol_packages_when_using_defaults()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -c Release -o {Quote(project.PackagesDirectory)} -p:PackageVersion=1.2.3 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(Directory.EnumerateFiles(project.PackagesDirectory, "*.snupkg", SearchOption.TopDirectoryOnly));

        using var package = ZipFile.OpenRead(Path.Combine(project.PackagesDirectory, "ConsumerProject.1.2.3.nupkg"));
        Assert.DoesNotContain(
            package.Entries,
            entry => entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
        );

        var assembly = ReadPackageEntryBytes(package, "lib/net8.0/ConsumerProject.dll");
        Assert.True(
            HasEmbeddedPortablePdb(assembly),
            "Expected the packed assembly to contain an embedded (MPDB) portable PDB."
        );
    }

    [Fact]
    public async Task should_pack_symbol_package_pair_when_symbol_format_is_snupkg()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessSymbolFormat"] = "snupkg",
            }
        );

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -c Release -o {Quote(project.PackagesDirectory)} -p:PackageVersion=1.2.3 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var symbolPackagePath = Path.Combine(project.PackagesDirectory, "ConsumerProject.1.2.3.snupkg");
        Assert.True(File.Exists(symbolPackagePath), "Expected a .snupkg symbol package next to the nupkg.");

        using var package = ZipFile.OpenRead(Path.Combine(project.PackagesDirectory, "ConsumerProject.1.2.3.nupkg"));
        var assembly = ReadPackageEntryBytes(package, "lib/net8.0/ConsumerProject.dll");
        Assert.False(HasEmbeddedPortablePdb(assembly), "snupkg mode must keep the PDB out of the assembly.");

        using var symbolPackage = ZipFile.OpenRead(symbolPackagePath);
        Assert.Contains(
            symbolPackage.Entries,
            entry => entry.FullName.EndsWith("ConsumerProject.pdb", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task should_derive_copyright_from_company_when_not_explicitly_set()
    {
        // SupportCopyright.targets builds Copyright as "Copyright © <Company> <year>" when unset.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["Company"] = "Headless Contoso" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        var currentYear = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        Assert.Equal($"Copyright © Headless Contoso {currentYear}", properties["Copyright"]);
    }

    [Fact]
    public async Task should_not_override_explicit_copyright()
    {
        // SupportCopyright.targets only computes Copyright when it is empty.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Company"] = "Headless Contoso",
                ["Copyright"] = "All rights reserved by Acme",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("All rights reserved by Acme", properties["Copyright"]);
    }

    [Fact]
    public void should_pack_system_runtime_experimental_ref_pack_cleanup_target()
    {
        // SupportSystemRuntimeExperimental.targets has no observable consumer property; it ships a
        // pack-time target that strips the duplicate System.Runtime.dll reference. Assert the asset.
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var content = ReadPackageEntry(package, "build/SupportSystemRuntimeExperimental.targets");

        Assert.Contains("RemoveSystemRuntimeFromRefPack", content, StringComparison.Ordinal);
        Assert.Contains("System.Runtime.Experimental", content, StringComparison.Ordinal);
    }

    [Fact]
    public void should_pack_embed_binlog_targets_for_post_mortem_context()
    {
        // SupportEmbedBinlog.targets exposes no consumer property; it injects EmbedInBinlog items
        // (a no-op outside -bl builds). Assert the shipped asset wires the expected targets.
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var content = ReadPackageEntry(package, "build/SupportEmbedBinlog.targets");

        Assert.Contains("HeadlessEmbedBannedSymbolsInBinLog", content, StringComparison.Ordinal);
        Assert.Contains("HeadlessEmbedEditorConfigInBinLog", content, StringComparison.Ordinal);
        Assert.Contains("EmbedInBinlog", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_add_strict_system_text_json_runtime_options_when_enabled_on_net9()
    {
        // RuntimeHostConfigurationOption.props -- off by default; opt-in adds STJ runtime switches
        // on net9.0+.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net9.0",
            outputType: "Exe"
        );

        var defaults = await project.EvaluateHeadlessPropertiesAsync();
        Assert.DoesNotContain(
            "RespectRequiredConstructorParametersDefault",
            defaults["RuntimeHostConfigurationOptions"],
            StringComparison.Ordinal
        );

        var enabled = await project.EvaluateHeadlessPropertiesAsync(
            "-p:HeadlessEnableStrictSystemTextJsonRuntimeDefaults=true"
        );
        Assert.Contains(
            "System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault=true",
            enabled["RuntimeHostConfigurationOptions"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "System.Text.Json.Serialization.RespectNullableAnnotationsDefault=true",
            enabled["RuntimeHostConfigurationOptions"],
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
            "-p:GITHUB_ACTIONS=true -p:GITHUB_REPOSITORY=xshaheen/headless-sdk"
        );

        Assert.Equal("true", properties["EnableSdkContainerSupport"]);
        Assert.Equal("ghcr.io", properties["ContainerRegistry"]);
        Assert.Equal("xshaheen/headless-sdk", properties["ContainerRepository"]);
    }

    [Fact]
    public void should_reference_sbom_targets_only_when_sbom_generation_enabled()
    {
        // SupportSbom.targets gates a Microsoft.Sbom.Targets PackageReference on GenerateSBOM=true
        // (auto-enabled on CI). The reference is implicitly defined so it is not governed by the
        // consumer's CPM; verify the shipped asset contract directly rather than via a network
        // restore of the SBOM package.
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var content = ReadPackageEntry(package, "build/SupportSbom.targets");
        var document = XDocument.Parse(content);

        var sbomGroup = document
            .Root!.Elements("ItemGroup")
            .Single(group =>
                string.Equals(
                    group.Attribute("Condition")?.Value,
                    "'$(GenerateSBOM)' == 'true'",
                    StringComparison.Ordinal
                )
            );
        var reference = sbomGroup
            .Elements("PackageReference")
            .Single(element =>
                string.Equals(element.Attribute("Include")?.Value, "Microsoft.Sbom.Targets", StringComparison.Ordinal)
            );

        Assert.Equal("true", reference.Attribute("IsImplicitlyDefined")?.Value);
        Assert.Contains("GenerateSBOM", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_not_write_config_files_on_plain_build()
    {
        // A normal build has no side effects: scaffolding only runs via the explicit target.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.False(File.Exists(project.EditorConfigPath), "Plain build must not create .editorconfig.");
        Assert.False(File.Exists(project.CSharpierIgnorePath), "Plain build must not create .csharpierignore.");
        Assert.False(File.Exists(project.GitIgnorePath), "Plain build must not create .gitignore.");
        Assert.False(File.Exists(project.GitAttributesPath), "Plain build must not create .gitattributes.");
    }

    [Fact]
    public async Task should_scaffold_config_files_when_target_invoked()
    {
        // With no selector set, the explicit target scaffolds the full default set.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(project.EditorConfigPath), "Expected the scaffold target to create .editorconfig.");
        Assert.True(
            File.Exists(project.CSharpierIgnorePath),
            "Expected the scaffold target to create .csharpierignore."
        );
        Assert.True(File.Exists(project.GitIgnorePath), "Expected the scaffold target to create .gitignore.");
        Assert.True(File.Exists(project.GitAttributesPath), "Expected the scaffold target to create .gitattributes.");
        Assert.Contains($"Created {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Created {project.CSharpierIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Created {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Created {project.GitAttributesPath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.CSharpierIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.GitAttributesPath}", result.Output, StringComparison.Ordinal);

        var gitignore = await File.ReadAllTextAsync(project.GitIgnorePath, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(gitignore), "Expected a non-empty scaffolded .gitignore.");
    }

    [Fact]
    public async Task should_not_overwrite_existing_file_when_scaffolding()
    {
        const string Sentinel = "# sentinel-user-owned-gitignore\n";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal) { [".gitignore"] = Sentinel }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        // The user's existing file must be preserved verbatim.
        var gitignore = await File.ReadAllTextAsync(project.GitIgnorePath, TestContext.Current.CancellationToken);
        Assert.Equal(NormalizeLineEndings(Sentinel), NormalizeLineEndings(gitignore));
        Assert.Contains($"Skipped {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Created {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);

        // Files that did not pre-exist are still created.
        Assert.True(
            File.Exists(project.EditorConfigPath),
            "Expected the scaffold target to create the absent .editorconfig."
        );
        Assert.Contains($"Created {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_overwrite_existing_file_when_force_enabled()
    {
        const string Sentinel = "# sentinel-user-owned-gitignore\n";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal) { [".gitignore"] = Sentinel }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles --no-incremental -p:HeadlessOverwriteConfigFiles=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        // The sentinel must be replaced with the bundled template content.
        var gitignore = await File.ReadAllTextAsync(project.GitIgnorePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("sentinel-user-owned-gitignore", gitignore, StringComparison.Ordinal);
        Assert.Contains("*.rsuser", gitignore, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

    private static void AssertImplicitAnalyzerReference(
        IReadOnlyDictionary<string, XElement> packageReferences,
        string packageId
    )
    {
        Assert.True(packageReferences.TryGetValue(packageId, out var packageReference), $"Missing {packageId}.");
        Assert.False(string.IsNullOrWhiteSpace(packageReference.Attribute("Version")?.Value));
        Assert.Equal("true", packageReference.Attribute("IsImplicitlyDefined")?.Value);
        Assert.Equal("all", packageReference.Element("PrivateAssets")?.Value);
        Assert.Contains("analyzers", packageReference.Element("IncludeAssets")?.Value, StringComparison.Ordinal);
    }

    private static bool IsBuildAsset(ZipArchiveEntry entry)
    {
        if (
            !entry.FullName.EndsWith(".props", StringComparison.Ordinal)
            && !entry.FullName.EndsWith(".targets", StringComparison.Ordinal)
        )
        {
            return false;
        }

        return entry.FullName.StartsWith("build/", StringComparison.Ordinal)
            || entry.FullName.StartsWith("buildMultiTargeting/", StringComparison.Ordinal)
            || entry.FullName.StartsWith("buildTransitive/", StringComparison.Ordinal);
    }

    private static string ReadPackageEntry(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static byte[] ReadPackageEntryBytes(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // "MPDB" is the magic header of the embedded portable PDB debug-directory blob (ECMA-335
    // Portable PDB spec); its presence in the image is what "DebugType=embedded" produces.
    private static bool HasEmbeddedPortablePdb(byte[] assembly) => assembly.AsSpan().IndexOf("MPDB"u8) >= 0;
}

public sealed class HeadlessSdkPackageFixture : IAsyncLifetime
{
    private readonly Dictionary<string, string> packagePaths = new(StringComparer.Ordinal);

    public string PackageRootDirectory { get; private set; } = null!;

    public string PackagePath { get; private set; } = null!;

    public string PackageSourceDirectory => Path.Combine(PackageRootDirectory, "packages");

    public string PackageVersion { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        PackageRootDirectory = Path.Combine(Path.GetTempPath(), "Headless.NET.Sdk.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PackageSourceDirectory);

        var cancellationToken = TestContext.Current.CancellationToken;
        var repositoryRoot = TestRepository.FindRoot("integration tests");
        var env = DotNetCommandEnvironment.CreateIsolatedEnvironment(PackageRootDirectory);
        var packageIds = new[]
        {
            "Headless.NET.Sdk",
            "Headless.NET.Sdk.Web",
            "Headless.NET.Sdk.Test",
            "Headless.NET.Sdk.Razor",
            "Headless.NET.Sdk.BlazorWebAssembly",
            "Headless.NET.Sdk.WindowsDesktop",
        };

        foreach (var packageId in packageIds)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
            var baseIntermediateOutputPath = EnsureTrailingDirectorySeparator(
                Path.Combine(PackageRootDirectory, "obj", packageId)
            );
            var baseOutputPath = EnsureTrailingDirectorySeparator(Path.Combine(PackageRootDirectory, "bin", packageId));
            var command =
                $"pack {Quote(projectPath)} -c Debug -o {Quote(PackageSourceDirectory)} -p:BaseIntermediateOutputPath={Quote(baseIntermediateOutputPath)} -p:BaseOutputPath={Quote(baseOutputPath)} -p:RestorePackagesWithLockFile=false -p:RestoreLockedMode=false";
            var result = await DotNetCommand.RunAsync(repositoryRoot, command, env, cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to pack {packageId} for integration tests.{Environment.NewLine}{result.Output}"
                );
            }

            var packagePath =
                Directory
                    .EnumerateFiles(PackageSourceDirectory, $"{packageId}.*.nupkg", SearchOption.TopDirectoryOnly)
                    .Where(path => !path.EndsWith(".snupkg", StringComparison.Ordinal))
                    .Where(path => HasVersionSuffix(Path.GetFileName(path), packageId))
                    .OrderByDescending(File.GetCreationTimeUtc)
                    .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Failed to locate packed {packageId} nupkg for integration tests."
                );

            packagePaths[packageId] = packagePath;
        }

        PackagePath = packagePaths["Headless.NET.Sdk"];
        PackageVersion = Path.GetFileNameWithoutExtension(PackagePath)["Headless.NET.Sdk.".Length..];
    }

    public string GetPackagePath(string packageId) => packagePaths[packageId];

    private static bool HasVersionSuffix(string fileName, string packageId)
    {
        var versionStart = packageId.Length + 1;
        return fileName.Length > versionStart && char.IsDigit(fileName[versionStart]);
    }

    private static string EnsureTrailingDirectorySeparator(string path) =>
        $"{Path.TrimEndingDirectorySeparator(path)}{Path.DirectorySeparatorChar}";

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(PackageRootDirectory))
            {
                Directory.Delete(PackageRootDirectory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[fixture] Failed to delete '{PackageRootDirectory}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[fixture] Failed to delete '{PackageRootDirectory}': {ex.Message}");
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class ConsumerProject : IAsyncDisposable
{
    private const string ProjectName = "ConsumerProject";

    private readonly Dictionary<string, string> environment;
    private readonly string packageSourceDirectory;

    private ConsumerProject(string rootDirectory, string packageVersion, string packageSourceDirectory)
    {
        RootDirectory = rootDirectory;
        ProjectFilePath = Path.Combine(rootDirectory, $"{ProjectName}.csproj");
        NuGetConfigPath = Path.Combine(rootDirectory, "NuGet.Config");
        EditorConfigPath = Path.Combine(rootDirectory, ".editorconfig");
        PackagesDirectory = Path.Combine(rootDirectory, "packages");
        SolutionDirectory = $"{Path.TrimEndingDirectorySeparator(rootDirectory)}{Path.DirectorySeparatorChar}";
        PackageVersion = packageVersion;
        this.packageSourceDirectory = packageSourceDirectory;
        environment = DotNetCommandEnvironment.CreateIsolatedEnvironment(rootDirectory);
    }

    public string EditorConfigPath { get; }

    public string CSharpierIgnorePath => Path.Combine(RootDirectory, ".csharpierignore");

    public string BinLogPath => Path.Combine(RootDirectory, "msbuild.binlog");

    public string BuildOutputSarifPath => Path.Combine(RootDirectory, "BuildOutput.sarif");

    public string GitAttributesPath => Path.Combine(RootDirectory, ".gitattributes");

    public string GitIgnorePath => Path.Combine(RootDirectory, ".gitignore");

    public string NuGetConfigPath { get; }

    public string PackageVersion { get; }

    public string PackagesDirectory { get; }

    public string ProjectAssetsPath => Path.Combine(RootDirectory, "obj", "project.assets.json");

    public string ProjectFilePath { get; }

    public string RootDirectory { get; }

    public string SolutionDirectory { get; }

    public static async Task<ConsumerProject> CreateAsync(
        string packageVersion,
        string packageSourceDirectory,
        string sdk = "Microsoft.NET.Sdk",
        string? targetFramework = "net8.0",
        string? outputType = null,
        string? editorConfigContent = null,
        bool enableEditorConfigCopy = false,
        bool enableDefaultConfigFilesCopy = false,
        string? warningsAsErrors = null,
        bool includePackageReference = true,
        string packageReferenceId = "Headless.NET.Sdk",
        bool useCentralPackageManagement = false,
        IReadOnlyDictionary<string, string>? extraProperties = null,
        IReadOnlyDictionary<string, string>? extraPackageReferences = null,
        IReadOnlyDictionary<string, string>? additionalFiles = null
    )
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "Headless.NET.Sdk.Consumer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        var project = new ConsumerProject(rootDirectory, packageVersion, packageSourceDirectory);
        var cancellationToken = TestContext.Current.CancellationToken;

        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            project.CreateProjectFile(
                sdk,
                targetFramework,
                outputType,
                enableEditorConfigCopy,
                enableDefaultConfigFilesCopy,
                warningsAsErrors,
                includePackageReference,
                packageReferenceId,
                useCentralPackageManagement,
                extraProperties,
                extraPackageReferences
            ),
            Encoding.UTF8,
            cancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(rootDirectory, "Class1.cs"),
            """
namespace ConsumerProject;

public sealed class Class1;
""",
            Encoding.UTF8,
            cancellationToken
        );
        await File.WriteAllTextAsync(
            project.NuGetConfigPath,
            project.CreateNuGetConfig(),
            Encoding.UTF8,
            cancellationToken
        );

        if (editorConfigContent is not null)
        {
            await File.WriteAllTextAsync(
                project.EditorConfigPath,
                editorConfigContent,
                Encoding.UTF8,
                cancellationToken
            );
        }

        if (additionalFiles is not null)
        {
            foreach (var (relativePath, content) in additionalFiles)
            {
                var filePath = Path.Combine(rootDirectory, relativePath);
                var directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
            }
        }

        return project;
    }

    public Task<DotNetCommandResult> RunDotNetAsync(string arguments) =>
        DotNetCommand.RunAsync(RootDirectory, arguments, environment, TestContext.Current.CancellationToken);

    public async Task<BuildDiagnosticsResult> BuildAndCollectDiagnosticsAsync(string additionalArguments = "")
    {
        if (File.Exists(BinLogPath))
        {
            File.Delete(BinLogPath);
        }

        if (File.Exists(BuildOutputSarifPath))
        {
            File.Delete(BuildOutputSarifPath);
        }

        var argumentsSuffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments}";
        var result = await RunDotNetAsync(
            $"build {Quote(ProjectFilePath)}{argumentsSuffix} -p:ErrorLog={Quote($"{BuildOutputSarifPath},version=2.1")} /bl:{Quote(BinLogPath)}"
        );
        var binLogFiles = File.Exists(BinLogPath) ? ReadBinLogFiles(BinLogPath) : [];
        var sarif = await SarifFile.LoadAsync(BuildOutputSarifPath);

        return new BuildDiagnosticsResult(result.ExitCode, result.Output, binLogFiles, sarif);
    }

    public async Task<Dictionary<string, string>> EvaluateHeadlessPropertiesAsync(string additionalArguments = "")
    {
        var argumentsSuffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments}";
        var result = await RunDotNetAsync(
            $"msbuild {Quote(ProjectFilePath)} /restore /t:WriteHeadlessProperties -p:RestoreConfigFile={Quote(NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -v:q -nologo{argumentsSuffix}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertiesPath = Path.Combine(RootDirectory, "headless-properties.txt");

        foreach (var line in await File.ReadAllLinesAsync(propertiesPath, TestContext.Current.CancellationToken))
        {
            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            Assert.True(separatorIndex > 0, $"Invalid property output line: {line}");
            properties[line[..separatorIndex]] = line[(separatorIndex + 1)..];
        }

        return properties;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private string CreateNuGetConfig()
    {
        return $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{{packageSourceDirectory}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;
    }

    private string CreateProjectFile(
        string sdk,
        string? targetFramework,
        string? outputType,
        bool enableEditorConfigCopy,
        bool enableDefaultConfigFilesCopy,
        string? warningsAsErrors,
        bool includePackageReference,
        string packageReferenceId,
        bool useCentralPackageManagement,
        IReadOnlyDictionary<string, string>? extraProperties,
        IReadOnlyDictionary<string, string>? extraPackageReferences
    )
    {
        var propertyLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            propertyLines.Add($"    <TargetFramework>{targetFramework}</TargetFramework>");
        }

        if (!string.IsNullOrWhiteSpace(outputType))
        {
            propertyLines.Add($"    <OutputType>{outputType}</OutputType>");
        }

        if (enableDefaultConfigFilesCopy)
        {
            propertyLines.Add(
                "    <HeadlessCopyDefaultConfigFilesToSolutionDir>true</HeadlessCopyDefaultConfigFilesToSolutionDir>"
            );
        }

        if (enableEditorConfigCopy)
        {
            propertyLines.Add(
                "    <HeadlessCopyEditorConfigToSolutionDir>true</HeadlessCopyEditorConfigToSolutionDir>"
            );
        }

        if (!string.IsNullOrWhiteSpace(warningsAsErrors))
        {
            propertyLines.Add($"    <WarningsAsErrors>{warningsAsErrors}</WarningsAsErrors>");
        }

        if (extraProperties is not null)
        {
            foreach (var (name, value) in extraProperties)
            {
                propertyLines.Add($"    <{name}>{value}</{name}>");
            }
        }

        var extraPropertyBlock =
            propertyLines.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}{string.Join(Environment.NewLine, propertyLines)}";

        var packageReferenceLines = new List<string>();

        if (includePackageReference)
        {
            var versionAttribute = useCentralPackageManagement ? string.Empty : $@" Version=""{PackageVersion}""";
            packageReferenceLines.Add(
                $@"    <PackageReference Include=""{packageReferenceId}""{versionAttribute} PrivateAssets=""all"" />"
            );
        }

        if (extraPackageReferences is not null)
        {
            foreach (var (name, version) in extraPackageReferences)
            {
                packageReferenceLines.Add($@"    <PackageReference Include=""{name}"" Version=""{version}"" />");
            }
        }

        var packageReferenceBlock =
            packageReferenceLines.Count == 0
                ? string.Empty
                : $"""

                      <ItemGroup>
                    {string.Join(Environment.NewLine, packageReferenceLines)}
                      </ItemGroup>
                    """;

        return $$"""
            <Project Sdk="{{sdk}}">
              <PropertyGroup>
                <Nullable>enable</Nullable>{{extraPropertyBlock}}
              </PropertyGroup>{{packageReferenceBlock}}

              <Target Name="WriteHeadlessProperties">
                <PropertyGroup>
                  <_HeadlessEvaluatedEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessEvaluatedEditorConfigFiles>
                  <_HeadlessEvaluatedNoneItems>@(None->'%(Identity)', '|')</_HeadlessEvaluatedNoneItems>
                  <_HeadlessEvaluatedPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessEvaluatedPackageReferences>
                  <_HeadlessEvaluatedRuntimeHostOptions>@(RuntimeHostConfigurationOption->'%(Identity)=%(Value)', '|')</_HeadlessEvaluatedRuntimeHostOptions>
                  <_HeadlessEvaluatedInternalsVisibleTo>@(InternalsVisibleTo, '|')</_HeadlessEvaluatedInternalsVisibleTo>
                  <_HeadlessEvaluatedVSTestLogger>$(VSTestLogger.Replace(';', '|'))</_HeadlessEvaluatedVSTestLogger>
                </PropertyGroup>
                <ItemGroup>
                  <_HeadlessEvaluatedNoWarnItems Include="$(NoWarn)" />
                </ItemGroup>
                <PropertyGroup>
                  <_HeadlessEvaluatedNoWarn>@(_HeadlessEvaluatedNoWarnItems, '|')</_HeadlessEvaluatedNoWarn>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/headless-properties.txt"
                  Lines="TargetFramework=$(TargetFramework);RollForward=$(RollForward);PackAsTool=$(PackAsTool);HeadlessSdkName=$(HeadlessSdkName);HeadlessSdkProjectType=$(HeadlessSdkProjectType);HeadlessSingleFileApp=$(HeadlessSingleFileApp);IsTestHarnessProject=$(IsTestHarnessProject);IsTestableProject=$(IsTestableProject);IsTestProject=$(IsTestProject);IsTestingPlatformApplication=$(IsTestingPlatformApplication);GenerateRuntimeConfigurationFiles=$(GenerateRuntimeConfigurationFiles);IsPackable=$(IsPackable);NoWarn=$(_HeadlessEvaluatedNoWarn);EditorConfigFiles=$(_HeadlessEvaluatedEditorConfigFiles);NoneItems=$(_HeadlessEvaluatedNoneItems);PackageReferences=$(_HeadlessEvaluatedPackageReferences);VSTestSetting=$(VSTestSetting);MSBuildTreatWarningsAsErrors=$(MSBuildTreatWarningsAsErrors);RestoreLockedMode=$(RestoreLockedMode);HeadlessEmitInternalsVisibleToAttributes=$(HeadlessEmitInternalsVisibleToAttributes);InternalsVisibleTo=$(_HeadlessEvaluatedInternalsVisibleTo);TestingPlatformCommandLineArguments=$(TestingPlatformCommandLineArguments);PackageTags=$(PackageTags);PublishRepositoryUrl=$(PublishRepositoryUrl);RepositoryType=$(RepositoryType);IncludeSymbols=$(IncludeSymbols);SymbolPackageFormat=$(SymbolPackageFormat);DebugType=$(DebugType);HeadlessSymbolFormat=$(HeadlessSymbolFormat);VSTestLogger=$(_HeadlessEvaluatedVSTestLogger);Copyright=$(Copyright);RuntimeHostConfigurationOptions=$(_HeadlessEvaluatedRuntimeHostOptions);EnableSdkContainerSupport=$(EnableSdkContainerSupport);ContainerRegistry=$(ContainerRegistry);ContainerRepository=$(ContainerRepository)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """;
    }

    private static IReadOnlyCollection<string> ReadBinLogFiles(string path)
    {
        using var stream = File.OpenRead(path);
        var build = StructuredLoggerSerialization.ReadBinLog(stream);

        return [.. build.SourceFiles.Select(file => file.FullPath)];
    }
}

internal static class TestRepository
{
    public static string FindRoot(string purpose)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "headless-sdk.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root for {purpose}.");
    }
}

internal static class DotNetCommandEnvironment
{
    public static Dictionary<string, string> CreateIsolatedEnvironment(string tempRoot)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_HOME"] = Path.Combine(tempRoot, "dotnet-cli-home"),
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["NUGET_PACKAGES"] = Path.Combine(tempRoot, ".nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(tempRoot, ".nuget-http-cache"),
        };

        foreach (var value in environment.Values)
        {
            Directory.CreateDirectory(value);
        }

        AddNeutralBuildEnvironment(environment);

        return environment;
    }

    public static void AddNeutralBuildEnvironment(IDictionary<string, string> environment)
    {
        // Workflow-level environment variables should not change default consumer-project behavior.
        environment["CONFIGURATION"] = "Debug";
        environment["CI"] = "false";
        environment["TF_BUILD"] = "false";
        environment["GITHUB_ACTIONS"] = "false";
        environment["GITLAB_CI"] = "false";
        environment["TEAMCITY_VERSION"] = string.Empty;
        environment["BUILD_COMMAND"] = string.Empty;
        environment["APPVEYOR"] = string.Empty;
        environment["TRAVIS"] = "false";
        environment["CIRCLECI"] = "false";
        environment["CODEBUILD_BUILD_ID"] = string.Empty;
        environment["AWS_REGION"] = string.Empty;
        environment["JENKINS_URL"] = string.Empty;
        environment["BUILD_ID"] = string.Empty;
        environment["BUILD_URL"] = string.Empty;
        environment["PROJECT_ID"] = string.Empty;
        environment["JB_SPACE_API_URL"] = string.Empty;
    }
}

internal sealed record BuildDiagnosticsResult(
    int ExitCode,
    string Output,
    IReadOnlyCollection<string> BinLogFiles,
    SarifFile Sarif
)
{
    public string SarifSummary =>
        string.Join(Environment.NewLine, Sarif.AllResults().Select(result => result.ToString()));

    public bool HasWarning(string ruleId) =>
        Sarif
            .AllResults()
            .Any(result =>
                string.Equals(result.Level, "warning", StringComparison.Ordinal)
                && string.Equals(result.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)
            );

    public IReadOnlyCollection<string> GetBinLogFiles() => BinLogFiles;
}

internal sealed class SarifFile
{
    [JsonPropertyName("runs")]
    public SarifRun[] Runs { get; init; } = [];

    public static async Task<SarifFile> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new SarifFile();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SarifFile>(stream) ?? new SarifFile();
    }

    public IEnumerable<SarifResult> AllResults() => Runs.SelectMany(run => run.Results);
}

internal sealed class SarifRun
{
    [JsonPropertyName("results")]
    public SarifResult[] Results { get; init; } = [];
}

internal sealed class SarifResult
{
    [JsonPropertyName("level")]
    public string? Level { get; init; }

    [JsonPropertyName("message")]
    public JsonElement Message { get; init; }

    [JsonPropertyName("ruleId")]
    public string? RuleId { get; init; }

    public override string ToString() => $"{Level}: {RuleId}: {GetMessageText()}";

    private string? GetMessageText()
    {
        if (Message.ValueKind == JsonValueKind.String)
        {
            return Message.GetString();
        }

        if (
            Message.ValueKind == JsonValueKind.Object
            && Message.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
        )
        {
            return text.GetString();
        }

        return Message.ValueKind == JsonValueKind.Undefined ? null : Message.ToString();
    }
}

internal static class DotNetCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        string arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo("dotnet", AddDisableBuildServersArgument(arguments))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILDNOINPROCNODE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                // A token already fired (caller cancellation or our timeout); wait unconditionally for the kill to settle.
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (InvalidOperationException) { }

            // Distinguish caller cancellation from the internal timeout: if the caller's token fired,
            // propagate real cancellation semantics rather than masking it as a command timeout.
            cancellationToken.ThrowIfCancellationRequested();

            var timeoutOutput = $"{await standardOutputTask}{await standardErrorTask}".Trim();
            throw new TimeoutException(
                $"dotnet {arguments} timed out after {Timeout}.{Environment.NewLine}{timeoutOutput}"
            );
        }

        var output = $"{await standardOutputTask}{await standardErrorTask}";
        return new DotNetCommandResult(process.ExitCode, output.Trim());
    }

    private static string AddDisableBuildServersArgument(string arguments)
    {
        if (arguments.Contains("--disable-build-servers", StringComparison.Ordinal))
        {
            return arguments;
        }

        var separatorIndex = arguments.IndexOf(' ', StringComparison.Ordinal);
        return separatorIndex < 0
            ? $"{arguments} --disable-build-servers"
            : $"{arguments[..separatorIndex]} --disable-build-servers{arguments[separatorIndex..]}";
    }

    public static string Quote(string value) => $"\"{value}\"";
}

internal sealed record DotNetCommandResult(int ExitCode, string Output);
