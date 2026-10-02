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

// A class fixture over one process-wide package set. Test classes take it so they can run in
// parallel; an assembly fixture would also share it, but xUnit creates assembly fixtures even
// when no selected test needs them, which would pack all six packages for the repository-only
// tests. Packing therefore happens once, on first use. Every consumer build owns its temp root and
// NuGet folders, so this package set is the only shared state and is read-only once loaded.
public sealed class HeadlessSdkPackageFixture : IAsyncLifetime
{
    private static readonly Lazy<Task<HeadlessSdkPackageFixture>> SharedPackages = new(LoadSharedAsync);

    internal static IReadOnlyList<string> MandatoryAnalyzerPackageIds { get; } =
    [
        "AsyncFixer",
        "ErrorProne.NET.CoreAnalyzers",
        "Meziantou.Analyzer",
        "Microsoft.CodeAnalysis.BannedApiAnalyzers",
        "Microsoft.VisualStudio.Threading.Analyzers",
        "ReflectionAnalyzers",
        "Roslynator.Analyzers",
        "Roslynator.Formatting.Analyzers",
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
        var shared = await SharedPackages.Value;
        PackageRootDirectory = shared.PackageRootDirectory;
        PackagePath = shared.PackagePath;
        PackageSourceDirectory = shared.PackageSourceDirectory;
        PackageVersion = shared.PackageVersion;

        foreach (var (packageId, packagePath) in shared.packagePaths)
        {
            packagePaths[packageId] = packagePath;
        }
    }

    private static async Task<HeadlessSdkPackageFixture> LoadSharedAsync()
    {
        var shared = new HeadlessSdkPackageFixture();
        await shared.LoadAsync();

        if (shared.deletePackageRootDirectory)
        {
            // Class fixtures are disposed per class while the package set outlives all of them,
            // so the self-packed directory is removed when the test process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => shared.DeletePackageRootDirectory();
        }

        return shared;
    }

    private async Task LoadAsync()
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

        // Unique per-run version: the consumer NuGet.Config uses the host global packages folder
        // as a fallback, and fallback folders are consulted before package sources - a stable
        // (MinVer) version already present in the host cache would silently substitute stale SDK
        // assets for the freshly packed ones. A version that can never pre-exist makes that
        // impossible. MinVerSkip is required: MinVer reassigns PackageVersion inside its target,
        // silently overriding a command-line -p:Version. ("it-" keeps the prerelease identifier
        // non-numeric, so it is always valid SemVer even if the hex happens to be all digits.)
        var uniquePackVersion = $"1.0.0-it-{Guid.NewGuid():N}";

        foreach (var packageId in PackageIds)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
            var baseIntermediateOutputPath = EnsureTrailingDirectorySeparator(
                Path.Combine(PackageRootDirectory, "obj", packageId)
            );
            var baseOutputPath = EnsureTrailingDirectorySeparator(Path.Combine(PackageRootDirectory, "bin", packageId));
            var command =
                $"pack {Quote(projectPath)} -c Debug -o {Quote(PackageSourceDirectory)} -p:MinVerSkip=true -p:Version={uniquePackVersion} -p:BaseIntermediateOutputPath={Quote(baseIntermediateOutputPath)} -p:BaseOutputPath={Quote(baseOutputPath)} -p:RestorePackagesWithLockFile=false -p:RestoreLockedMode=false";
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

        // The consumer NuGet.Config uses the host global packages folder as a fallback, and
        // fallback folders are consulted before package sources. Self-packed runs use a unique
        // per-run version that can never pre-exist, but prepacked artifacts (HEADLESS_PACKAGES_DIR)
        // carry stable versions - if the host cache already holds any of them, every consumer
        // test would silently exercise the cached copy instead of the supplied artifact. Fail
        // loudly instead.
        foreach (var packageId in PackageIds)
        {
            var cachedCopy = Path.Combine(
                ConsumerProject.HostGlobalPackagesFolder,
                packageId.ToLowerInvariant(),
                PackageVersion
            );

            if (Directory.Exists(cachedCopy))
            {
                throw new InvalidOperationException(
                    $"The host NuGet cache already contains {packageId} {PackageVersion} at '{cachedCopy}'. "
                        + "The consumer fallback folder would shadow the packages under test; clear the cached "
                        + "copy or repack with a version that does not pre-exist."
                );
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void DeletePackageRootDirectory()
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
    }
}
