using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

#nullable enable

namespace Headless.Defaults.Tests.Integrations;

[CollectionDefinition(nameof(HeadlessDefaultsPackageCollection))]
public sealed class HeadlessDefaultsPackageCollection : ICollectionFixture<HeadlessDefaultsPackageFixture>;

[Collection(nameof(HeadlessDefaultsPackageCollection))]
public sealed class SdkIntegrationTests(HeadlessDefaultsPackageFixture fixture)
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
        Assert.Contains("**/[Nn]u[Gg]et.config", csharpierIgnore, StringComparison.Ordinal);
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

        var analyzerHygiene = ReadPackageEntry(package, "build/SupportAnalyzerHygiene.targets");
        Assert.Contains("HeadlessDisableSponsorLinkAnalyzers", analyzerHygiene, StringComparison.Ordinal);
        Assert.Contains("DisableSponsorLink", analyzerHygiene, StringComparison.Ordinal);

        var testTargets = ReadPackageEntry(package, "build/SupportTestProjects.targets");
        Assert.Contains("configurations/default.runsettings", testTargets, StringComparison.Ordinal);

        var runsettings = ReadPackageEntry(package, "configurations/default.runsettings");
        Assert.Contains("<TreatNoTestsAsError>true</TreatNoTestsAsError>", runsettings, StringComparison.Ordinal);
        Assert.Contains("GitHubActionsTestLogger.dll", runsettings, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

    private static string Quote(string value) => $"\"{value}\"";

    private static string ReadPackageEntry(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }
}

public sealed class HeadlessDefaultsPackageFixture : IAsyncLifetime
{
    public string PackageRootDirectory { get; private set; } = null!;

    public string PackagePath { get; private set; } = null!;

    public string PackageSourceDirectory => Path.Combine(PackageRootDirectory, "packages");

    public string PackageVersion { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        PackageRootDirectory = Path.Combine(
            Path.GetTempPath(),
            "Headless.Defaults.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(PackageSourceDirectory);

        var repositoryRoot = FindRepositoryRoot();
        var env = CreateDotNetEnvironment(PackageRootDirectory);
        var projectPath = Path.Combine(repositoryRoot, "src", "Headless.Defaults", "Headless.Defaults.csproj");
        var command = $"pack {Quote(projectPath)} -c Debug -o {Quote(PackageSourceDirectory)}";
        var result = await DotNetCommand.RunAsync(repositoryRoot, command, env);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to pack Headless.Defaults for integration tests.{Environment.NewLine}{result.Output}"
            );
        }

        var packagePath = Directory
            .EnumerateFiles(PackageSourceDirectory, "Headless.Defaults.*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".snupkg", StringComparison.Ordinal))
            .OrderByDescending(File.GetCreationTimeUtc)
            .FirstOrDefault();

        if (packagePath is null)
        {
            throw new InvalidOperationException(
                "Failed to locate packed Headless.Defaults nupkg for integration tests."
            );
        }

        PackagePath = packagePath;
        PackageVersion = Path.GetFileNameWithoutExtension(packagePath)
            .Replace("Headless.Defaults.", string.Empty, StringComparison.Ordinal);
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
            if (File.Exists(Path.Combine(current.FullName, "headless-defaults.slnx")))
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

    public string ProjectFilePath { get; }

    public string RootDirectory { get; }

    public string SolutionDirectory { get; }

    public static async Task<ConsumerProject> CreateAsync(
        string packageVersion,
        string packageSourceDirectory,
        string? editorConfigContent = null,
        bool enableEditorConfigCopy = false,
        bool enableDefaultConfigFilesCopy = false,
        string? warningsAsErrors = null
    )
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "Headless.Defaults.Consumer",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(rootDirectory);

        var project = new ConsumerProject(rootDirectory, packageVersion, packageSourceDirectory);

        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            project.CreateProjectFile(enableEditorConfigCopy, enableDefaultConfigFilesCopy, warningsAsErrors),
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

        return project;
    }

    public Task<DotNetCommandResult> RunDotNetAsync(string arguments) =>
        DotNetCommand.RunAsync(RootDirectory, arguments, environment);

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
        bool enableEditorConfigCopy,
        bool enableDefaultConfigFilesCopy,
        string? warningsAsErrors
    )
    {
        var extraProperties = new List<string>();

        if (enableDefaultConfigFilesCopy)
        {
            extraProperties.Add(
                "    <HeadlessCopyDefaultConfigFilesToSolutionDir>true</HeadlessCopyDefaultConfigFilesToSolutionDir>"
            );
        }

        if (enableEditorConfigCopy)
        {
            extraProperties.Add(
                "    <HeadlessCopyEditorConfigToSolutionDir>true</HeadlessCopyEditorConfigToSolutionDir>"
            );
        }

        if (!string.IsNullOrWhiteSpace(warningsAsErrors))
        {
            extraProperties.Add($"    <WarningsAsErrors>{warningsAsErrors}</WarningsAsErrors>");
        }

        var extraPropertyBlock =
            extraProperties.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}{string.Join(Environment.NewLine, extraProperties)}";

        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>{{extraPropertyBlock}}
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Headless.Defaults" Version="{{PackageVersion}}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """;
    }
}

internal static class DotNetCommand
{
    public static async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        string arguments,
        IReadOnlyDictionary<string, string> environment
    )
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
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

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = $"{await standardOutputTask}{await standardErrorTask}";
        return new DotNetCommandResult(process.ExitCode, output.Trim());
    }
}

internal sealed record DotNetCommandResult(int ExitCode, string Output);
