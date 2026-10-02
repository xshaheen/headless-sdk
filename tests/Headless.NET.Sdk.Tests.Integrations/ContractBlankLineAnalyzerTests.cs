using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// The HLS analyzers ship inside the packages rather than as a NuGet dependency, so these consumer
// builds prove the injected assembly loads, exactly once, in each consumption mode.
public sealed class ContractBlankLineAnalyzerTests(HeadlessSdkPackageFixture fixture) : ContractConsumerBehaviorTests
{
    private const string Violations = """
        namespace ConsumerProject;

        public static class BlankLines
        {
            public static int Count(System.Collections.Generic.List<int> values)
            {
                var count = values.Count;
                values.ForEach(value =>
                    System.Console.WriteLine(value)
                );
                return count;
            }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_report_blank_line_rules_at_suggestion_severity_in_package_and_sdk_consumption(
        bool useSdkConsumption
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: useSdkConsumption ? $"Headless.NET.Sdk/{fixture.PackageVersion}" : "Microsoft.NET.Sdk",
            targetFramework: "net10.0",
            includePackageReference: !useSdkConsumption,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal) { ["BlankLines.cs"] = Violations }
        );

        var result = await project.BuildAndCollectDiagnosticsAsync(
            $"--no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        // SARIF records Info, the "suggestion" severity, as "note". One report per gap also proves
        // the analyzer assembly was loaded once, not once per consumption path.
        Assert.Equal(["note"], Levels(result, "HLS0001"));
        Assert.Equal(["note", "note"], Levels(result, "HLS0002"));
    }

    [Fact]
    public async Task should_not_report_blank_line_rules_when_the_consumer_editorconfig_turns_them_off()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            editorConfigContent: """
            root = true

            [*.cs]
            dotnet_diagnostic.HLS0001.severity = none
            dotnet_diagnostic.HLS0002.severity = none
            """,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal) { ["BlankLines.cs"] = Violations }
        );

        var result = await project.BuildAndCollectDiagnosticsAsync(
            $"--no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(Levels(result, "HLS0001"));
        Assert.Empty(Levels(result, "HLS0002"));
    }

    private static string?[] Levels(BuildDiagnosticsResult result, string ruleId) =>
        [
            .. result
                .Sarif.AllResults()
                .Where(diagnostic => string.Equals(diagnostic.RuleId, ruleId, StringComparison.Ordinal))
                .Select(diagnostic => diagnostic.Level),
        ];
}
