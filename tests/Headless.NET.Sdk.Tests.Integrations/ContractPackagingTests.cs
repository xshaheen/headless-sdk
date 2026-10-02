using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed class ContractPackagingTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_generate_an_spdx_sbom_inside_a_clean_consumer_package(bool usePackageReference)
    {
        // useHostFallbackFolder: false - this test's contract is that the SBOM tooling restores
        // into the clean consumer's own packages folder. A fallback-satisfied package is never
        // copied locally, and under PackageReference consumption restore evaluation cannot see
        // the package's build assets (ExcludeRestorePackageImports), so the restore-resolved
        // path property is unavailable there by design.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: usePackageReference,
            useHostFallbackFolder: false
        );
        var packageOutput = Path.Combine(project.RootDirectory, "sbom-packages");
        Directory.CreateDirectory(packageOutput);

        var result = await project.RunDotNetAsync(
            $"pack {Quote(project.ProjectFilePath)} -p:GenerateSBOM=true -p:PackageVersion=1.0.0 -o {Quote(packageOutput)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
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
}
