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

public sealed class HeadlessSdkPackageFixture : IAsyncLifetime
{
    internal static IReadOnlyList<string> MandatoryAnalyzerPackageIds { get; } =
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

    internal static IReadOnlyList<string> PackageIds { get; } =
        [
            "Headless.NET.Sdk",
            "Headless.NET.Sdk.Web",
            "Headless.NET.Sdk.Test",
            "Headless.NET.Sdk.Razor",
            "Headless.NET.Sdk.BlazorWebAssembly",
            "Headless.NET.Sdk.WindowsDesktop",
        ];

    private readonly Dictionary<string, string> packagePaths = new(StringComparer.Ordinal);
    private bool deletePackageRootDirectory;

    public string PackageRootDirectory { get; private set; } = null!;

    public string PackagePath { get; private set; } = null!;

    public string PackageSourceDirectory { get; private set; } = null!;

    public string PackageVersion { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var repositoryRoot = TestRepository.FindRoot("integration tests");
        var prepackedPackagesDirectory = Environment.GetEnvironmentVariable("HEADLESS_PACKAGES_DIR");

        if (!string.IsNullOrWhiteSpace(prepackedPackagesDirectory))
        {
            PackageRootDirectory = Path.IsPathFullyQualified(prepackedPackagesDirectory)
                ? Path.GetFullPath(prepackedPackagesDirectory)
                : Path.GetFullPath(prepackedPackagesDirectory, repositoryRoot);
            PackageSourceDirectory = PackageRootDirectory;
            LoadPackagePaths(PackageSourceDirectory);
            return;
        }

        PackageRootDirectory = Path.Combine(Path.GetTempPath(), "Headless.NET.Sdk.Tests", Guid.NewGuid().ToString("N"));
        PackageSourceDirectory = Path.Combine(PackageRootDirectory, "packages");
        deletePackageRootDirectory = true;
        Directory.CreateDirectory(PackageSourceDirectory);

        var cancellationToken = TestContext.Current.CancellationToken;
        var env = DotNetCommandEnvironment.CreateIsolatedEnvironment(PackageRootDirectory);

        foreach (var packageId in PackageIds)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
            var baseIntermediateOutputPath = EnsureTrailingDirectorySeparator(
                Path.Combine(PackageRootDirectory, "obj", packageId)
            );
            var baseOutputPath = EnsureTrailingDirectorySeparator(Path.Combine(PackageRootDirectory, "bin", packageId));
            var command =
                $"pack {Quote(projectPath)} -c Debug -o {Quote(PackageSourceDirectory)} -p:BaseIntermediateOutputPath={Quote(baseIntermediateOutputPath)} -p:BaseOutputPath={Quote(baseOutputPath)} -p:RestorePackagesWithLockFile=false -p:RestoreLockedMode=false";
            var result = await DotNetCommand.RunAsync(repositoryRoot, command, env, cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to pack {packageId} for integration tests.{Environment.NewLine}{result.Output}"
                );
            }
        }

        LoadPackagePaths(PackageSourceDirectory);
    }

    public string GetPackagePath(string packageId) => packagePaths[packageId];

    private static bool HasVersionSuffix(string fileName, string packageId)
    {
        var versionStart = packageId.Length + 1;
        return fileName.Length > versionStart && char.IsDigit(fileName[versionStart]);
    }

    private static string EnsureTrailingDirectorySeparator(string path) =>
        $"{Path.TrimEndingDirectorySeparator(path)}{Path.DirectorySeparatorChar}";

    private void LoadPackagePaths(string packageDirectory)
    {
        if (!Directory.Exists(packageDirectory))
        {
            throw new DirectoryNotFoundException($"HEADLESS_PACKAGES_DIR does not exist: '{packageDirectory}'.");
        }

        foreach (var packageId in PackageIds)
        {
            var candidates = Directory
                .EnumerateFiles(packageDirectory, $"{packageId}.*.nupkg", SearchOption.TopDirectoryOnly)
                .Where(path => HasVersionSuffix(Path.GetFileName(path), packageId))
                .ToArray();

            if (candidates.Length != 1)
            {
                throw new InvalidOperationException(
                    $"HEADLESS_PACKAGES_DIR must contain exactly one {packageId} nupkg; found {candidates.Length} in '{packageDirectory}'."
                );
            }

            packagePaths[packageId] = candidates[0];
        }

        SetPackageIdentity();
    }

    private void SetPackageIdentity()
    {
        var versions = packagePaths
            .Select(pair => Path.GetFileNameWithoutExtension(pair.Value)[(pair.Key.Length + 1)..])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (versions.Length != 1)
        {
            throw new InvalidOperationException(
                $"All six Headless SDK packages must have one consistent version; found: {string.Join(", ", versions)}."
            );
        }

        PackagePath = packagePaths["Headless.NET.Sdk"];
        PackageVersion = versions[0];
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (deletePackageRootDirectory && Directory.Exists(PackageRootDirectory))
            {
                Directory.Delete(PackageRootDirectory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[fixture] Failed to delete '{PackageRootDirectory}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[fixture] Failed to delete '{PackageRootDirectory}': {ex.Message}");
        }

        return ValueTask.CompletedTask;
    }
}
