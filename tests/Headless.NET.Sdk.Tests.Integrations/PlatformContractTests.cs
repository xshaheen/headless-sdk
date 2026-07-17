using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed class WindowsPlatformContractTests(HeadlessSdkPackageFixture fixture)
{
    [Theory]
    [InlineData("UseWPF")]
    [InlineData("UseWindowsForms")]
    public async Task should_build_real_windows_desktop_consumers_on_windows(string desktopProperty)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var additionalFiles =
            desktopProperty == "UseWPF"
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MainWindow.xaml"] = """
                        <Window x:Class="ConsumerProject.MainWindow"
                                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                          <Grid />
                        </Window>
                        """,
                    ["MainWindow.xaml.cs"] = """
                        using System.Windows;

                        namespace ConsumerProject;

                        public partial class MainWindow : Window
                        {
                            public MainWindow() => InitializeComponent();
                        }
                        """,
                }
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MainForm.cs"] = """
                        using System.Windows.Forms;

                        namespace ConsumerProject;

                        public sealed class MainForm : Form;
                        """,
                };

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk.WindowsDesktop/{fixture.PackageVersion}",
            targetFramework: "net10.0-windows",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { [desktopProperty] = "true" },
            additionalFiles: additionalFiles
        );

        var result = await project.BuildWithBinLogAsync(
            $"-p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        TestContext.Current.AddAttachment(
            $"windows-desktop-{desktopProperty}-msbuild.binlog",
            await File.ReadAllBytesAsync(project.BinLogPath, TestContext.Current.CancellationToken)
        );
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

        var result = await project.BuildWithBinLogAsync(
            $"-p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        TestContext.Current.AddAttachment(
            "macos-base-msbuild.binlog",
            await File.ReadAllBytesAsync(project.BinLogPath, TestContext.Current.CancellationToken)
        );
    }
}
