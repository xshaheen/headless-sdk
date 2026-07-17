using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// Guards the checked-in dependency pins injected by Headless.NET.Sdk.Test. These are pure source
// checks (no packaging fixture), so they stay out of the package collection.
public sealed class VersionConsistencyTests
{
    // Shipped property name -> the package whose version it carries.
    private static readonly IReadOnlyDictionary<string, string> InjectedVersionProperties = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["_HeadlessMtpCrashDumpVersion"] = "Microsoft.Testing.Extensions.CrashDump",
        ["_HeadlessMtpHangDumpVersion"] = "Microsoft.Testing.Extensions.HangDump",
        ["_HeadlessMtpHotReloadVersion"] = "Microsoft.Testing.Extensions.HotReload",
        ["_HeadlessMtpRetryVersion"] = "Microsoft.Testing.Extensions.Retry",
        ["_HeadlessMtpTrxReportVersion"] = "Microsoft.Testing.Extensions.TrxReport",
        ["_HeadlessMtpCodeCoverageVersion"] = "Microsoft.Testing.Extensions.CodeCoverage",
    };

    [Fact]
    public void shipped_test_tool_versions_should_match_central_package_versions()
    {
        var repositoryRoot = TestRepository.FindRoot("version consistency tests");
        var central = ReadCentralPackageVersions(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        var shipped = ReadPropertyValues(
            Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "build", "SupportTestProjects.Versions.props")
        );

        foreach (var (property, packageId) in InjectedVersionProperties)
        {
            Assert.True(
                shipped.TryGetValue(property, out var shippedVersion),
                $"SupportTestProjects.Versions.props is missing {property}."
            );
            Assert.True(
                central.TryGetValue(packageId, out var centralVersion),
                $"Directory.Packages.props has no <PackageVersion> for {packageId}."
            );
            Assert.True(
                string.Equals(shippedVersion, $"[{centralVersion}]", StringComparison.Ordinal),
                $"{packageId}: SupportTestProjects.Versions.props has {shippedVersion} but "
                    + $"Directory.Packages.props pins {centralVersion}."
            );
        }
    }

    [Fact]
    public void implicit_test_tool_references_should_use_shipped_version_properties()
    {
        var repositoryRoot = TestRepository.FindRoot("version consistency tests");
        var props = XDocument.Load(
            Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk.Test", "build", "Headless.NET.Sdk.Test.props")
        );
        var references = props
            .Descendants("PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")?.Value ?? string.Empty,
                element => element,
                StringComparer.Ordinal
            );

        Assert.Equal(InjectedVersionProperties.Count, references.Count);

        foreach (var (property, packageId) in InjectedVersionProperties)
        {
            Assert.True(
                references.TryGetValue(packageId, out var reference),
                $"Missing implicit reference: {packageId}."
            );
            Assert.Equal($"$({property})", reference.Attribute("Version")?.Value);
            Assert.Equal("true", reference.Attribute("IsImplicitlyDefined")?.Value);
            Assert.Equal("all", reference.Attribute("PrivateAssets")?.Value);
        }
    }

    [Fact]
    public void shipped_sbom_import_path_should_match_the_central_package_version()
    {
        var repositoryRoot = TestRepository.FindRoot("SBOM version consistency tests");
        var central = ReadCentralPackageVersions(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        Assert.True(
            central.TryGetValue("Microsoft.Sbom.Targets", out var centralVersion),
            "Directory.Packages.props has no <PackageVersion> for Microsoft.Sbom.Targets."
        );

        var props = ReadPropertyValues(
            Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "build", "SupportSbom.props")
        );
        Assert.Equal($"[{centralVersion}]", props["_HeadlessMicrosoftSbomTargetsVersion"]);
    }

    private static Dictionary<string, string> ReadCentralPackageVersions(string path) =>
        XDocument
            .Load(path)
            .Descendants("PackageVersion")
            .Where(element => element.Attribute("Include") is not null && element.Attribute("Version") is not null)
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal
            );

    private static Dictionary<string, string> ReadPropertyValues(string path) =>
        XDocument
            .Load(path)
            .Descendants("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);
}
