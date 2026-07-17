using System;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class WindowsPlatformContractTests(HeadlessSdkPackageFixture fixture)
{
    [Fact]
    public async Task should_build_windows_desktop_consumer_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.WindowsDesktop/{fixture.PackageVersion}",
            targetFramework: "net10.0-windows",
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
    }
}

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class MacOsPlatformContractTests(HeadlessSdkPackageFixture fixture)
{
    [Fact]
    public async Task should_build_base_sdk_consumer_on_macos()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
    }
}
