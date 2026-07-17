using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class ContractConsumerBehaviorTests(HeadlessSdkPackageFixture fixture)
{
    private static readonly string[] MandatoryAnalyzerPackages =
    [
        "AsyncFixer",
        "Asyncify",
        "ErrorProne.NET.CoreAnalyzers",
        "Meziantou.Analyzer",
        "Microsoft.CodeAnalysis.BannedApiAnalyzers",
        "Microsoft.VisualStudio.Threading.Analyzers",
        "ReflectionAnalyzers",
        "Roslynator.Analyzers",
        "SmartAnalyzers.MultithreadingAnalyzer",
    ];

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
            $"build {Quote(project.ProjectFilePath)} -p:HeadlessInferTargetFramework=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("TargetFramework", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_restore_all_mandatory_analyzers_and_report_banned_symbols()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);

        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        foreach (var analyzerPackage in MandatoryAnalyzerPackages)
        {
            Assert.Contains($"\"{analyzerPackage}/", assets, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task should_reassert_mandatory_analyzers_after_consumer_override_attempts()
    {
        var baseDependencies = ReadPackageDependencyVersions(fixture.PackagePath);
        var meziantouVersion = baseDependencies["Meziantou.Analyzer"];
        var asyncFixerVersion = baseDependencies["AsyncFixer"];
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["RunAnalyzers"] = "false" },
            extraPackageReferences: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Meziantou.Analyzer"] = meziantouVersion,
                ["AsyncFixer"] = asyncFixerVersion,
            },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );
        await UpdateProjectAsync(
            project,
            root =>
            {
                var references = root.Descendants("PackageReference").ToArray();
                var meziantou = references.Single(element =>
                    element.Attribute("Include")?.Value == "Meziantou.Analyzer"
                );
                meziantou.SetAttributeValue("ExcludeAssets", "all");

                var asyncFixer = references.Single(element => element.Attribute("Include")?.Value == "AsyncFixer");
                asyncFixer.ReplaceWith(new XElement("PackageReference", new XAttribute("Remove", "AsyncFixer")));

                var propertyTarget = root.Elements("Target")
                    .Single(element => element.Attribute("Name")?.Value == "WriteHeadlessProperties");
                propertyTarget.AddBeforeSelf(
                    new XElement(
                        "ItemGroup",
                        new XElement("EditorConfigFiles", new XAttribute("Remove", "@(EditorConfigFiles)"))
                    )
                );
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var editorConfigs = properties["EditorConfigFiles"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            1,
            editorConfigs.Count(path =>
                path.EndsWith("Headless.NET.Sdk.Analyzers.editorconfig", StringComparison.OrdinalIgnoreCase)
            )
        );

        using var assets = JsonDocument.Parse(
            await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken)
        );
        var framework = assets
            .RootElement.GetProperty("project")
            .GetProperty("frameworks")
            .EnumerateObject()
            .Single()
            .Value;
        var target = assets.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        var libraries = assets.RootElement.GetProperty("libraries");
        AssertAnalyzerDependencyInAssets(framework, target, libraries, "Meziantou.Analyzer", meziantouVersion);
        AssertAnalyzerDependencyInAssets(framework, target, libraries, "AsyncFixer", asyncFixerVersion);
    }

    [Fact]
    public async Task should_not_reference_meziantou_analyzer_from_its_own_package_project()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PackageId"] = "Meziantou.Analyzer",
            }
        );

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(restore.ExitCode == 0, restore.Output);
        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("Meziantou.Analyzer", packageReferences, StringComparer.Ordinal);

        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\"Meziantou.Analyzer/", assets, StringComparison.Ordinal);
        foreach (var analyzerPackage in MandatoryAnalyzerPackages.Where(package => package != "Meziantou.Analyzer"))
        {
            Assert.Contains($"\"{analyzerPackage}/", assets, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task should_restore_required_mtp_extensions_and_run_a_clean_consumer_on_first_restore()
    {
        var xunitVersion = ReadCentralPackageVersion("xunit.v3.mtp-v2");
        var testSdkPackagePath = fixture.GetPackagePath("Headless.NET.Sdk.Test");
        var testSdkDependencies = ReadPackageDependencyVersions(testSdkPackagePath);
        var crashDumpVersion = testSdkDependencies["Microsoft.Testing.Extensions.CrashDump"];
        var codeCoverageVersion = testSdkDependencies["Microsoft.Testing.Extensions.CodeCoverage"];
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
            targetFramework: "net10.0",
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

    [Fact]
    public async Task should_honor_final_implicit_usings_false_value()
    {
        const string JsonConsumer =
            "namespace ConsumerProject; public static class JsonConsumer { public static string Serialize(object value) => JsonSerializer.Serialize(value); }";

        await using var defaultProject = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["JsonConsumer.cs"] = JsonConsumer,
            }
        );
        var defaultResult = await defaultProject.RunDotNetAsync(
            $"build {Quote(defaultProject.ProjectFilePath)} -p:RestoreConfigFile={Quote(defaultProject.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(defaultResult.ExitCode == 0, defaultResult.Output);

        await using var disabledProject = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["ImplicitUsings"] = "false" },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["JsonConsumer.cs"] = JsonConsumer,
            }
        );
        var disabledResult = await disabledProject.RunDotNetAsync(
            $"build {Quote(disabledProject.ProjectFilePath)} -p:RestoreConfigFile={Quote(disabledProject.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.NotEqual(0, disabledResult.ExitCode);
        Assert.Contains("CS0103", disabledResult.Output, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer", disabledResult.Output, StringComparison.Ordinal);
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

    [Fact]
    public async Task should_import_quality_assets_once_when_sdk_and_package_reference_are_both_used()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: true
        );

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );
        Assert.True(restore.ExitCode == 0, restore.Output);
        Assert.DoesNotContain("NU1504", restore.Output, StringComparison.Ordinal);

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var analyzerPackage in MandatoryAnalyzerPackages)
        {
            Assert.Equal(
                1,
                packageReferences.Count(id => string.Equals(id, analyzerPackage, StringComparison.Ordinal))
            );
        }

        var editorConfigs = properties["EditorConfigFiles"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            1,
            editorConfigs.Count(path =>
                path.EndsWith("Headless.NET.Sdk.Analyzers.editorconfig", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    [Fact]
    public async Task should_support_additional_sdk_consumption_with_a_satellite_package()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <Sdk Name="Headless.NET.Sdk.Razor" Version="{{fixture.PackageVersion}}" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="AssertHeadlessContract" BeforeTargets="Build">
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk.Razor'"
                  Text="Expected the Headless Razor SDK identity, got '$(HeadlessSdkName)'."
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
        await AssertMandatoryAnalyzersInAssetsAsync(project);
    }

    [Fact]
    public async Task should_resolve_a_versionless_sdk_from_global_json()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            """
            <Project Sdk="Headless.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="AssertHeadlessContract" BeforeTargets="Build">
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'"
                  Text="Expected the Headless SDK identity, got '$(HeadlessSdkName)'."
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );

        var repositoryRoot = TestRepository.FindRoot("global.json SDK resolution contract test");
        using var repositoryGlobalJson = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(repositoryRoot, "global.json"),
                TestContext.Current.CancellationToken
            )
        );
        var sdkVersion = repositoryGlobalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sdkVersion));
        await File.WriteAllTextAsync(
            Path.Combine(project.RootDirectory, "global.json"),
            $$"""
            {
              "sdk": {
                "version": "{{sdkVersion}}",
                "rollForward": "latestFeature",
                "allowPrerelease": false
              },
              "msbuild-sdks": {
                "Headless.NET.Sdk": "{{fixture.PackageVersion}}"
              }
            }
            """,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
        await AssertMandatoryAnalyzersInAssetsAsync(project);
    }

    [Fact]
    public async Task should_restore_and_evaluate_a_clean_consumer_with_static_graph()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="WriteHeadlessStaticGraphContract">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'"
                  Text="Static-graph evaluation lost the Headless SDK identity."
                />
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/static-graph-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -restore -graphBuild -t:WriteHeadlessStaticGraphContract -p:RestoreUseStaticGraphEvaluation=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -nologo"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "static-graph-contract.txt"));
    }

    [Fact]
    public async Task should_succeed_design_time_build_and_retain_the_headless_contract()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="WriteHeadlessDesignTimeContract" DependsOnTargets="Compile">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <Error Condition="'$(DesignTimeBuild)' != 'true'" Text="Expected a design-time build." />
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'"
                  Text="Design-time evaluation lost the Headless SDK identity."
                />
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/design-time-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -restore -t:WriteHeadlessDesignTimeContract -p:DesignTimeBuild=true -p:BuildingProject=false -p:SkipCompilerExecution=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -nologo"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "design-time-contract.txt"));
    }

    [Fact]
    public async Task should_import_quality_assets_once_in_outer_and_inner_multitargeting_builds()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target
                Name="WriteHeadlessOuterBuildContract"
                BeforeTargets="DispatchToInnerBuilds"
                Condition="'$(IsCrossTargetingBuild)' == 'true'"
              >
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/outer-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
              <Target
                Name="WriteHeadlessInnerBuildContract"
                AfterTargets="Build"
                Condition="'$(IsCrossTargetingBuild)' != 'true'"
              >
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/inner-$(TargetFramework)-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -m:1 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "outer-contract.txt"));
        foreach (var targetFramework in new[] { "net10.0", "net10.0-windows" })
        {
            await AssertQualityContractFileAsync(
                Path.Combine(project.RootDirectory, $"inner-{targetFramework}-contract.txt")
            );
            Assert.True(
                File.Exists(
                    Path.Combine(project.RootDirectory, "bin", "Debug", targetFramework, "ConsumerProject.dll")
                ),
                $"Expected an output assembly for {targetFramework}."
            );
        }
    }

    [Fact]
    public async Task should_run_a_dotnet_10_file_app_with_sdk_directive()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        var appPath = Path.Combine(project.RootDirectory, "contract-app.cs");
        await File.WriteAllTextAsync(
            appPath,
            $$"""
            #:sdk Headless.NET.Sdk@{{fixture.PackageVersion}}
            #:property TargetFramework=net10.0

            using System.Reflection;

            var sdkName = Assembly
                .GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "Headless.NET.Sdk.SdkName")
                .Value;
            Console.WriteLine($"SDK={sdkName};JSON_TYPE={typeof(JsonSerializer).FullName};NOW={DateTime.Now:O}");
            """,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var result = await project.RunDotNetAsync($"run --file {Quote(appPath)}");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("SDK=Headless.NET.Sdk", result.Output, StringComparison.Ordinal);
        Assert.Contains("JSON_TYPE=System.Text.Json.JsonSerializer", result.Output, StringComparison.Ordinal);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_generate_an_spdx_sbom_inside_a_clean_consumer_package(bool usePackageReference)
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: usePackageReference
        );
        var packageOutput = Path.Combine(project.RootDirectory, "sbom-packages");
        Directory.CreateDirectory(packageOutput);

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -p:GenerateSBOM=true -p:PackageVersion=1.0.0 -o {Quote(packageOutput)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        var packagePath = Assert.Single(
            Directory.EnumerateFiles(packageOutput, "*.nupkg", SearchOption.TopDirectoryOnly)
        );
        using var package = ZipFile.OpenRead(packagePath);
        Assert.Contains(
            package.Entries,
            entry => string.Equals(entry.FullName, "_manifest/spdx_2.2/manifest.spdx.json", StringComparison.Ordinal)
        );
    }

    private static string ReadCentralPackageVersion(string packageId)
    {
        var repositoryRoot = TestRepository.FindRoot($"central package version for {packageId}");
        var document = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        var package = document
            .Descendants("PackageVersion")
            .Single(element => string.Equals(element.Attribute("Include")?.Value, packageId, StringComparison.Ordinal));

        return package.Attribute("Version")?.Value
            ?? throw new InvalidOperationException($"PackageVersion {packageId} does not declare Version.");
    }

    private static IReadOnlyDictionary<string, string> ReadPackageDependencyVersions(string packagePath)
    {
        using var package = ZipFile.OpenRead(packagePath);
        var nuspec = package.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspec.Open();
        return XDocument
            .Load(stream)
            .Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .ToDictionary(
                element =>
                    element.Attribute("id")?.Value
                    ?? throw new InvalidOperationException("Package dependency does not declare an ID."),
                element =>
                    element.Attribute("version")?.Value
                    ?? throw new InvalidOperationException("Package dependency does not declare a version."),
                StringComparer.Ordinal
            );
    }

    private static string ReadExactPackageDependencyVersion(string versionRange, string packageId)
    {
        if (
            versionRange.Length < 3
            || versionRange[0] != '['
            || versionRange[^1] != ']'
            || versionRange.Contains(',', StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Package dependency {packageId} must use an exact version range; found '{versionRange}'."
            );
        }

        return versionRange[1..^1];
    }

    private static void AssertAnalyzerDependencyInAssets(
        JsonElement framework,
        JsonElement target,
        JsonElement libraries,
        string packageId,
        string expectedVersionRange
    )
    {
        var dependency = framework.GetProperty("dependencies").GetProperty(packageId);
        Assert.Equal("All", dependency.GetProperty("suppressParent").GetString());
        Assert.Contains("Analyzers", dependency.GetProperty("include").GetString(), StringComparison.OrdinalIgnoreCase);

        var expectedVersion =
            expectedVersionRange.Length >= 3
            && expectedVersionRange[0] == '['
            && expectedVersionRange[^1] == ']'
            && !expectedVersionRange.Contains(',', StringComparison.Ordinal)
                ? expectedVersionRange[1..^1]
                : expectedVersionRange;
        var packageKey = $"{packageId}/{expectedVersion}";
        Assert.True(target.TryGetProperty(packageKey, out _), $"Expected exact resolved package {packageKey}.");
        var library = libraries.GetProperty(packageKey);
        Assert.Contains(
            library.GetProperty("files").EnumerateArray(),
            file =>
                file.GetString()?.Contains("analyzers/", StringComparison.OrdinalIgnoreCase) == true
                && file.GetString()?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true
        );
    }

    private static async Task AssertMandatoryAnalyzersInAssetsAsync(ConsumerProject project)
    {
        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        foreach (var analyzerPackage in MandatoryAnalyzerPackages)
        {
            Assert.Contains($"\"{analyzerPackage}/", assets, StringComparison.Ordinal);
        }
    }

    private static async Task AssertQualityContractFileAsync(string path)
    {
        Assert.True(File.Exists(path), $"Expected evaluated Headless contract file '{path}'.");
        var properties = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))
            .Trim()
            .Split('~')
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        Assert.Equal("Headless.NET.Sdk", properties["SdkName"]);
        var packageReferences = properties["PackageReferences"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var analyzerPackage in MandatoryAnalyzerPackages)
        {
            Assert.Equal(
                1,
                packageReferences.Count(id => string.Equals(id, analyzerPackage, StringComparison.Ordinal))
            );
        }

        var editorConfigs = properties["EditorConfigFiles"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            1,
            editorConfigs.Count(file =>
                file.EndsWith("Headless.NET.Sdk.Analyzers.editorconfig", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    private static async Task WriteProjectAsync(
        ConsumerProject project,
        string projectContent,
        IReadOnlyDictionary<string, string> sourceFiles
    )
    {
        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            projectContent,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        foreach (var (fileName, content) in sourceFiles)
        {
            await File.WriteAllTextAsync(
                Path.Combine(project.RootDirectory, fileName),
                content,
                Encoding.UTF8,
                TestContext.Current.CancellationToken
            );
        }
    }

    private static async Task UpdateProjectAsync(ConsumerProject project, Action<XElement> update)
    {
        var document = XDocument.Load(project.ProjectFilePath);
        var root = document.Root ?? throw new InvalidOperationException("Consumer project has no root element.");
        update(root);
        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            document.ToString(),
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );
    }
}
