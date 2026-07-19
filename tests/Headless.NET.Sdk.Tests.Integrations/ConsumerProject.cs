using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;
using StructuredLoggerSerialization = Microsoft.Build.Logging.StructuredLogger.Serialization;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

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
        environment = DotNetCommandEnvironment.CreateIsolatedEnvironment(rootDirectory);
    }

    public string EditorConfigPath { get; }

    public string CSharpierIgnorePath => Path.Combine(RootDirectory, ".csharpierignore");

    public string BinLogPath => Path.Combine(RootDirectory, "msbuild.binlog");

    public string BuildOutputSarifPath => Path.Combine(RootDirectory, "BuildOutput.sarif");

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
        string? targetFramework = "net10.0",
        string? outputType = null,
        string? editorConfigContent = null,
        bool enableEditorConfigCopy = false,
        bool enableDefaultConfigFilesCopy = false,
        string? warningsAsErrors = null,
        bool includePackageReference = true,
        string packageReferenceId = "Headless.NET.Sdk",
        bool useCentralPackageManagement = false,
        IReadOnlyDictionary<string, string>? extraProperties = null,
        IReadOnlyDictionary<string, string>? extraPackageReferences = null,
        IReadOnlyDictionary<string, string>? additionalFiles = null
    )
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "Headless.NET.Sdk.Consumer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        var project = new ConsumerProject(rootDirectory, packageVersion, packageSourceDirectory);
        var cancellationToken = TestContext.Current.CancellationToken;

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
                useCentralPackageManagement,
                extraProperties,
                extraPackageReferences
            ),
            Encoding.UTF8,
            cancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(rootDirectory, "Class1.cs"),
            """
namespace ConsumerProject;

public sealed class Class1;
""",
            Encoding.UTF8,
            cancellationToken
        );
        await File.WriteAllTextAsync(
            project.NuGetConfigPath,
            project.CreateNuGetConfig(),
            Encoding.UTF8,
            cancellationToken
        );

        if (editorConfigContent is not null)
        {
            await File.WriteAllTextAsync(
                project.EditorConfigPath,
                editorConfigContent,
                Encoding.UTF8,
                cancellationToken
            );
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

                await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
            }
        }

        return project;
    }

    public Task<DotNetCommandResult> RunDotNetAsync(string arguments) =>
        DotNetCommand.RunAsync(RootDirectory, arguments, environment, TestContext.Current.CancellationToken);

    public async Task<BuildDiagnosticsResult> BuildAndCollectDiagnosticsAsync(string additionalArguments = "")
    {
        if (File.Exists(BinLogPath))
        {
            File.Delete(BinLogPath);
        }

        if (File.Exists(BuildOutputSarifPath))
        {
            File.Delete(BuildOutputSarifPath);
        }

        var argumentsSuffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments}";
        var result = await RunDotNetAsync(
            $"build {Quote(ProjectFilePath)}{argumentsSuffix} -p:ErrorLog={Quote($"{BuildOutputSarifPath},version=2.1")} /bl:{Quote(BinLogPath)}"
        );
        var binLogFiles = File.Exists(BinLogPath) ? ReadBinLogFiles(BinLogPath) : [];
        var sarif = await SarifFile.LoadAsync(BuildOutputSarifPath);

        return new BuildDiagnosticsResult(result.ExitCode, result.Output, binLogFiles, sarif);
    }

    public async Task<DotNetCommandResult> BuildWithBinLogAsync(string additionalArguments = "")
    {
        if (File.Exists(BinLogPath))
        {
            File.Delete(BinLogPath);
        }

        var argumentsSuffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments}";
        return await RunDotNetAsync($"build {Quote(ProjectFilePath)}{argumentsSuffix} /bl:{Quote(BinLogPath)}");
    }

    public async Task<Dictionary<string, string>> EvaluateHeadlessPropertiesAsync(string additionalArguments = "")
    {
        var argumentsSuffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments}";
        var result = await RunDotNetAsync(
            $"msbuild {Quote(ProjectFilePath)} /restore /t:WriteHeadlessProperties -p:RestoreConfigFile={Quote(NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -v:q -nologo{argumentsSuffix}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertiesPath = Path.Combine(RootDirectory, "headless-properties.txt");

        foreach (var line in await File.ReadAllLinesAsync(propertiesPath, TestContext.Current.CancellationToken))
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
              <packageSourceMapping>
                <packageSource key="local">
                  <package pattern="Headless.NET.Sdk*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
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
        bool useCentralPackageManagement,
        IReadOnlyDictionary<string, string>? extraProperties,
        IReadOnlyDictionary<string, string>? extraPackageReferences
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

        var packageReferenceLines = new List<string>();

        if (includePackageReference)
        {
            var versionAttribute = useCentralPackageManagement ? string.Empty : $@" Version=""{PackageVersion}""";
            packageReferenceLines.Add(
                $@"    <PackageReference Include=""{packageReferenceId}""{versionAttribute} PrivateAssets=""all"" />"
            );
        }

        if (extraPackageReferences is not null)
        {
            foreach (var (name, version) in extraPackageReferences)
            {
                packageReferenceLines.Add($@"    <PackageReference Include=""{name}"" Version=""{version}"" />");
            }
        }

        var packageReferenceBlock =
            packageReferenceLines.Count == 0
                ? string.Empty
                : $"""

                      <ItemGroup>
                    {string.Join(Environment.NewLine, packageReferenceLines)}
                      </ItemGroup>
                    """;

        return $$"""
            <Project Sdk="{{sdk}}">
              <PropertyGroup>
                <Nullable>enable</Nullable>{{extraPropertyBlock}}
              </PropertyGroup>{{packageReferenceBlock}}

              <Target Name="WriteHeadlessProperties">
                <PropertyGroup>
                  <_HeadlessEvaluatedEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessEvaluatedEditorConfigFiles>
                  <_HeadlessEvaluatedAdditionalFiles>@(AdditionalFiles->'%(Identity)', '|')</_HeadlessEvaluatedAdditionalFiles>
                  <_HeadlessEvaluatedNoneItems>@(None->'%(Identity)', '|')</_HeadlessEvaluatedNoneItems>
                  <_HeadlessEvaluatedPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessEvaluatedPackageReferences>
                  <_HeadlessEvaluatedRuntimeHostOptions>@(RuntimeHostConfigurationOption->'%(Identity)=%(Value)', '|')</_HeadlessEvaluatedRuntimeHostOptions>
                  <_HeadlessEvaluatedInternalsVisibleTo>@(InternalsVisibleTo, '|')</_HeadlessEvaluatedInternalsVisibleTo>
                </PropertyGroup>
                <ItemGroup>
                  <_HeadlessEvaluatedNoWarnItems Include="$(NoWarn)" />
                </ItemGroup>
                <PropertyGroup>
                  <_HeadlessEvaluatedNoWarn>@(_HeadlessEvaluatedNoWarnItems, '|')</_HeadlessEvaluatedNoWarn>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/headless-properties.txt"
                  Lines="TargetFramework=$(TargetFramework);RollForward=$(RollForward);PackAsTool=$(PackAsTool);HeadlessSdkName=$(HeadlessSdkName);HeadlessSdkProjectType=$(HeadlessSdkProjectType);IsTestHarnessProject=$(IsTestHarnessProject);IsTestProject=$(IsTestProject);IsTestingPlatformApplication=$(IsTestingPlatformApplication);GenerateRuntimeConfigurationFiles=$(GenerateRuntimeConfigurationFiles);GenerateSBOM=$(GenerateSBOM);IsPackable=$(IsPackable);EnablePackageValidation=$(EnablePackageValidation);NoWarn=$(_HeadlessEvaluatedNoWarn);EditorConfigFiles=$(_HeadlessEvaluatedEditorConfigFiles);AdditionalFiles=$(_HeadlessEvaluatedAdditionalFiles);NoneItems=$(_HeadlessEvaluatedNoneItems);PackageReferences=$(_HeadlessEvaluatedPackageReferences);MSBuildTreatWarningsAsErrors=$(MSBuildTreatWarningsAsErrors);RestoreLockedMode=$(RestoreLockedMode);HeadlessEmitInternalsVisibleToAttributes=$(HeadlessEmitInternalsVisibleToAttributes);InternalsVisibleTo=$(_HeadlessEvaluatedInternalsVisibleTo);TestingPlatformCommandLineArguments=$(TestingPlatformCommandLineArguments);PackageTags=$(PackageTags);PublishRepositoryUrl=$(PublishRepositoryUrl);RepositoryType=$(RepositoryType);RepositoryBranch=$(RepositoryBranch);IncludeSymbols=$(IncludeSymbols);SymbolPackageFormat=$(SymbolPackageFormat);DebugType=$(DebugType);HeadlessSymbolFormat=$(HeadlessSymbolFormat);Copyright=$(Copyright);RuntimeHostConfigurationOptions=$(_HeadlessEvaluatedRuntimeHostOptions);EnableSdkContainerSupport=$(EnableSdkContainerSupport);ContainerRegistry=$(ContainerRegistry);ContainerRepository=$(ContainerRepository);ContainerImageTagsMainVersionPrefix=$(ContainerImageTagsMainVersionPrefix);ContainerImageTagsIncludeLatest=$(ContainerImageTagsIncludeLatest);ContainerImageTags=$(ContainerImageTags)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """;
    }

    private static IReadOnlyCollection<string> ReadBinLogFiles(string path)
    {
        using var stream = File.OpenRead(path);
        var build = StructuredLoggerSerialization.ReadBinLog(stream);

        return [.. build.SourceFiles.Select(file => file.FullPath)];
    }
}
