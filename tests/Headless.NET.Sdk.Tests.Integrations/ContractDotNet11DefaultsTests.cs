using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

// Defaults that target .NET 11 (runtime async, updated memory-safety rules) or opt-in publish
// scenarios (Native AOT). Targets set them, because the target framework and package references are
// final only after the project body, so the tests query the evaluated value after running the target.
public sealed class ContractDotNet11DefaultsTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    private const string RuntimeAsyncTarget = "HeadlessConfigureRuntimeAsync";
    private const string MemorySafetyTarget = "HeadlessConfigureUpdatedMemorySafetyRules";
    private const string SkipDocumentationTarget = "HeadlessSkipDocumentationFilesForNativeAot";
    private const string AsyncSource = """
        namespace ConsumerProject;

        public static class Worker
        {
            public static async Task<int> ComputeAsync(int value)
            {
                await Task.Yield();
                return value + 1;
            }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_enable_runtime_async_for_net11_and_honor_the_opt_out_and_webassembly_exclusion(
        bool usePackageReference
    )
    {
        Assert.SkipUnless(TestRepository.ConsumerSdkMajor >= 11, "A net11.0 consumer needs the .NET 11 SDK.");

        await using var project = await CreateRestoredProjectAsync("net11.0", usePackageReference);

        var features = await GetPropertyAfterTargetAsync(project, RuntimeAsyncTarget, "Features");
        Assert.Contains("runtime-async=on", features, StringComparison.Ordinal);

        var optedOut = await GetPropertyAfterTargetAsync(
            project,
            RuntimeAsyncTarget,
            "Features",
            "-p:HeadlessRuntimeAsync=false"
        );
        Assert.DoesNotContain("runtime-async", optedOut, StringComparison.Ordinal);

        var webAssembly = await GetPropertyAfterTargetAsync(
            project,
            RuntimeAsyncTarget,
            "Features",
            "-p:RuntimeIdentifier=browser-wasm"
        );
        Assert.DoesNotContain("runtime-async", webAssembly, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public async Task should_not_enable_runtime_async_when_targeting_a_framework_older_than_net11(
        string targetFramework
    )
    {
        await using var project = await CreateRestoredProjectAsync(targetFramework, usePackageReference: false);

        var features = await GetPropertyAfterTargetAsync(project, RuntimeAsyncTarget, "Features");

        Assert.DoesNotContain("runtime-async", features, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_compile_with_runtime_async_only_for_the_net11_target_of_a_multi_targeted_project(
        bool usePackageReference
    )
    {
        Assert.SkipUnless(TestRepository.ConsumerSdkMajor >= 11, "A net11.0 consumer needs the .NET 11 SDK.");

        await using var project = await CreateRestoredProjectAsync("net10.0", usePackageReference);
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="{{(usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}")}}">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              {{(
                usePackageReference
                    ? $"<ItemGroup><PackageReference Include=\"Headless.NET.Sdk\" Version=\"{fixture.PackageVersion}\" PrivateAssets=\"all\" /></ItemGroup>"
                    : string.Empty
            )}}
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Worker.cs"] = AsyncSource }
        );

        var build = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(build.ExitCode == 0, build.Output);

        // A compiler-generated async state machine is a nested type named after its method, so it is
        // present in the assembly metadata exactly when the method was not compiled with runtime async.
        Assert.True(HasAsyncStateMachine(project, "net10.0"), "net10.0 must keep the compiler state machine.");
        Assert.False(HasAsyncStateMachine(project, "net11.0"), "net11.0 must compile with runtime async.");

        var optedOut = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:HeadlessRuntimeAsync=false -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(optedOut.ExitCode == 0, optedOut.Output);
        Assert.True(HasAsyncStateMachine(project, "net11.0"), "The opt-out must restore the compiler state machine.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_allow_unsafe_blocks_and_skip_documentation_files_for_native_aot_by_default(
        bool usePackageReference
    )
    {
        await using var project = await CreateRestoredProjectAsync("net10.0", usePackageReference);

        Assert.Equal("true", await GetPropertyAsync(project, "AllowUnsafeBlocks"));
        Assert.Equal("true", await GetPropertyAsync(project, "IlcGenerateMstatFile"));
        Assert.Equal("true", await GetPropertyAsync(project, "IlcGenerateDgmlFile"));
        Assert.Equal("false", await GetPropertyAsync(project, "AllowUnsafeBlocks", "-p:AllowUnsafeBlocks=false"));

        // The SDK defaults these to true before the project decides PublishAot, so a target turns them off.
        foreach (var property in new[] { "PublishDocumentationFiles", "PublishDocumentationFile" })
        {
            Assert.Equal("true", await GetPropertyAsync(project, property));
            Assert.Equal("true", await GetPropertyAfterTargetAsync(project, SkipDocumentationTarget, property));
            Assert.Equal(
                "false",
                await GetPropertyAfterTargetAsync(project, SkipDocumentationTarget, property, "-p:PublishAot=true")
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_enable_updated_memory_safety_rules_only_for_preview_projects_without_cswin32(
        bool usePackageReference
    )
    {
        await using var project = await CreateRestoredProjectAsync("net10.0", usePackageReference);

        var latest = await GetPropertyAfterTargetAsync(project, MemorySafetyTarget, "Features");
        Assert.DoesNotContain("updated-memory-safety-rules", latest, StringComparison.Ordinal);

        var preview = await GetPropertyAfterTargetAsync(
            project,
            MemorySafetyTarget,
            "Features",
            "-p:LangVersion=preview"
        );
        Assert.Contains("updated-memory-safety-rules", preview, StringComparison.Ordinal);

        var optedOut = await GetPropertyAfterTargetAsync(
            project,
            MemorySafetyTarget,
            "Features",
            "-p:LangVersion=preview -p:HeadlessUpdatedMemorySafetyRules=false"
        );
        Assert.DoesNotContain("updated-memory-safety-rules", optedOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_skip_updated_memory_safety_rules_when_the_project_references_cswin32()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            evaluationOnlyPackageReferences: ["Microsoft.Windows.CsWin32"]
        );
        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(restore.ExitCode == 0, restore.Output);

        var features = await GetPropertyAfterTargetAsync(
            project,
            "_HeadlessAddEvaluationOnlyPackageReferences;" + MemorySafetyTarget,
            "Features",
            "-p:LangVersion=preview"
        );

        Assert.DoesNotContain("updated-memory-safety-rules", features, StringComparison.Ordinal);
    }

    private async Task<ConsumerProject> CreateRestoredProjectAsync(string targetFramework, bool usePackageReference)
    {
        var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: targetFramework,
            includePackageReference: usePackageReference
        );
        await File.WriteAllTextAsync(
            Path.Combine(project.RootDirectory, "Worker.cs"),
            AsyncSource,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var restore = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(restore.ExitCode == 0, restore.Output);

        return project;
    }

    private static async Task<string> GetPropertyAsync(
        ConsumerProject project,
        string property,
        string additionalArguments = ""
    )
    {
        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -getProperty:{property} -nologo -v:quiet {additionalArguments}"
        );
        Assert.True(result.ExitCode == 0, result.Output);

        return result.Output.Trim();
    }

    private static async Task<string> GetPropertyAfterTargetAsync(
        ConsumerProject project,
        string target,
        string property,
        string additionalArguments = ""
    )
    {
        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -t:{target} -getProperty:{property} -nologo -v:quiet {additionalArguments}"
        );
        Assert.True(result.ExitCode == 0, result.Output);

        return result.Output.Trim();
    }

    private static bool HasAsyncStateMachine(ConsumerProject project, string framework)
    {
        var assemblyPath = Path.Combine(project.RootDirectory, "bin", "Debug", framework, "ConsumerProject.dll");
        Assert.True(File.Exists(assemblyPath), assemblyPath);

        return Encoding
            .UTF8.GetString(File.ReadAllBytes(assemblyPath))
            .Contains("<ComputeAsync>d__", StringComparison.Ordinal);
    }
}
