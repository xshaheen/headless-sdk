using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
{
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
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(local.ExitCode == 0, local.Output);
        Assert.Contains("CS1030", local.Output, StringComparison.Ordinal);
        Assert.Contains("warning HEADLESS0001", local.Output, StringComparison.OrdinalIgnoreCase);

        var ci = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:GITHUB_ACTIONS=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
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
            $"restore {Quote(project.ProjectFilePath)} -p:ContinuousIntegrationBuild=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
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
            $"restore {Quote(project.ProjectFilePath)} -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=false -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(seed.ExitCode == 0, seed.Output);
        Assert.True(File.Exists(Path.Combine(project.RootDirectory, "packages.lock.json")));

        var withLock = await project.EvaluateHeadlessPropertiesAsync("-p:ContinuousIntegrationBuild=true");
        Assert.Equal("true", withLock["RestoreLockedMode"]);
    }
}
