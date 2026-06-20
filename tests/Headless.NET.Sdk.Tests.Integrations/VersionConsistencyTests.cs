using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// Guards the single source of truth for the versions the SDK injects into consumer test projects.
// build/SupportTestProjects.targets references them as $(_Headless*Version) properties; those
// properties live in build/SupportTestProjects.Versions.props, which the
// GenerateHeadlessTestToolVersions target regenerates from Directory.Packages.props on every build.
// These are pure source-file checks (no packaging fixture), so they stay out of the collection.
public sealed class VersionConsistencyTests
{
    // Generated property name -> the package whose version it carries.
    private static readonly IReadOnlyDictionary<string, string> InjectedVersionProperties = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["_HeadlessMicrosoftNetTestSdkVersion"] = "Microsoft.NET.Test.Sdk",
        ["_HeadlessMtpCrashDumpVersion"] = "Microsoft.Testing.Extensions.CrashDump",
        ["_HeadlessMtpHangDumpVersion"] = "Microsoft.Testing.Extensions.HangDump",
        ["_HeadlessMtpHotReloadVersion"] = "Microsoft.Testing.Extensions.HotReload",
        ["_HeadlessMtpRetryVersion"] = "Microsoft.Testing.Extensions.Retry",
        ["_HeadlessMtpTrxReportVersion"] = "Microsoft.Testing.Extensions.TrxReport",
        ["_HeadlessMtpCodeCoverageVersion"] = "Microsoft.Testing.Extensions.CodeCoverage",
        ["_HeadlessGitHubActionsTestLoggerVersion"] = "GitHubActionsTestLogger",
    };

    [Fact]
    public void generated_test_tool_versions_should_match_central_package_versions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var central = ReadCentralPackageVersions(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        var generated = ReadPropertyValues(
            Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "build", "SupportTestProjects.Versions.props")
        );

        foreach (var (property, packageId) in InjectedVersionProperties)
        {
            Assert.True(
                generated.TryGetValue(property, out var generatedVersion),
                $"SupportTestProjects.Versions.props is missing {property}. Rebuild Headless.NET.Sdk to regenerate it."
            );
            Assert.True(
                central.TryGetValue(packageId, out var centralVersion),
                $"Directory.Packages.props has no <PackageVersion> for {packageId}."
            );
            Assert.True(
                string.Equals(generatedVersion, centralVersion, StringComparison.Ordinal),
                $"{packageId}: SupportTestProjects.Versions.props has {generatedVersion} but Directory.Packages.props "
                    + $"pins {centralVersion}. Rebuild Headless.NET.Sdk to regenerate the file, then commit it."
            );
        }
    }

    [Fact]
    public void injected_test_tool_references_should_use_generated_version_properties()
    {
        var repositoryRoot = FindRepositoryRoot();
        var targets = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "build", "SupportTestProjects.targets")
        );

        foreach (var property in InjectedVersionProperties.Keys)
        {
            Assert.True(
                targets.Contains($"$({property})", StringComparison.Ordinal),
                $"SupportTestProjects.targets should inject the version via $({property}) so it stays single-sourced."
            );
        }
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

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Could not locate repository root for version consistency tests.");
    }
}
