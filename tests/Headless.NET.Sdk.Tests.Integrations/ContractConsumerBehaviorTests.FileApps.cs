using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
{
    [Fact]
    public async Task should_run_a_dotnet_10_file_app_with_sdk_directive()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        var appPath = Path.Combine(project.RootDirectory, "contract-app.cs");
        await File.WriteAllTextAsync(
            appPath,
            $$"""
            #:sdk Headless.NET.Sdk@{{fixture.PackageVersion}}
            #:package Humanizer@2.14.1

            using Humanizer;
            using System.Reflection;

            var sdkName = Assembly
                .GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "Headless.NET.Sdk.SdkName")
                .Value;
            Console.WriteLine($"SDK={sdkName};JSON_TYPE={typeof(JsonSerializer).FullName};HUMANIZED={2.ToWords()};NOW={DateTime.Now:O}");
            """,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var result = await project.RunDotNetAsync($"run --file {Quote(appPath)}");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("SDK=Headless.NET.Sdk", result.Output, StringComparison.Ordinal);
        Assert.Contains("JSON_TYPE=System.Text.Json.JsonSerializer", result.Output, StringComparison.Ordinal);
        Assert.Contains("HUMANIZED=two", result.Output, StringComparison.Ordinal);
        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_run_every_sdk_family_member_as_a_ci_file_app()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        foreach (var packageId in HeadlessSdkPackageFixture.PackageIds)
        {
            var appPath = Path.Combine(project.RootDirectory, $"{packageId}-contract-app.cs");
            await File.WriteAllTextAsync(
                appPath,
                $$"""
                #:sdk {{packageId}}@{{fixture.PackageVersion}}
                #:property ContinuousIntegrationBuild=true

                using System.Reflection;

                var sdkName = Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(attribute => attribute.Key == "Headless.NET.Sdk.SdkName")
                    .Value;
                Console.WriteLine($"SDK={sdkName}");
                """,
                Encoding.UTF8,
                TestContext.Current.CancellationToken
            );

            var result = await project.RunDotNetAsync($"run --file {Quote(appPath)}");

            Assert.True(result.ExitCode == 0, $"{packageId}:{Environment.NewLine}{result.Output}");
            Assert.Contains($"SDK={packageId}", result.Output, StringComparison.Ordinal);
        }
    }
}
