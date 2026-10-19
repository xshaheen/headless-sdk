using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed class ContractCiRestoreTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    // The PackageReference restore bootstrap the README documents, verbatim.
    private const string RestoreBootstrapDirectoryBuildProps = """
        <Project>
          <PropertyGroup>
            <NuGetAudit>true</NuGetAudit>
            <NuGetAuditMode>all</NuGetAuditMode>
            <NuGetAuditLevel>low</NuGetAuditLevel>
            <RestoreLockedMode
              Condition="Exists('$(MSBuildProjectDirectory)/packages.lock.json')"
            >true</RestoreLockedMode>
            <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904;NU1905</WarningsNotAsErrors>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public async Task should_warn_about_the_restore_bootstrap_when_package_reference_targets_net8()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net8.0"
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("warning HSDK001", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set NuGetAuditMode=all in", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_fail_ci_build_when_package_reference_has_no_restore_bootstrap()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net8.0"
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:CI=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error HSDK001", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set NuGetAuditMode=all in", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_not_warn_about_the_restore_bootstrap_when_ci_package_reference_has_it()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net8.0",
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Build.props"] = RestoreBootstrapDirectoryBuildProps,
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:CI=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("HSDK001", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_not_warn_about_the_restore_bootstrap_when_consumed_as_msbuild_sdk()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net8.0",
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:CI=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("HSDK001", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_apply_warning_errors_only_when_ci_policy_is_active()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ContractWarning.cs"] = "#warning HEADLESS_CONTRACT_WARNING",
            }
        );
        await UpdateProjectAsync(
            project,
            root =>
                root.Add(
                    new XElement(
                        "Target",
                        new XAttribute("Name", "EmitHeadlessContractMsBuildWarning"),
                        new XAttribute("BeforeTargets", "CoreCompile"),
                        new XElement(
                            "Warning",
                            new XAttribute("Code", "HEADLESS0001"),
                            new XAttribute("Text", "HEADLESS_CONTRACT_MSBUILD_WARNING")
                        )
                    )
                )
        );

        var local = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(local.ExitCode == 0, local.Output);
        Assert.Contains("CS1030", local.Output, StringComparison.Ordinal);
        Assert.Contains("warning HEADLESS0001", local.Output, StringComparison.OrdinalIgnoreCase);

        var ci = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:GITHUB_ACTIONS=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.NotEqual(0, ci.ExitCode);
        Assert.Contains("error CS1030", ci.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("error HEADLESS0001", ci.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_keep_an_unreachable_audit_source_as_nu1900_warning_on_ci()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await File.WriteAllTextAsync(
            project.NuGetConfigPath,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{{fixture.PackageSourceDirectory}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <auditSources>
                <clear />
                <add key="unreachable-audit" value="https://127.0.0.1:1/v3/index.json" />
              </auditSources>
            </configuration>
            """,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:ContinuousIntegrationBuild=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("warning NU1900", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error NU1900", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_keep_vulnerable_package_findings_as_warnings_on_ci_unless_the_consumer_escalates_them()
    {
        // Newtonsoft.Json 12.0.1 carries GHSA-5crp-9r3c-p9vr (high severity, NU1903). The finding
        // comes from the live nuget.org advisory feed, so this test needs the network like any restore.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraPackageReferences: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Newtonsoft.Json"] = "12.0.1",
            }
        );

        var ci = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:GITHUB_ACTIONS=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(ci.ExitCode == 0, ci.Output);
        Assert.Contains("warning NU1903", ci.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error NU1903", ci.Output, StringComparison.OrdinalIgnoreCase);

        await UpdateProjectAsync(
            project,
            root =>
                root.Add(
                    new XElement("PropertyGroup", new XElement("WarningsAsErrors", "$(WarningsAsErrors);NU1903;NU1904"))
                )
        );

        var escalated = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:GITHUB_ACTIONS=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.NotEqual(0, escalated.ExitCode);
        Assert.Contains("error NU1903", escalated.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_keep_non_vulnerability_nuget_warnings_as_warnings_on_ci()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraPackageReferences: new Dictionary<string, string>(StringComparer.Ordinal) { ["Humanizer"] = "2.14.1" }
        );
        await UpdateProjectAsync(
            project,
            root =>
                root.Add(
                    new XElement(
                        "ItemGroup",
                        new XElement(
                            "PackageReference",
                            new XAttribute("Include", "Humanizer"),
                            new XAttribute("Version", "2.14.1")
                        )
                    )
                )
        );

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:ContinuousIntegrationBuild=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(restore.ExitCode == 0, restore.Output);
        Assert.Contains("warning NU1504", restore.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error NU1504", restore.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_enable_locked_restore_on_ci_only_when_a_lock_file_exists()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false
        );

        var withoutLock = await project.EvaluateHeadlessPropertiesAsync("-p:ContinuousIntegrationBuild=true");
        Assert.NotEqual("true", withoutLock["RestoreLockedMode"]);
        Assert.False(File.Exists(Path.Combine(project.RootDirectory, "packages.lock.json")));

        var seed = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=false -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(seed.ExitCode == 0, seed.Output);
        Assert.True(File.Exists(Path.Combine(project.RootDirectory, "packages.lock.json")));

        var withLock = await project.EvaluateHeadlessPropertiesAsync("-p:ContinuousIntegrationBuild=true");
        Assert.Equal("true", withLock["RestoreLockedMode"]);
    }
}
