using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

#nullable enable

namespace Headless.Sdk.Tests.Integrations;

[CollectionDefinition(nameof(HeadlessSdkPackageCollection))]
public sealed class HeadlessSdkPackageCollection : ICollectionFixture<HeadlessSdkPackageFixture>;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class SdkIntegrationTests(HeadlessSdkPackageFixture fixture)
{
    [Fact]
    public async Task PackDoesNotRequireLogoByDefault()
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
    public async Task DocumentationWarningsAreReportedWhenExplicitlyEnabled()
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
    public async Task BuildDoesNotOverwriteExistingEditorConfigByDefault()
    {
        const string ExistingEditorConfig = """
root = true

[*.cs]
indent_size = 2
""";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            editorConfigContent: ExistingEditorConfig
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(
            NormalizeLineEndings(ExistingEditorConfig),
            NormalizeLineEndings(await File.ReadAllTextAsync(project.EditorConfigPath))
        );
    }

    [Fact]
    public async Task BuildCopiesEditorConfigWhenExplicitlyEnabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            enableEditorConfigCopy: true
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(
            File.Exists(project.EditorConfigPath),
            "Expected build to copy .editorconfig when opt-in property is set."
        );

        var copiedEditorConfig = await File.ReadAllTextAsync(project.EditorConfigPath);
        Assert.Contains("# Common Settings", copiedEditorConfig, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildCopiesDefaultConfigFilesWhenExplicitlyEnabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            enableDefaultConfigFilesCopy: true
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(project.EditorConfigPath), "Expected build to copy .editorconfig.");
        Assert.True(File.Exists(project.CSharpierIgnorePath), "Expected build to copy .csharpierignore.");
        Assert.True(File.Exists(project.GitIgnorePath), "Expected build to copy .gitignore.");
        Assert.True(File.Exists(project.GitAttributesPath), "Expected build to copy .gitattributes.");

        var csharpierIgnore = await File.ReadAllTextAsync(project.CSharpierIgnorePath);
        Assert.Contains("**/*.verified.*", csharpierIgnore, StringComparison.Ordinal);
        Assert.Contains("**/*.received.*", csharpierIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedPackageContainsFixedNuGetAuditWarningsAsErrorsExpression()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var entry = package.GetEntry("build/SupportNuGetAudit.targets");

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());
        var content = reader.ReadToEnd();

