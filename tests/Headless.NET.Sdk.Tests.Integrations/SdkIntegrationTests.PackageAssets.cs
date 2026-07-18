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
        // The test-project NoWarn lives in SupportGeneral.targets (not .props) so consumer-set
        // IsTestProject / IsTestHarnessProject values are visible under MSBuild SDK consumption,
        // where build props load before Directory.Build.props.
        var content = ReadPackageEntry(package, "build/SupportGeneral.targets");
        var document = XDocument.Parse(content);
        var testNoWarn = document
            .Root!.Elements("PropertyGroup")
            .Elements("NoWarn")
            .Single(element =>
                string.Equals(
                    element.Attribute("Condition")?.Value,
                    "'$(IsTestProject)' == 'true' or '$(IsTestHarnessProject)' == 'true'",
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
        // MTP coverage settings use the platform's --coverage-settings argument.
        Assert.Contains("--coverage-settings", testTargets, StringComparison.Ordinal);

        var runsettings = ReadPackageEntry(package, "configurations/default.runsettings");
        Assert.Contains("<TreatNoTestsAsError>true</TreatNoTestsAsError>", runsettings, StringComparison.Ordinal);
        Assert.Contains(@".*\.Tests\.[^.]+\.dll$", runsettings, StringComparison.Ordinal);
        Assert.Contains(@".*\.Testing\.[^.]+\.dll$", runsettings, StringComparison.Ordinal);
        Assert.Contains(@".*\.g\.cs$", runsettings, StringComparison.Ordinal);
        Assert.Contains(@"^.*\.Migrations\..*$", runsettings, StringComparison.Ordinal);

        var generalTargets = ReadPackageEntry(package, "build/SupportGeneral.targets");
        Assert.Contains("DisableDocumentationWarnings", generalTargets, StringComparison.Ordinal);
        Assert.Contains("CS1573;CS1591", generalTargets, StringComparison.Ordinal);

        var mandatoryAnalyzers = ReadPackageEntry(package, "build/SupportMandatoryAnalyzers.targets");
        Assert.Contains("Headless.NET.Sdk.Tests.editorconfig", mandatoryAnalyzers, StringComparison.Ordinal);
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
    public void should_pack_single_file_and_sdk_metadata_support_without_target_framework_inference()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        Assert.NotNull(package.GetEntry("build/SupportSingleFileApp.props"));
        Assert.Null(package.GetEntry("build/SupportTargetFrameworkInference.props"));
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
#pragma warning disable CA2000 // Dispose objects before losing scope
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
#pragma warning restore CA2000

            Assert.NotNull(package.GetEntry("sdk/Sdk.props"));
            Assert.NotNull(package.GetEntry("sdk/Sdk.targets"));
            Assert.NotNull(package.GetEntry($"build/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"build/{packageId}.targets"));
            Assert.NotNull(package.GetEntry($"buildMultiTargeting/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"buildMultiTargeting/{packageId}.targets"));
            Assert.Null(package.GetEntry($"buildTransitive/{packageId}.props"));
            Assert.Null(package.GetEntry($"buildTransitive/{packageId}.targets"));
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
            Assert.Contains($"<HeadlessSdkName>{packageId}</HeadlessSdkName>", buildProps, StringComparison.Ordinal);
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
                    if (packageReference.Attribute("Include") is not null)
                    {
                        Assert.Equal("true", packageReference.Attribute("IsImplicitlyDefined")?.Value);
                    }
                    else
                    {
                        Assert.True(
                            packageReference.Attribute("Update") is not null
                                || packageReference.Attribute("Remove") is not null
                        );
                    }
                }
            }
        }
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
        // SupportPackageInformation.targets prepends "xshaheen;" to PackageTags for non-test projects.
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
    public async Task should_infer_repository_branch_after_package_information_defaults_on_github_actions()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync(
            "-p:GITHUB_ACTIONS=true -p:GITHUB_REF=refs/pull/26/merge"
        );

        Assert.Equal("true", properties["PublishRepositoryUrl"]);
        Assert.Equal("pr26", properties["RepositoryBranch"]);
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

        var assembly = ReadPackageEntryBytes(package, "lib/net10.0/ConsumerProject.dll");
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
        var assembly = ReadPackageEntryBytes(package, "lib/net10.0/ConsumerProject.dll");
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
    public async Task should_not_enable_sbom_generation_on_continuous_integration_by_default()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:CI=true");

        Assert.Equal("false", properties["GenerateSBOM"]);
    }

    [Fact]
    public void should_keep_sbom_generation_opt_in_with_binding_only_in_targets()
    {
        // The restore-visible dependency is injected from props. The targets asset may reinforce
        // metadata on that existing item, but it must not add a new restore dependency.
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var document = XDocument.Parse(ReadPackageEntry(package, "build/SupportSbom.targets"));
        var bindingUpdate = Assert.Single(document.Descendants("PackageReference"));
        Assert.Null(bindingUpdate.Attribute("Include"));
        Assert.Equal("Microsoft.Sbom.Targets", bindingUpdate.Attribute("Update")?.Value);
    }
}
