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
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; public static System.Collections.ArrayList Values => new(); public static System.Reflection.Assembly? Find() => System.Reflection.Assembly.GetAssembly(typeof(BannedApiConsumer)); }",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
        Assert.Contains("List<T>", result.Output, StringComparison.Ordinal);
        Assert.Contains("Type.Assembly", result.Output, StringComparison.Ordinal);

        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        foreach (var analyzerPackage in HeadlessSdkPackageFixture.MandatoryAnalyzerPackageIds)
        {
            Assert.Contains($"\"{analyzerPackage}/", assets, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("IncludeDefaultBannedSymbols", "false", false, true, false)]
    [InlineData("IncludeDefaultBannedSymbols", "false", false, true, true)]
    [InlineData("BannedNewtonsoftJsonSymbols", "false", true, false, false)]
    [InlineData("BannedNewtonsoftJsonSymbols", "false", true, false, true)]
    [InlineData("DisableSupportBannedSymbols", "true", false, false, false)]
    [InlineData("DisableSupportBannedSymbols", "true", false, false, true)]
    public async Task should_honor_banned_symbol_opt_outs_in_package_and_sdk_consumption(
        string propertyName,
        string propertyValue,
        bool expectDefaultSymbols,
        bool expectNewtonsoftSymbols,
        bool useSdkConsumption
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: useSdkConsumption ? $"Headless.NET.Sdk/{fixture.PackageVersion}" : "Microsoft.NET.Sdk",
            targetFramework: "net10.0",
            includePackageReference: !useSdkConsumption,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { [propertyName] = propertyValue }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var additionalFiles = properties["AdditionalFiles"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            expectDefaultSymbols,
            additionalFiles.Any(path => path.EndsWith("BannedSymbols.txt", StringComparison.OrdinalIgnoreCase))
        );
        Assert.Equal(
            expectNewtonsoftSymbols,
            additionalFiles.Any(path =>
                path.EndsWith("BannedSymbols.NewtonsoftJson.txt", StringComparison.OrdinalIgnoreCase)
            )
        );
        Assert.Contains(
            "Microsoft.CodeAnalysis.BannedApiAnalyzers",
            properties["PackageReferences"],
            StringComparison.Ordinal
        );
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
        foreach (
            var analyzerPackage in HeadlessSdkPackageFixture.MandatoryAnalyzerPackageIds.Where(package =>
                package != "Meziantou.Analyzer"
            )
        )
        {
            Assert.Contains($"\"{analyzerPackage}/", assets, StringComparison.Ordinal);
        }
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
}