        Assert.Contains("$(WarningsAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904", content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<WarningsAsErrors>(WarningsAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904</WarningsAsErrors>",
            content,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void PackedPackageContainsAnalyzerHygieneAndCoverageSettings()
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

        var testTargets = ReadPackageEntry(package, "build/SupportTestProjects.targets");
        Assert.Contains("configurations/default.runsettings", testTargets, StringComparison.Ordinal);

        var runsettings = ReadPackageEntry(package, "configurations/default.runsettings");
        Assert.Contains("<TreatNoTestsAsError>true</TreatNoTestsAsError>", runsettings, StringComparison.Ordinal);
        Assert.Contains("GitHubActionsTestLogger.dll", runsettings, StringComparison.Ordinal);

        var generalTargets = ReadPackageEntry(package, "build/SupportGeneral.targets");
        Assert.Contains("DisableDocumentationWarnings", generalTargets, StringComparison.Ordinal);
        Assert.Contains("CS1573;CS1591", generalTargets, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedPackageContainsSingleFileTargetFrameworkNpmAndSdkMetadataSupport()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        Assert.NotNull(package.GetEntry("build/SupportSingleFileApp.props"));
        Assert.NotNull(package.GetEntry("build/SupportTargetFrameworkInference.props"));
        Assert.NotNull(package.GetEntry("build/SupportNpm.targets"));
        Assert.NotNull(package.GetEntry("configurations/Headless.Sdk.SingleFileApp.editorconfig"));

        var bannedNewtonsoftJson = ReadPackageEntry(package, "configurations/BannedSymbols.NewtonsoftJson.txt");
        Assert.Contains("N:Newtonsoft.Json.Linq", bannedNewtonsoftJson, StringComparison.Ordinal);
        Assert.Contains("N:Newtonsoft.Json.Serialization", bannedNewtonsoftJson, StringComparison.Ordinal);

        var assemblyAttributes = ReadPackageEntry(package, "build/SupportAssemblyAttributes.targets");
        Assert.Contains("Headless.Sdk.SdkName", assemblyAttributes, StringComparison.Ordinal);

        var npmTargets = ReadPackageEntry(package, "build/SupportNpm.targets");
        Assert.Contains("HeadlessEnableNpmRestore", npmTargets, StringComparison.Ordinal);
        Assert.Contains("npm $(_HeadlessNpmRestoreCommand) --no-fund --no-audit", npmTargets, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedProjectTypePackagesContainSdkWrappersAndBuildAssets()
    {
        var expectedPackages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Headless.Sdk.Web"] = "Microsoft.NET.Sdk.Web",
            ["Headless.Sdk.Test"] = "Microsoft.NET.Sdk",
            ["Headless.Sdk.Razor"] = "Microsoft.NET.Sdk.Razor",
            ["Headless.Sdk.BlazorWebAssembly"] = "Microsoft.NET.Sdk.BlazorWebAssembly",
            ["Headless.Sdk.WindowsDesktop"] = "Microsoft.NET.Sdk.WindowsDesktop",
        };

        foreach (var (packageId, baseSdk) in expectedPackages)
        {
            using var package = ZipFile.OpenRead(fixture.GetPackagePath(packageId));

            Assert.NotNull(package.GetEntry("sdk/Sdk.props"));
            Assert.NotNull(package.GetEntry("sdk/Sdk.targets"));
            Assert.NotNull(package.GetEntry($"build/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"build/{packageId}.targets"));
            Assert.NotNull(package.GetEntry($"buildMultiTargeting/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"buildMultiTargeting/{packageId}.targets"));
            Assert.NotNull(package.GetEntry($"buildTransitive/{packageId}.props"));
            Assert.NotNull(package.GetEntry($"buildTransitive/{packageId}.targets"));
            Assert.NotNull(package.GetEntry("build/SupportGeneral.props"));
            Assert.NotNull(package.GetEntry("configurations/editorconfig.txt"));

            var sdkProps = ReadPackageEntry(package, "sdk/Sdk.props");
            Assert.Contains($"<HeadlessSdkName>{packageId}</HeadlessSdkName>", sdkProps, StringComparison.Ordinal);
            Assert.Contains($"Sdk=\"{baseSdk}\"", sdkProps, StringComparison.Ordinal);

            var buildProps = ReadPackageEntry(package, $"build/{packageId}.props");
            Assert.Contains(
                $"<HeadlessSdkName Condition=\"'$(HeadlessSdkName)' == ''\">{packageId}</HeadlessSdkName>",
                buildProps,
                StringComparison.Ordinal
            );
        }
    }

    [Fact]
    public async Task PackageReferenceRestoreIncludesImplicitAnalyzerPackages()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath);
        Assert.Contains("\"Meziantou.Analyzer/", assets, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.CodeAnalysis.BannedApiAnalyzers/", assets, StringComparison.Ordinal);
        Assert.Contains("\"AsyncFixer/", assets, StringComparison.Ordinal);
        Assert.Contains("Meziantou.Analyzer.dll", assets, StringComparison.Ordinal);
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers.dll", assets, StringComparison.Ordinal);
        Assert.Contains("AsyncFixer.dll", assets, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MsBuildPropertiesUseExpectedDefaults()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("LatestMajor", properties["RollForward"]);
        Assert.Equal("true", properties["PackAsTool"]);
        Assert.Equal("Headless.Sdk", properties["HeadlessSdkName"]);
        Assert.Equal("Default", properties["HeadlessSdkProjectType"]);
    }

    [Fact]
    public async Task MsBuildPropertiesInferTargetFrameworkWhenExplicitlyEnabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.Sdk/{fixture.PackageVersion}",
            targetFramework: null,
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:HeadlessInferTargetFramework=true");

        Assert.StartsWith("net", properties["TargetFramework"], StringComparison.Ordinal);
        Assert.NotEqual("net", properties["TargetFramework"]);
    }

    [Fact]
    public async Task MsBuildPropertiesUseTestProjectTypeSdk()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.Sdk.Test/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("Headless.Sdk.Test", properties["HeadlessSdkName"]);
        Assert.Equal("Test", properties["HeadlessSdkProjectType"]);
        Assert.Equal("true", properties["IsTestableProject"]);
        Assert.Equal("true", properties["IsTestProject"]);
        Assert.Equal("false", properties["IsPackable"]);
    }

    [Theory]
    [InlineData("Headless.Sdk.Web", "Microsoft.NET.Sdk.Web", "Web", "false", "false")]
    [InlineData("Headless.Sdk.Test", "Microsoft.NET.Sdk", "Test", "true", "true")]
    [InlineData("Headless.Sdk.Razor", "Microsoft.NET.Sdk.Razor", "Razor", "false", "false")]
    [InlineData(
        "Headless.Sdk.BlazorWebAssembly",
        "Microsoft.NET.Sdk.BlazorWebAssembly",
        "BlazorWebAssembly",
        "false",
        "false"
    )]
    [InlineData("Headless.Sdk.WindowsDesktop", "Microsoft.NET.Sdk.WindowsDesktop", "WindowsDesktop", "false", "false")]
    public async Task MsBuildPropertiesUseProjectTypePackageReference(
        string packageId,
        string sdkName,
        string projectType,
        string isTestableProject,
        string isTestProject
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: sdkName,
            packageReferenceId: packageId
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(packageId, properties["HeadlessSdkName"]);
        Assert.Equal(projectType, properties["HeadlessSdkProjectType"]);
        Assert.Equal(isTestableProject, properties["IsTestableProject"]);
        Assert.Equal(isTestProject, properties["IsTestProject"]);
    }

    [Theory]
    [InlineData("Headless.Sdk.Web", "Web")]
    [InlineData("Headless.Sdk.Razor", "Razor")]
    [InlineData("Headless.Sdk.BlazorWebAssembly", "BlazorWebAssembly")]
    [InlineData("Headless.Sdk.WindowsDesktop", "WindowsDesktop")]
    public async Task MsBuildPropertiesUseProjectTypeSdk(string sdkName, string projectType)
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"{sdkName}/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(sdkName, properties["HeadlessSdkName"]);
        Assert.Equal(projectType, properties["HeadlessSdkProjectType"]);
        Assert.Equal("false", properties["IsTestableProject"]);
    }

    [Fact]
    public async Task MsBuildPropertiesSkipToolPackagingForWebProjects()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: "Microsoft.NET.Sdk.Web",
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal(string.Empty, properties["PackAsTool"]);
    }

    [Fact]
    public async Task MsBuildPropertiesIncludeSingleFileEditorConfigWhenEnabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:HeadlessSingleFileApp=true");

        Assert.Equal("true", properties["HeadlessSingleFileApp"]);
        Assert.Contains(
            "Headless.Sdk.SingleFileApp.editorconfig",
            properties["EditorConfigFiles"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PackIncludesReadmeLicenseAndThirdPartyNotices()
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

    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

    private static string Quote(string value) => $"\"{value}\"";

    private static void AssertImplicitAnalyzerReference(
        IReadOnlyDictionary<string, XElement> packageReferences,
        string packageId
    )
    {
        Assert.True(packageReferences.TryGetValue(packageId, out var packageReference), $"Missing {packageId}.");
        Assert.False(string.IsNullOrWhiteSpace(packageReference.Attribute("Version")?.Value));
        Assert.Equal("true", packageReference.Attribute("IsImplicitlyDefined")?.Value);
        Assert.Equal("all", packageReference.Element("PrivateAssets")?.Value);
        Assert.Contains("analyzers", packageReference.Element("IncludeAssets")?.Value, StringComparison.Ordinal);
    }

    private static string ReadPackageEntry(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }
}

public sealed class HeadlessSdkPackageFixture : IAsyncLifetime
{
    private readonly Dictionary<string, string> packagePaths = new(StringComparer.Ordinal);

    public string PackageRootDirectory { get; private set; } = null!;

    public string PackagePath { get; private set; } = null!;

    public string PackageSourceDirectory => Path.Combine(PackageRootDirectory, "packages");

    public string PackageVersion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        PackageRootDirectory = Path.Combine(Path.GetTempPath(), "Headless.Sdk.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PackageSourceDirectory);

        var repositoryRoot = FindRepositoryRoot();
        var env = CreateDotNetEnvironment(PackageRootDirectory);
        var packageIds = new[]
        {
            "Headless.Sdk",
            "Headless.Sdk.Web",
            "Headless.Sdk.Test",
            "Headless.Sdk.Razor",
            "Headless.Sdk.BlazorWebAssembly",
            "Headless.Sdk.WindowsDesktop",
        };

        foreach (var packageId in packageIds)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
            var command = $"pack {Quote(projectPath)} -c Debug -o {Quote(PackageSourceDirectory)}";
            var result = await DotNetCommand.RunAsync(repositoryRoot, command, env);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to pack {packageId} for integration tests.{Environment.NewLine}{result.Output}"
                );
            }

            var packagePath = Directory
                .EnumerateFiles(PackageSourceDirectory, $"{packageId}.*.nupkg", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".snupkg", StringComparison.Ordinal))
                .Where(path => HasVersionSuffix(Path.GetFileName(path), packageId))
                .OrderByDescending(File.GetCreationTimeUtc)
                .FirstOrDefault();

            if (packagePath is null)
            {
                throw new InvalidOperationException(
                    $"Failed to locate packed {packageId} nupkg for integration tests."
                );
            }

            packagePaths[packageId] = packagePath;
        }

        PackagePath = packagePaths["Headless.Sdk"];
        PackageVersion = Path.GetFileNameWithoutExtension(PackagePath)
            .Replace("Headless.Sdk.", string.Empty, StringComparison.Ordinal);
    }

    public string GetPackagePath(string packageId) => packagePaths[packageId];

    private static bool HasVersionSuffix(string fileName, string packageId)
    {
        var versionStart = packageId.Length + 1;
        return fileName.Length > versionStart && char.IsDigit(fileName[versionStart]);
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(PackageRootDirectory))
            {
                Directory.Delete(PackageRootDirectory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return Task.CompletedTask;
    }

    private static Dictionary<string, string> CreateDotNetEnvironment(string tempRoot)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_HOME"] = Path.Combine(tempRoot, "dotnet-cli-home"),
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["NUGET_PACKAGES"] = Path.Combine(tempRoot, ".nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(tempRoot, ".nuget-http-cache"),
        };

        foreach (var value in env.Values)
        {
            Directory.CreateDirectory(value);
        }

        return env;
    }

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

        throw new InvalidOperationException("Could not locate repository root for integration tests.");
    }

