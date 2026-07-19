using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Headless.NET.Sdk.Tests.Integrations;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class PackageContractTests(HeadlessSdkPackageFixture fixture)
{
    private const string PackageDescription =
        "An opinionated MSBuild SDK family for consistent project evaluation, mandatory analyzer infrastructure, configurable banned-API policy, CI quality gates, packaging defaults, and Microsoft Testing Platform support across compatible .NET projects. Distributed through GitHub Packages and NuGet.org; no license is currently granted.";

    private static readonly string[] SharedPackageEntries =
    [
        "_rels/.rels",
        "[Content_Types].xml",
        "build/GlobalUsing.props",
        "build/GlobalUsing.targets",
        "build/Headless.NET.Sdk.props",
        "build/Headless.NET.Sdk.targets",
        "build/RuntimeHostConfigurationOption.props",
        "build/RuntimeHostConfigurationOption.targets",
        "build/SupportAdditionalFiles.targets",
        "build/SupportAnalyzerEditorConfigs.props",
        "build/SupportAnalyzerHygiene.targets",
        "build/SupportAssemblyAttributes.targets",
        "build/SupportBannedSymbols.targets",
        "build/SupportCopyright.targets",
        "build/SupportDetectContinuousIntegration.props",
        "build/SupportDetectContinuousIntegration.targets",
        "build/SupportEmbedBinlog.targets",
        "build/SupportGeneral.props",
        "build/SupportGeneral.targets",
        "build/SupportImplicitAnalyzers.props",
        "build/SupportMandatoryAnalyzers.targets",
        "build/SupportNuGetAudit.targets",
        "build/SupportNuGetWarningPolicy.props",
        "build/SupportPackageInformation.props",
        "build/SupportPackageInformation.targets",
        "build/SupportSbom.props",
        "build/SupportSbom.targets",
        "build/SupportSingleFileApp.props",
        "build/SupportSystemRuntimeExperimental.targets",
        "build/SupportTestProjects.targets",
        "build/SupportTestProjects.Versions.props",
        "build/SupportWebContainer.targets",
        "configurations/BannedSymbols.NewtonsoftJson.txt",
        "configurations/BannedSymbols.txt",
        "configurations/default.runsettings",
        "configurations/editorconfig.txt",
        "configurations/Headless.NET.Sdk.Analyzers.editorconfig",
        "configurations/Headless.NET.Sdk.EnforceConfigureAwait.editorconfig",
        "configurations/Headless.NET.Sdk.SingleFileApp.editorconfig",
        "configurations/Headless.NET.Sdk.Tests.editorconfig",
        "configurations/template.csharpierignore",
        "configurations/template.gitattributes",
        "configurations/template.gitignore",
        "logo.png",
        "package/services/metadata/core-properties/*.psmdcp",
        "README.md",
        "sdk/Sdk.props",
        "sdk/Sdk.targets",
    ];

    private static readonly Dictionary<string, string> BaseDependencySnapshot = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        ["AsyncFixer"] = "[2.1.0]",
        ["Asyncify"] = "[0.9.7]",
        ["ErrorProne.NET.CoreAnalyzers"] = "[0.1.2]",
        ["Meziantou.Analyzer"] = "[3.0.75]",
        ["Microsoft.CodeAnalysis.BannedApiAnalyzers"] = "[4.14.0]",
        ["Microsoft.Sbom.Targets"] = "[4.1.5]",
        ["Microsoft.VisualStudio.Threading.Analyzers"] = "[17.14.15]",
        ["ReflectionAnalyzers"] = "[0.3.1]",
        ["Roslynator.Analyzers"] = "[4.15.0]",
        ["SmartAnalyzers.MultithreadingAnalyzer"] = "[1.1.31]",
    };

    private static readonly Dictionary<string, string> TestDependencySnapshot = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        ["Microsoft.Testing.Extensions.CodeCoverage"] = "[18.9.0]",
        ["Microsoft.Testing.Extensions.CrashDump"] = "[2.3.1]",
        ["Microsoft.Testing.Extensions.HangDump"] = "[2.3.1]",
        ["Microsoft.Testing.Extensions.HotReload"] = "[2.3.1]",
        ["Microsoft.Testing.Extensions.Retry"] = "[2.3.1]",
        ["Microsoft.Testing.Extensions.TrxReport"] = "[2.3.1]",
    };

    [Fact]
    public void should_match_the_exact_package_entry_snapshots()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var actual = package
                .Entries.Select(entry =>
                    entry.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
                        ? "package/services/metadata/core-properties/*.psmdcp"
                        : entry.FullName
                )
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var expected = BuildExpectedEntrySnapshot(packageId);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void should_match_the_exact_nuspec_contract_snapshots()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var nuspec = ReadNuspec(package, packageId);
            var metadata = nuspec.Descendants().Single(element => element.Name.LocalName == "metadata");
            var actualDependencies = metadata
                .Descendants()
                .Where(element => element.Name.LocalName == "dependency")
                .ToDictionary(
                    element => element.Attribute("id")!.Value,
                    element => element.Attribute("version")!.Value,
                    StringComparer.Ordinal
                );
            var expectedDependencies = new Dictionary<string, string>(BaseDependencySnapshot, StringComparer.Ordinal);
            if (string.Equals(packageId, "Headless.NET.Sdk.Test", StringComparison.Ordinal))
            {
                foreach (var dependency in TestDependencySnapshot)
                {
                    expectedDependencies.Add(dependency.Key, dependency.Value);
                }
            }

            Assert.Equal(
                expectedDependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                actualDependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            );
            Assert.Equal(packageId, MetadataValue(metadata, "id"));
            Assert.Equal(packageId, MetadataValue(metadata, "title"));
            Assert.Equal("Mahmoud Shaheen", MetadataValue(metadata, "authors"));
            Assert.Equal(PackageDescription, MetadataValue(metadata, "description"));
            Assert.Equal("README.md", MetadataValue(metadata, "readme"));
            Assert.Equal("logo.png", MetadataValue(metadata, "icon"));
            Assert.Equal("https://github.com/xshaheen/headless-sdk", MetadataValue(metadata, "projectUrl"));
            Assert.Equal("https://github.com/xshaheen/headless-sdk/releases", MetadataValue(metadata, "releaseNotes"));
            Assert.Equal(
                $"Copyright © Mahmoud Shaheen 2024 - {DateTime.UtcNow.Year}",
                MetadataValue(metadata, "copyright")
            );
            Assert.Equal(
                "xshaheen;sdk;msbuild-sdk;msbuild;editorconfig;configuration;settings;props;targets",
                MetadataValue(metadata, "tags")
            );
            Assert.Equal("true", MetadataValue(metadata, "developmentDependency"));
            Assert.Equal("false", MetadataValue(metadata, "requireLicenseAcceptance"));
            Assert.Null(metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "license"));
            Assert.Contains(
                metadata.Descendants(),
                element =>
                    element.Name.LocalName == "packageType"
                    && string.Equals(element.Attribute("name")?.Value, "MSBuildSdk", StringComparison.Ordinal)
            );
            var repository = Assert.Single(metadata.Descendants(), element => element.Name.LocalName == "repository");
            Assert.Equal("git", repository.Attribute("type")?.Value);
            Assert.Equal("https://github.com/xshaheen/headless-sdk.git", repository.Attribute("url")?.Value);
            Assert.False(string.IsNullOrWhiteSpace(repository.Attribute("branch")?.Value));
            Assert.False(string.IsNullOrWhiteSpace(repository.Attribute("commit")?.Value));
        }
    }

    [Fact]
    public void should_publish_framework_agnostic_dependency_groups()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var nuspec = ReadNuspec(package, packageId);
            var dependencies = nuspec.Descendants().Single(element => element.Name.LocalName == "dependencies");
            var groups = dependencies.Elements().ToArray();

            Assert.NotEmpty(groups);
            Assert.All(
                groups,
                group =>
                {
                    Assert.Equal("group", group.Name.LocalName);
                    Assert.Null(group.Attribute("targetFramework"));
                    Assert.Contains(group.Elements(), element => element.Name.LocalName == "dependency");
                }
            );
        }
    }

    [Fact]
    public void should_describe_every_package_as_available_to_compatible_dotnet_projects()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var readme = package.GetEntry("README.md");
            Assert.NotNull(readme);

            using var reader = new StreamReader(readme.Open());
            var content = reader.ReadToEnd();

            Assert.Contains("any compatible .NET project", content, StringComparison.Ordinal);
            Assert.DoesNotContain("internal package", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void should_ship_only_direct_build_assets()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));

            Assert.DoesNotContain(
                package.Entries,
                entry => entry.FullName.StartsWith("buildTransitive/", StringComparison.Ordinal)
            );
            Assert.Contains(package.Entries, entry => entry.FullName == $"build/{packageId}.props");
            Assert.Contains(package.Entries, entry => entry.FullName == $"build/{packageId}.targets");
            Assert.Contains(package.Entries, entry => entry.FullName == $"buildMultiTargeting/{packageId}.props");
            Assert.Contains(package.Entries, entry => entry.FullName == $"buildMultiTargeting/{packageId}.targets");
        }
    }

    [Fact]
    public void should_not_ship_a_headless_target_framework_restriction()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var msbuildAssets = package
                .Entries.Where(entry =>
                    (
                        entry.FullName.EndsWith(".props", StringComparison.Ordinal)
                        || entry.FullName.EndsWith(".targets", StringComparison.Ordinal)
                    )
                    && (
                        entry.FullName.StartsWith("build/", StringComparison.Ordinal)
                        || entry.FullName.StartsWith("buildMultiTargeting/", StringComparison.Ordinal)
                        || entry.FullName.StartsWith("sdk/", StringComparison.Ordinal)
                    )
                )
                .ToArray();
            Assert.NotEmpty(msbuildAssets);

            foreach (var asset in msbuildAssets)
            {
                using var reader = new StreamReader(asset.Open());
                var content = reader.ReadToEnd();

                Assert.DoesNotContain("HeadlessValidateTargetFramework", content, StringComparison.Ordinal);
                Assert.DoesNotContain("supports only", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("TargetFramework.StartsWith('net10.0')", content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void should_hook_build_targets_before_microsoft_targets_in_every_sdk_wrapper()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var sdkProps = package.GetEntry("sdk/Sdk.props");
            Assert.NotNull(sdkProps);

            using var stream = sdkProps.Open();
            var document = XDocument.Load(stream);
            var hook = Assert.Single(
                document.Descendants(),
                element => element.Name.LocalName == "BeforeMicrosoftNETSdkTargets"
            );

            Assert.Null(hook.Attribute("Condition"));
            Assert.Contains($"../build/{packageId}.targets", hook.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void should_make_mandatory_quality_and_optional_sbom_dependencies_restore_visible()
    {
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));
            var dependencies = ReadDependencies(package, packageId);

            foreach (var analyzerPackage in HeadlessSdkPackageFixture.MandatoryAnalyzerPackageIds)
            {
                Assert.Contains(analyzerPackage, dependencies);
            }

            Assert.Contains("Microsoft.Sbom.Targets", dependencies);
        }
    }

    [Fact]
    public void should_not_override_microsoft_source_embedding_defaults()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var target = package.GetEntry("build/SupportPackageInformation.targets");
        Assert.NotNull(target);

        using var reader = new StreamReader(target.Open());
        var content = reader.ReadToEnd();

        Assert.DoesNotContain("<EmbedUntrackedSources>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void should_keep_sbom_restore_and_import_ownership_explicit()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var props = package.GetEntry("build/SupportSbom.props");
        var target = package.GetEntry("build/SupportSbom.targets");
        Assert.NotNull(props);
        Assert.NotNull(target);

        using var propsReader = new StreamReader(props.Open());
        var propsDocument = XDocument.Parse(propsReader.ReadToEnd());
        var restoreReference = Assert.Single(propsDocument.Descendants("PackageReference"));
        Assert.Equal("Microsoft.Sbom.Targets", restoreReference.Attribute("Include")?.Value);
        Assert.Equal("none", restoreReference.Attribute("IncludeAssets")?.Value);

        using var targetReader = new StreamReader(target.Open());
        var targetsDocument = XDocument.Parse(targetReader.ReadToEnd());
        var bindingUpdate = Assert.Single(targetsDocument.Descendants("PackageReference"));
        Assert.Null(bindingUpdate.Attribute("Include"));
        Assert.Equal("Microsoft.Sbom.Targets", bindingUpdate.Attribute("Update")?.Value);
        Assert.Equal("none", bindingUpdate.Attribute("IncludeAssets")?.Value);
    }

    [Fact]
    public void should_gate_nuget_publication_on_a_published_release_and_protected_environment()
    {
        var repositoryRoot = TestRepository.FindRoot("NuGet publication workflow contract");
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "publish.yml"));

        Assert.Contains("release:", workflow, StringComparison.Ordinal);
        Assert.Contains("- published", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "if: github.event_name == 'release' && github.event.action == 'published'",
            workflow,
            StringComparison.Ordinal
        );
        Assert.Contains("name: NuGet Release", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ secrets.NUGET_API_KEY }}", workflow, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--skip-duplicate", workflow, StringComparison.Ordinal);
    }

    private static HashSet<string> ReadDependencies(ZipArchive package, string packageId)
    {
        return ReadNuspec(package, packageId)
            .Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static XDocument ReadNuspec(ZipArchive package, string packageId)
    {
        var nuspec = package.GetEntry($"{packageId}.nuspec");
        Assert.NotNull(nuspec);

        using var reader = new StreamReader(nuspec.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static string MetadataValue(XElement metadata, string name) =>
        metadata.Elements().Single(element => element.Name.LocalName == name).Value;

    private static string[] BuildExpectedEntrySnapshot(string packageId)
    {
        var expected = new HashSet<string>(SharedPackageEntries, StringComparer.Ordinal)
        {
            $"{packageId}.nuspec",
            $"buildMultiTargeting/{packageId}.props",
            $"buildMultiTargeting/{packageId}.targets",
        };

        if (!string.Equals(packageId, "Headless.NET.Sdk", StringComparison.Ordinal))
        {
            expected.Add($"build/{packageId}.props");
            expected.Add($"build/{packageId}.targets");
        }

        return expected.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}
