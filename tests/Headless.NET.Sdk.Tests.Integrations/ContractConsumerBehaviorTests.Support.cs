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

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
{
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
        foreach (var analyzerPackage in HeadlessSdkPackageFixture.MandatoryAnalyzerPackageIds)
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
        foreach (var analyzerPackage in HeadlessSdkPackageFixture.MandatoryAnalyzerPackageIds)
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