    private static string Quote(string value) => $"\"{value}\"";
}

internal sealed class ConsumerProject : IAsyncDisposable
{
    private const string ProjectName = "ConsumerProject";

    private readonly Dictionary<string, string> environment;
    private readonly string packageSourceDirectory;

    private ConsumerProject(string rootDirectory, string packageVersion, string packageSourceDirectory)
    {
        RootDirectory = rootDirectory;
        ProjectFilePath = Path.Combine(rootDirectory, $"{ProjectName}.csproj");
        NuGetConfigPath = Path.Combine(rootDirectory, "NuGet.Config");
        EditorConfigPath = Path.Combine(rootDirectory, ".editorconfig");
        PackagesDirectory = Path.Combine(rootDirectory, "packages");
        SolutionDirectory = $"{Path.TrimEndingDirectorySeparator(rootDirectory)}{Path.DirectorySeparatorChar}";
        PackageVersion = packageVersion;
        this.packageSourceDirectory = packageSourceDirectory;
        environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_HOME"] = Path.Combine(rootDirectory, "dotnet-cli-home"),
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["NUGET_PACKAGES"] = Path.Combine(rootDirectory, ".nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(rootDirectory, ".nuget-http-cache"),
        };

        foreach (var value in environment.Values)
        {
            Directory.CreateDirectory(value);
        }
    }

