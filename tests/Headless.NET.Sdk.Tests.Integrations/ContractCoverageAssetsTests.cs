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

// Builds and runs a test host per consumption mode, so it lives apart from the evaluation-only
// ContractCoverageTests and runs in parallel with it.
public sealed class ContractCoverageAssetsTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    private const string CodeCoveragePackageId = "Microsoft.Testing.Extensions.CodeCoverage";
    private const string LibraryName = "CoverageLibrary";

    // One native per shipped runtime family is enough to tell copied natives from absent ones.
    private static readonly string[] CodeCoverageNativeFiles =
    [
        "runtimes/linux-x64/native/libInstrumentationEngine.so",
        "runtimes/linux-musl-x64/native/libInstrumentationEngine.so",
        "runtimes/osx-x64/native/libInstrumentationEngine.dylib",
        "runtimes/win-x64/native/covrun64.dll",
    ];

    [Theory]
    [InlineData("project-sdk")]
    [InlineData("project-sdk-multi-targeting")]
    [InlineData("global-json-sdk")]
    [InlineData("additional-sdk")]
    [InlineData("package-reference")]
    public async Task should_leave_code_coverage_natives_out_of_the_output_when_coverage_is_off(string consumptionMode)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows build hosts keep the coverage natives.");

        await using var project = await CreateCoverageConsumerAsync(consumptionMode);

        await BuildAsync(project);

        foreach (var outputDirectory in OutputDirectories(project, consumptionMode))
        {
            Assert.All(CodeCoverageNativeFiles, file =>
                Assert.False(File.Exists(Path.Combine(outputDirectory, file)), file)
            );
            Assert.False(
                Directory
                    .EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                    .Any(file => Path.GetFileName(file).StartsWith("Cov_", StringComparison.Ordinal)),
                $"Coverage profiler configuration was copied to {outputDirectory}."
            );

            // The managed extension still ships, so `--coverage` stays a valid option at run time.
            Assert.True(File.Exists(Path.Combine(outputDirectory, $"{CodeCoveragePackageId}.dll")), outputDirectory);
            var library = await ReadCodeCoverageDepsLibraryAsync(outputDirectory);
            Assert.False(library.TryGetProperty("runtimeTargets", out _), library.ToString());
            Assert.True(library.TryGetProperty("runtime", out _), library.ToString());

            var run = await RunTestHostAsync(project, outputDirectory, string.Empty);
            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("Passed", run.Output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task should_leave_code_coverage_natives_out_of_a_file_based_test_app_when_coverage_is_off()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows build hosts keep the coverage natives.");

        var xunitVersion = TestRepository.ReadCentralPackageVersion("xunit.v3.mtp-v2");
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            includePackageReference: false
        );
        var appPath = Path.Combine(project.RootDirectory, "coverage-app.cs");
        await File.WriteAllTextAsync(
            appPath,
            $$"""
            #:sdk Headless.NET.Sdk.Test@{{fixture.PackageVersion}}
            #:package xunit.v3.mtp-v2@{{xunitVersion}}

            using Xunit;

            namespace CoverageApp;

            public sealed class CoverageAppTests
            {
                [Fact]
                public void passes() => Assert.True(true);
            }
            """,
            TestContext.Current.CancellationToken
        );
        var outputDirectory = Path.Combine(project.RootDirectory, "out");

        var build = await project.RunDotNetAsync($"build {Quote(appPath)} --output {Quote(outputDirectory)}");
        Assert.True(build.ExitCode == 0, build.Output);

        Assert.All(CodeCoverageNativeFiles, file =>
            Assert.False(File.Exists(Path.Combine(outputDirectory, file)), file)
        );
        Assert.True(File.Exists(Path.Combine(outputDirectory, $"{CodeCoveragePackageId}.dll")), outputDirectory);
    }

    [Fact]
    public async Task should_copy_code_coverage_natives_and_collect_coverage_when_coverage_is_enabled()
    {
        await using var project = await CreateCoverageConsumerAsync("project-sdk");

        await BuildAsync(project, "-p:EnableCodeCoverage=true");

        var outputDirectory = Assert.Single(OutputDirectories(project, "project-sdk"));
        Assert.All(CodeCoverageNativeFiles, file =>
            Assert.True(File.Exists(Path.Combine(outputDirectory, file)), file)
        );
        var library = await ReadCodeCoverageDepsLibraryAsync(outputDirectory);
        var runtimeTargets = library.GetProperty("runtimeTargets").EnumerateObject().Select(target => target.Name);
        Assert.Contains(CodeCoverageNativeFiles[0], runtimeTargets);

        await AssertLibraryCoverageAsync(project, outputDirectory);
    }

    // Module runners (`dotnet test --test-modules ... --coverage`) pass `--coverage` to hosts that
    // were built without EnableCodeCoverage, so coverage must not depend on the copied natives.
    [Fact]
    public async Task should_collect_coverage_when_coverage_is_requested_at_run_time_of_a_coverage_off_build()
    {
        await using var project = await CreateCoverageConsumerAsync("project-sdk");

        await BuildAsync(project);

        var outputDirectory = Assert.Single(OutputDirectories(project, "project-sdk"));
        Assert.All(CodeCoverageNativeFiles, file =>
            Assert.False(File.Exists(Path.Combine(outputDirectory, file)), file)
        );

        await AssertLibraryCoverageAsync(project, outputDirectory);
    }

    private async Task<ConsumerProject> CreateCoverageConsumerAsync(string consumptionMode)
    {
        var xunitVersion = TestRepository.ReadCentralPackageVersion("xunit.v3.mtp-v2");
        var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            includePackageReference: false
        );

        var targetFrameworks = string.Equals(consumptionMode, "project-sdk-multi-targeting", StringComparison.Ordinal)
            ? "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>"
            : "<TargetFramework>net8.0</TargetFramework>";
        var (projectSdk, additionalSdk, sdkReference) = consumptionMode switch
        {
            "project-sdk" or "project-sdk-multi-targeting" => (
                $"Headless.NET.Sdk.Test/{fixture.PackageVersion}",
                string.Empty,
                string.Empty
            ),
            "global-json-sdk" => ("Headless.NET.Sdk.Test", string.Empty, string.Empty),
            "additional-sdk" => (
                "Microsoft.NET.Sdk",
                $"""<Sdk Name="Headless.NET.Sdk.Test" Version="{fixture.PackageVersion}" />""",
                string.Empty
            ),
            "package-reference" => (
                "Microsoft.NET.Sdk",
                string.Empty,
                $"""<PackageReference Include="Headless.NET.Sdk.Test" Version="{fixture.PackageVersion}" PrivateAssets="all" />"""
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(consumptionMode), consumptionMode, null),
        };

        // On Linux a PackageReference consumer of the Test SDK fails to copy its apphost (MSB3030)
        // unless the project itself declares OutputType=Exe, whether or not coverage natives are
        // stripped. That defect is outside the coverage contract, so this mode declares it to keep
        // its copy-local assertions running on every platform.
        var outputType = string.Equals(consumptionMode, "package-reference", StringComparison.Ordinal)
            ? "<OutputType>Exe</OutputType>"
            : string.Empty;

        // The library sits under a name the packaged runsettings do not exclude, so a coverage
        // report that measured anything shows its module; the test assembly itself is excluded.
        Directory.CreateDirectory(Path.Combine(project.RootDirectory, LibraryName));
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="{{projectSdk}}">
              {{additionalSdk}}
              <PropertyGroup>
                {{targetFrameworks}}
                {{outputType}}
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="{{LibraryName}}/**" />
                {{sdkReference}}
                <PackageReference Include="xunit.v3.mtp-v2" Version="{{xunitVersion}}" />
                <ProjectReference Include="{{LibraryName}}/{{LibraryName}}.csproj" />
              </ItemGroup>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CoverageTests.cs"] = $$"""
                using Xunit;

                namespace ConsumerProject;

                public sealed class CoverageTests
                {
                    [Fact]
                    public void adds() => Assert.Equal(3, {{LibraryName}}.Calculator.Add(1, 2));
                }
                """,
                [$"{LibraryName}/{LibraryName}.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """,
                [$"{LibraryName}/Calculator.cs"] = $$"""
                namespace {{LibraryName}};

                public static class Calculator
                {
                    public static int Add(int left, int right)
                    {
                        return left + right;
                    }

                    public static int Subtract(int left, int right)
                    {
                        return left - right;
                    }
                }
                """,
            }
        );

        if (string.Equals(consumptionMode, "global-json-sdk", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(
                Path.Combine(project.RootDirectory, "global.json"),
                $$"""
                {
                  "msbuild-sdks": {
                    "Headless.NET.Sdk.Test": "{{fixture.PackageVersion}}"
                  }
                }
                """,
                TestContext.Current.CancellationToken
            );
        }

        return project;
    }

    // A separate restore lets the build evaluate the package-delivered test-host properties, which a
    // PackageReference consumer only sees once the package is restored.
    private static async Task BuildAsync(ConsumerProject project, string properties = "")
    {
        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} {properties}"
        );
        Assert.True(restore.ExitCode == 0, restore.Output);

        var build = await project.RunDotNetAsync($"build {Quote(project.ProjectFilePath)} --no-restore {properties}");
        Assert.True(build.ExitCode == 0, build.Output);
    }

    private static Task<DotNetCommandResult> RunTestHostAsync(
        ConsumerProject project,
        string outputDirectory,
        string arguments
    ) =>
        project.RunDotNetAsync(
            $"run --project {Quote(project.ProjectFilePath)} --no-build --no-restore --framework {Path.GetFileName(outputDirectory)} -- {arguments}"
        );

    private static IEnumerable<string> OutputDirectories(ConsumerProject project, string consumptionMode)
    {
        var frameworks = string.Equals(consumptionMode, "project-sdk-multi-targeting", StringComparison.Ordinal)
            ? new[] { "net8.0", "net10.0" }
            : ["net8.0"];
        return frameworks.Select(framework => Path.Combine(project.RootDirectory, "bin", "Debug", framework));
    }

    private async Task<JsonElement> ReadCodeCoverageDepsLibraryAsync(string outputDirectory)
    {
        var testSdkDependencies = ReadPackageDependencyVersions(fixture.GetPackagePath("Headless.NET.Sdk.Test"));
        var version = ReadExactPackageDependencyVersion(
            testSdkDependencies[CodeCoveragePackageId],
            CodeCoveragePackageId
        );
        using var deps = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "ConsumerProject.deps.json"),
                TestContext.Current.CancellationToken
            )
        );
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        return target.GetProperty($"{CodeCoveragePackageId}/{version}").Clone();
    }

    private static async Task AssertLibraryCoverageAsync(ConsumerProject project, string outputDirectory)
    {
        var settingsEvaluation = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -getProperty:HeadlessCoverageSettingsPath -nologo -v:quiet"
        );
        Assert.True(settingsEvaluation.ExitCode == 0, settingsEvaluation.Output);

        var resultsDirectory = Path.Combine(project.RootDirectory, "TestResults");
        var run = await RunTestHostAsync(
            project,
            outputDirectory,
            $"--coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --coverage-settings {Quote(settingsEvaluation.Output.Trim())} --results-directory {Quote(resultsDirectory)}"
        );
        Assert.True(run.ExitCode == 0, run.Output);

        var reportPath = Path.Combine(resultsDirectory, "coverage.cobertura.xml");
        Assert.True(File.Exists(reportPath), run.Output);
        var report = XDocument.Load(reportPath);
        var library = Assert.Single(report.Descendants("package"), package =>
            package.Attribute("name")?.Value == LibraryName
        );
        var methods = library
            .Descendants("method")
            .ToDictionary(
                method => method.Attribute("name")?.Value ?? string.Empty,
                method => method.Descendants("line").Sum(line => int.Parse(line.Attribute("hits")?.Value ?? "0")),
                StringComparer.Ordinal
            );
        Assert.True(methods["Add"] > 0, report.ToString());
        Assert.Equal(0, methods["Subtract"]);
    }
}
