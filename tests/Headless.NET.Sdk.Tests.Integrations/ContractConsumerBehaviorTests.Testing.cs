using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
{
    private static readonly string[] RequiredMtpExtensions =
    [
        "Microsoft.Testing.Extensions.CodeCoverage",
        "Microsoft.Testing.Extensions.CrashDump",
        "Microsoft.Testing.Extensions.HangDump",
        "Microsoft.Testing.Extensions.HotReload",
        "Microsoft.Testing.Extensions.Retry",
        "Microsoft.Testing.Extensions.TrxReport",
    ];

    [Fact]
    public async Task should_restore_required_mtp_extensions_and_run_a_clean_consumer_on_first_restore()
    {
        var xunitVersion = TestRepository.ReadCentralPackageVersion("xunit.v3.mtp-v2");
        var testSdkPackagePath = fixture.GetPackagePath("Headless.NET.Sdk.Test");
        var testSdkDependencies = ReadPackageDependencyVersions(testSdkPackagePath);
        var crashDumpVersion = testSdkDependencies["Microsoft.Testing.Extensions.CrashDump"];
        var codeCoverageVersion = testSdkDependencies["Microsoft.Testing.Extensions.CodeCoverage"];
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            targetFramework: "net8.0",
            includePackageReference: false,
            extraPackageReferences: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["xunit.v3.mtp-v2"] = xunitVersion,
                ["Microsoft.Testing.Extensions.CrashDump"] = crashDumpVersion,
                ["Microsoft.Testing.Extensions.CodeCoverage"] = codeCoverageVersion,
            },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ContractSmokeTests.cs"] =
                    "using Xunit; namespace ConsumerProject; public sealed class ContractSmokeTests { [Fact] public void passes() => Assert.True(true); }",
            }
        );
        await UpdateProjectAsync(
            project,
            root =>
            {
                var references = root.Descendants("PackageReference").ToArray();
                var crashDump = references.Single(element =>
                    element.Attribute("Include")?.Value == "Microsoft.Testing.Extensions.CrashDump"
                );
                crashDump.ReplaceWith(
                    new XElement("PackageReference", new XAttribute("Remove", "Microsoft.Testing.Extensions.CrashDump"))
                );

                var codeCoverage = references.Single(element =>
                    element.Attribute("Include")?.Value == "Microsoft.Testing.Extensions.CodeCoverage"
                );
                codeCoverage.SetAttributeValue("ExcludeAssets", "all");
            }
        );

        var repositoryRoot = TestRepository.FindRoot("MTP contract test");
        File.Copy(Path.Combine(repositoryRoot, "global.json"), Path.Combine(project.RootDirectory, "global.json"));

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(restore.ExitCode == 0, restore.Output);
        var assetsText = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        foreach (var extension in RequiredMtpExtensions)
        {
            Assert.Contains($"\"{extension}/", assetsText, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"Microsoft.NET.Test.Sdk/", assetsText, StringComparison.Ordinal);

        using var assets = JsonDocument.Parse(assetsText);
        var target = assets.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        var libraries = assets.RootElement.GetProperty("libraries");
        foreach (var extension in RequiredMtpExtensions)
        {
            var expectedVersion = ReadExactPackageDependencyVersion(testSdkDependencies[extension], extension);
            var packageKey = $"{extension}/{expectedVersion}";
            Assert.True(target.TryGetProperty(packageKey, out _), $"Expected exact resolved package {packageKey}.");
            Assert.True(
                libraries.TryGetProperty(packageKey, out var library),
                $"Expected library assets for {packageKey}."
            );
            Assert.Contains(
                library.GetProperty("files").EnumerateArray(),
                file => file.GetString()?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true
            );
        }

        var evaluation = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -getProperty:OutputType,IsTestingPlatformApplication,UseMicrosoftTestingPlatformRunner,TestProject,IsTestProject -nologo"
        );
        Assert.True(evaluation.ExitCode == 0, evaluation.Output);

        // The integration suite itself runs under dotnet test's MTP server. Starting a second
        // dotnet-test server recursively suppresses nested discovery, so execute the generated
        // MTP application directly. UseMicrosoftTestingPlatformRunner=true above proves this is
        // the MTP entrypoint rather than xUnit's legacy console runner.
        var test = await project.RunDotNetAsync($"run --project {Quote(project.ProjectFilePath)} --no-restore");

        Assert.True(
            test.ExitCode == 0,
            $"{test.Output}{Environment.NewLine}Evaluated properties:{Environment.NewLine}{evaluation.Output}"
        );
        Assert.Contains("Passed", test.Output, StringComparison.OrdinalIgnoreCase);
    }
}