    public string EditorConfigPath { get; }

    public string CSharpierIgnorePath => Path.Combine(RootDirectory, ".csharpierignore");

    public string GitAttributesPath => Path.Combine(RootDirectory, ".gitattributes");

    public string GitIgnorePath => Path.Combine(RootDirectory, ".gitignore");

    public string NuGetConfigPath { get; }

    public string PackageVersion { get; }

    public string PackagesDirectory { get; }

    public string ProjectAssetsPath => Path.Combine(RootDirectory, "obj", "project.assets.json");

    public string ProjectFilePath { get; }

    public string RootDirectory { get; }

    public string SolutionDirectory { get; }

    public static async Task<ConsumerProject> CreateAsync(
        string packageVersion,
        string packageSourceDirectory,
        string sdk = "Microsoft.NET.Sdk",
        string? targetFramework = "net8.0",
        string? outputType = null,
        string? editorConfigContent = null,
        bool enableEditorConfigCopy = false,
        bool enableDefaultConfigFilesCopy = false,
        string? warningsAsErrors = null,
        bool includePackageReference = true,
        string packageReferenceId = "Headless.Sdk",
        IReadOnlyDictionary<string, string>? extraProperties = null,
        IReadOnlyDictionary<string, string>? additionalFiles = null
    )
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "Headless.Sdk.Consumer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        var project = new ConsumerProject(rootDirectory, packageVersion, packageSourceDirectory);

        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            project.CreateProjectFile(
                sdk,
                targetFramework,
                outputType,
                enableEditorConfigCopy,
                enableDefaultConfigFilesCopy,
                warningsAsErrors,
                includePackageReference,
                packageReferenceId,
                extraProperties
            ),
            Encoding.UTF8
        );
        await File.WriteAllTextAsync(
            Path.Combine(rootDirectory, "Class1.cs"),
            """
namespace ConsumerProject;

public sealed class Class1;
""",
            Encoding.UTF8
        );
        await File.WriteAllTextAsync(project.NuGetConfigPath, project.CreateNuGetConfig(), Encoding.UTF8);

        if (editorConfigContent is not null)
        {
            await File.WriteAllTextAsync(project.EditorConfigPath, editorConfigContent, Encoding.UTF8);
        }

        if (additionalFiles is not null)
        {
            foreach (var (relativePath, content) in additionalFiles)
            {
                var filePath = Path.Combine(rootDirectory, relativePath);
                var directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
            }
        }

        return project;
    }

    public Task<DotNetCommandResult> RunDotNetAsync(string arguments) =>
        DotNetCommand.RunAsync(RootDirectory, arguments, environment);

    public async Task<Dictionary<string, string>> EvaluateHeadlessPropertiesAsync(string additionalArguments = "")
    {
        var argumentsSuffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments}";
        var result = await RunDotNetAsync(
            $"msbuild {Quote(ProjectFilePath)} /restore /t:WriteHeadlessProperties -p:RestoreConfigFile={Quote(NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -v:q -nologo{argumentsSuffix}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertiesPath = Path.Combine(RootDirectory, "headless-properties.txt");

        foreach (var line in await File.ReadAllLinesAsync(propertiesPath))
        {
            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            Assert.True(separatorIndex > 0, $"Invalid property output line: {line}");
            properties[line[..separatorIndex]] = line[(separatorIndex + 1)..];
        }

        return properties;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private string CreateNuGetConfig()
    {
        return $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{{packageSourceDirectory}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;
    }

    private string CreateProjectFile(
        string sdk,
        string? targetFramework,
        string? outputType,
        bool enableEditorConfigCopy,
        bool enableDefaultConfigFilesCopy,
        string? warningsAsErrors,
        bool includePackageReference,
        string packageReferenceId,
        IReadOnlyDictionary<string, string>? extraProperties
    )
    {
        var propertyLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            propertyLines.Add($"    <TargetFramework>{targetFramework}</TargetFramework>");
        }

        if (!string.IsNullOrWhiteSpace(outputType))
        {
            propertyLines.Add($"    <OutputType>{outputType}</OutputType>");
        }

        if (enableDefaultConfigFilesCopy)
        {
            propertyLines.Add(
                "    <HeadlessCopyDefaultConfigFilesToSolutionDir>true</HeadlessCopyDefaultConfigFilesToSolutionDir>"
            );
        }

        if (enableEditorConfigCopy)
        {
            propertyLines.Add(
                "    <HeadlessCopyEditorConfigToSolutionDir>true</HeadlessCopyEditorConfigToSolutionDir>"
            );
        }

        if (!string.IsNullOrWhiteSpace(warningsAsErrors))
        {
            propertyLines.Add($"    <WarningsAsErrors>{warningsAsErrors}</WarningsAsErrors>");
        }

        if (extraProperties is not null)
        {
            foreach (var (name, value) in extraProperties)
            {
                propertyLines.Add($"    <{name}>{value}</{name}>");
            }
        }

        var extraPropertyBlock =
            propertyLines.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}{string.Join(Environment.NewLine, propertyLines)}";

        var packageReferenceBlock = includePackageReference
            ? $$"""

                  <ItemGroup>
                    <PackageReference Include="{{packageReferenceId}}" Version="{{PackageVersion}}" PrivateAssets="all" />
                  </ItemGroup>
                """
            : string.Empty;

        return $$"""
            <Project Sdk="{{sdk}}">
              <PropertyGroup>
                <Nullable>enable</Nullable>{{extraPropertyBlock}}
              </PropertyGroup>{{packageReferenceBlock}}

              <Target Name="WriteHeadlessProperties">
                <PropertyGroup>
                  <_HeadlessEvaluatedEditorConfigFiles>@(EditorConfigFiles)</_HeadlessEvaluatedEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/headless-properties.txt"
                  Lines="TargetFramework=$(TargetFramework);RollForward=$(RollForward);PackAsTool=$(PackAsTool);HeadlessSdkName=$(HeadlessSdkName);HeadlessSdkProjectType=$(HeadlessSdkProjectType);HeadlessSingleFileApp=$(HeadlessSingleFileApp);IsTestableProject=$(IsTestableProject);IsTestProject=$(IsTestProject);IsPackable=$(IsPackable);EditorConfigFiles=$(_HeadlessEvaluatedEditorConfigFiles);VSTestSetting=$(VSTestSetting)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """;
    }

    private static string Quote(string value) => $"\"{value}\"";
}

internal static class DotNetCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        string arguments,
        IReadOnlyDictionary<string, string> environment
    )
    {
        var startInfo = new ProcessStartInfo("dotnet", AddDisableBuildServersArgument(arguments))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        foreach (string key in Environment.GetEnvironmentVariables().Keys)
        {
            var value = Environment.GetEnvironmentVariable(key);

            if (value is null)
            {
                continue;
            }

            startInfo.Environment[key] = value;
        }

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILDNOINPROCNODE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (InvalidOperationException) { }

            var timeoutOutput = $"{await standardOutputTask}{await standardErrorTask}".Trim();
            throw new TimeoutException(
                $"dotnet {arguments} timed out after {Timeout}.{Environment.NewLine}{timeoutOutput}"
            );
        }

        var output = $"{await standardOutputTask}{await standardErrorTask}";
        return new DotNetCommandResult(process.ExitCode, output.Trim());
    }

    private static string AddDisableBuildServersArgument(string arguments)
    {
        if (arguments.Contains("--disable-build-servers", StringComparison.Ordinal))
        {
            return arguments;
        }

        var separatorIndex = arguments.IndexOf(' ', StringComparison.Ordinal);
        return separatorIndex < 0
            ? $"{arguments} --disable-build-servers"
            : $"{arguments[..separatorIndex]} --disable-build-servers{arguments[separatorIndex..]}";
    }
}

internal sealed record DotNetCommandResult(int ExitCode, string Output);
