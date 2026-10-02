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

public sealed partial class SdkIntegrationTests
{
    [Fact]
    public async Task should_include_implicit_analyzer_packages_when_restoring_via_package_reference()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        var assets = await File.ReadAllTextAsync(project.ProjectAssetsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"Meziantou.Analyzer/", assets, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.CodeAnalysis.BannedApiAnalyzers/", assets, StringComparison.Ordinal);
        Assert.Contains("\"AsyncFixer/", assets, StringComparison.Ordinal);
        Assert.Contains("Meziantou.Analyzer.dll", assets, StringComparison.Ordinal);
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers.dll", assets, StringComparison.Ordinal);
        Assert.Contains("AsyncFixer.dll", assets, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_import_bundled_analyzer_editorconfig_when_building()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe",
            // This test asserts MA0047 surfaces as a warning, proving the bundled analyzer
            // editorconfig was imported. Keep warnings non-fatal explicitly for isolation.
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TreatWarningsAsErrors"] = "false",
            },
            additionalFiles: new Dictionary<string, string>
            {
                ["Sample.cs"] = """
Console.WriteLine();

class Foo { }
""",
            }
        );

        var result = await project.BuildAndCollectDiagnosticsAsync(
            $"-p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(
            result.GetBinLogFiles(),
            file => file.EndsWith("Headless.NET.Sdk.Analyzers.editorconfig", StringComparison.OrdinalIgnoreCase)
        );
        Assert.True(result.HasWarning("MA0047"), result.SarifSummary);
    }

    [Fact]
    public async Task should_not_report_configure_await_when_enforcement_is_disabled_by_default()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Repro.cs"] =
                    "namespace ConsumerProject; public static class Repro { public static async System.Threading.Tasks.Task M() => await System.Threading.Tasks.Task.Delay(1); }",
            }
        );

        // --no-incremental: analyzers do not reliably re-run on an incremental build, so force a full
        // build to make the (absent) CA2007 diagnostic deterministic.
        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("CA2007", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_exclude_known_non_blocking_sync_calls_from_vsthrd103(bool useSdkConsumption)
    {
        // A stub DbSet stands in for EF Core so the test needs no package download; the analyzer
        // matches exclusions by namespace-qualified simple type name, so the stub exercises the DbSet entry. The
        // Journal control proves VSTHRD103 still reports ordinary sync-over-async calls in the same
        // build. No caller may share a name with an Async alternative, because VSTHRD103 skips an
        // alternative named like the enclosing method. VSTHRD103 is off by default because CA1849
        // reports a superset; the shipped exclusion list serves consumers that turn it back on.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: useSdkConsumption ? $"Headless.NET.Sdk/{fixture.PackageVersion}" : "Microsoft.NET.Sdk",
            targetFramework: "net10.0",
            includePackageReference: !useSdkConsumption,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".editorconfig"] = """
                root = true

                [*.cs]
                dotnet_diagnostic.VSTHRD103.severity = warning
                """,
                ["Repro.cs"] = """
                using System.Threading.Tasks;

                namespace Microsoft.EntityFrameworkCore
                {
                    public sealed class DbSet<TEntity>
                    {
                        public void Add(TEntity entity) { }

                        public Task AddAsync(TEntity entity) => Task.CompletedTask;
                    }
                }

                namespace ConsumerProject
                {
                    public sealed class Journal
                    {
                        public void Append(string entry) { }

                        public Task AppendAsync(string entry) => Task.CompletedTask;
                    }

                    public static class Repro
                    {
                        public static async Task TrackAsync(
                            Microsoft.EntityFrameworkCore.DbSet<string> set,
                            System.IO.MemoryStream stream,
                            System.Threading.CancellationTokenSource source
                        )
                        {
                            await Task.Yield();
                            set.Add("entity");
                            stream.Write(new byte[1], 0, 1);
                            source.Cancel();
                        }

                        public static async Task RecordAsync(Journal journal)
                        {
                            await Task.Yield();
                            journal.Append("entry");
                        }
                    }
                }
                """,
            }
        );

        var result = await project.BuildAndCollectDiagnosticsAsync(
            $"--no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        var vsthrd103 = result
            .Sarif.AllResults()
            .Where(diagnostic => string.Equals(diagnostic.RuleId, "VSTHRD103", StringComparison.Ordinal))
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(
            vsthrd103.Any(message => message.Contains("Append", StringComparison.Ordinal)),
            result.SarifSummary + Environment.NewLine + result.Output
        );
        Assert.DoesNotContain(vsthrd103, message => message.Contains("Add", StringComparison.Ordinal));
        Assert.DoesNotContain(vsthrd103, message => message.Contains("Write", StringComparison.Ordinal));
        Assert.DoesNotContain(vsthrd103, message => message.Contains("Cancel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_honor_directory_build_props_overrides_for_advisory_defaults(bool useSdkConsumption)
    {
        // Advisory defaults (WarningLevel, Features, ...) are ==''-guarded so a consumer
        // Directory.Build.props value wins under BOTH consumption modes. Under PackageReference
        // consumption the Headless props evaluate AFTER Directory.Build.props, where an
        // unconditional assignment would silently override the consumer.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: useSdkConsumption ? $"Headless.NET.Sdk/{fixture.PackageVersion}" : "Microsoft.NET.Sdk",
            includePackageReference: !useSdkConsumption,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Directory.Build.props"] = """
                <Project>
                  <PropertyGroup>
                    <WarningLevel>4</WarningLevel>
                    <Features>peverify-compat</Features>
                  </PropertyGroup>
                </Project>
                """,
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("4", properties["WarningLevel"]);
        Assert.Equal("peverify-compat", properties["Features"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_keep_mandatory_baseline_authoritative_over_project_body(bool useSdkConsumption)
    {
        // Deterministic and the analysis level/mode are mandatory policy: a project-body override
        // is re-asserted away after evaluation in both consumption modes.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: useSdkConsumption ? $"Headless.NET.Sdk/{fixture.PackageVersion}" : "Microsoft.NET.Sdk",
            includePackageReference: !useSdkConsumption,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Deterministic"] = "false",
                ["AnalysisLevel"] = "latest-minimum",
                ["AnalysisMode"] = "Minimum",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["Deterministic"]);
        Assert.Equal("latest-all", properties["AnalysisLevel"]);
        Assert.Equal("All", properties["AnalysisMode"]);
    }

    [Fact]
    public async Task should_not_detect_llm_context_by_default()
    {
        // The harness neutralizes every agent variable (this suite itself usually runs under one),
        // so a plain consumer build must see no LLM context and no warning escalation.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("false", properties["HeadlessIsLlmContext"]);
        Assert.Empty(properties["MSBuildTreatWarningsAsErrors"]);
    }

    [Fact]
    public async Task should_enable_warnings_as_errors_for_llm_context_without_ci_side_effects()
    {
        // An AI coding agent driving the build gets the warning gate so it fixes warnings in the
        // same session - but must NOT inherit CI-only behavior (SBOM, locked restore).
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            environmentOverrides: new Dictionary<string, string>(StringComparer.Ordinal) { ["CLAUDECODE"] = "1" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["HeadlessIsLlmContext"]);
        Assert.Equal("true", properties["MSBuildTreatWarningsAsErrors"]);
        Assert.Equal("true", properties["CodeAnalysisTreatWarningsAsErrors"]);
        Assert.NotEqual("true", properties["ContinuousIntegrationBuild"]);
        Assert.NotEqual("true", properties["GenerateSBOM"]);
        Assert.Empty(properties["RestoreLockedMode"]);
    }

    [Fact]
    public async Task should_report_analyzer_timings_only_when_ci_build()
    {
        // Analyzer profiling costs every compile, so local and agent builds must not pay for it;
        // a project-body ContinuousIntegrationBuild must still activate it because the default
        // lives in the .targets that evaluate after the consumer project.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            environmentOverrides: new Dictionary<string, string>(StringComparer.Ordinal) { ["CLAUDECODE"] = "1" }
        );

        var agent = await project.EvaluateHeadlessPropertiesAsync();
        var ci = await project.EvaluateHeadlessPropertiesAsync("-p:ContinuousIntegrationBuild=true");
        var ciOptOut = await project.EvaluateHeadlessPropertiesAsync(
            "-p:ContinuousIntegrationBuild=true -p:ReportAnalyzer=false"
        );
        var localOptIn = await project.EvaluateHeadlessPropertiesAsync("-p:ReportAnalyzer=true");

        Assert.Equal("true", agent["HeadlessIsLlmContext"]);
        Assert.Empty(agent["ReportAnalyzer"]);
        Assert.Equal("true", ci["ReportAnalyzer"]);
        Assert.Equal("false", ciOptOut["ReportAnalyzer"]);
        Assert.Equal("true", localOptIn["ReportAnalyzer"]);

        await using var projectBodyCi = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ContinuousIntegrationBuild"] = "true",
            }
        );

        Assert.Equal("true", (await projectBodyCi.EvaluateHeadlessPropertiesAsync())["ReportAnalyzer"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_honor_local_analyzer_run_opt_outs_only_outside_ci_and_agent_builds(
        bool includePackageReference
    )
    {
        // The inner-loop opt-outs must survive both consumption modes locally, while the CI and
        // agent gates, where findings are enforced, must still run analyzers during build.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: includePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: includePackageReference,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RunAnalyzersDuringBuild"] = "false",
                ["RunAnalyzersDuringLiveAnalysis"] = "false",
            },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BannedApiConsumer.cs"] =
                    "namespace ConsumerProject; public static class BannedApiConsumer { public static System.DateTime Value => System.DateTime.Now; }",
            }
        );

        var local = await project.EvaluateHeadlessPropertiesAsync();
        var ci = await project.EvaluateHeadlessPropertiesAsync("-p:ContinuousIntegrationBuild=true");
        var agent = await project.EvaluateHeadlessPropertiesAsync("-p:HeadlessIsLlmContext=true");

        Assert.Equal("false", local["RunAnalyzersDuringBuild"]);
        Assert.Equal("false", local["RunAnalyzersDuringLiveAnalysis"]);
        Assert.Equal("true", ci["RunAnalyzersDuringBuild"]);
        Assert.Equal("true", ci["RunAnalyzersDuringLiveAnalysis"]);
        Assert.Equal("true", agent["RunAnalyzersDuringBuild"]);
        Assert.Equal("true", agent["RunAnalyzersDuringLiveAnalysis"]);

        // Roslyn honors RunAnalyzersDuringBuild only while RunAnalyzers is empty, so prove the
        // analyzers actually skip locally and run for an agent, not just the property values.
        var build =
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}";
        var localBuild = await project.RunDotNetAsync(build);
        Assert.True(localBuild.ExitCode == 0, localBuild.Output);
        Assert.DoesNotContain("RS0030", localBuild.Output, StringComparison.Ordinal);
        var agentBuild = await project.RunDotNetAsync($"{build} -p:HeadlessIsLlmContext=true");
        Assert.Contains("RS0030", agentBuild.Output, StringComparison.Ordinal);

        await using var defaults = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: includePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: includePackageReference
        );

        var defaultProperties = await defaults.EvaluateHeadlessPropertiesAsync();
        Assert.Equal("true", defaultProperties["RunAnalyzersDuringBuild"]);
        Assert.Equal("true", defaultProperties["RunAnalyzersDuringLiveAnalysis"]);
    }

    [Fact]
    public async Task should_respect_consumer_llm_context_opt_out()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessIsLlmContext"] = "false",
            },
            environmentOverrides: new Dictionary<string, string>(StringComparer.Ordinal) { ["CLAUDECODE"] = "1" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("false", properties["HeadlessIsLlmContext"]);
        Assert.Empty(properties["MSBuildTreatWarningsAsErrors"]);
    }

    [Fact]
    public async Task should_fail_build_on_warning_in_llm_context()
    {
        // CA2007 (via opt-in enforcement) is a deterministic warning source; under an agent
        // session it must escalate to an error and fail the build.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEnforceConfigureAwait"] = "true",
            },
            environmentOverrides: new Dictionary<string, string>(StringComparer.Ordinal) { ["CLAUDECODE"] = "1" },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Repro.cs"] =
                    "namespace ConsumerProject; public static class Repro { public static async System.Threading.Tasks.Task M() => await System.Threading.Tasks.Task.Delay(1); }",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CA2007", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_allow_consumer_editorconfig_to_reenable_baseline_disabled_rules()
    {
        // The baseline disables ship as editorconfig severity = none (global_level 0), so a
        // consumer's own file-scoped .editorconfig can raise any of them back. CA1031 is
        // representative: disabled by the SDK baseline, re-enabled here, and Repro.cs catches a
        // general exception that must then be reported. (CA1812 would be a poor probe: the SDK's
        // conventional InternalsVisibleTo emission makes it treat internals as externally
        // visible, so it stays silent regardless of severity.)
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TreatWarningsAsErrors"] = "false",
            },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".editorconfig"] = """
                root = true

                [*.cs]
                dotnet_diagnostic.CA1031.severity = warning
                """,
                ["Repro.cs"] =
                    "namespace ConsumerProject; public static class Repro { public static void M() { try { System.Console.WriteLine(); } catch (System.Exception) { } } }",
            }
        );

        // --no-incremental: analyzers do not reliably re-run on an incremental build.
        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("CA1031", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_report_configure_await_warning_when_enforcement_is_enabled()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net10.0",
            includePackageReference: false,
            // This test asserts CA2007 surfaces as a warning, proving the opt-in enforcement
            // editorconfig was imported. Keep warnings non-fatal explicitly for isolation.
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEnforceConfigureAwait"] = "true",
                ["TreatWarningsAsErrors"] = "false",
            },
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Repro.cs"] =
                    "namespace ConsumerProject; public static class Repro { public static async System.Threading.Tasks.Task M() => await System.Threading.Tasks.Task.Delay(1); }",
            }
        );

        // --no-incremental: analyzers do not reliably re-run on an incremental build, so force a full
        // build to make the CA2007 diagnostic deterministic.
        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("warning CA2007", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_use_expected_msbuild_property_defaults()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("LatestMajor", properties["RollForward"]);
        Assert.Equal("true", properties["PackAsTool"]);
        Assert.Equal("Headless.NET.Sdk", properties["HeadlessSdkName"]);
        Assert.Equal("Default", properties["HeadlessSdkProjectType"]);
        Assert.Equal("true", properties["IsPackable"]);
        Assert.Equal("false", properties["EnablePackageValidation"]);
        Assert.Equal("true", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.Contains("ConsumerProject.Tests.Unit", properties["InternalsVisibleTo"], StringComparison.Ordinal);

        await using var libraryProject = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );
        var libraryDefaults = await libraryProject.EvaluateHeadlessPropertiesAsync();
        Assert.Equal("true", libraryDefaults["EnablePackageValidation"]);

        var overrides = await libraryProject.EvaluateHeadlessPropertiesAsync("-p:EnablePackageValidation=false");
        Assert.Equal("false", overrides["EnablePackageValidation"]);
    }

    [Fact]
    public async Task should_skip_conventional_internals_visible_to_for_signed_projects()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal) { ["SignAssembly"] = "true" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("true", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.DoesNotContain("ConsumerProject.Tests.Unit", properties["InternalsVisibleTo"], StringComparison.Ordinal);
        Assert.Empty(properties["InternalsVisibleTo"]);
    }

    [Fact]
    public async Task should_allow_disabling_conventional_internals_visible_to_attributes()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEmitInternalsVisibleToAttributes"] = "false",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.Equal("false", properties["HeadlessEmitInternalsVisibleToAttributes"]);
        Assert.Empty(properties["InternalsVisibleTo"]);
    }

    [Fact]
    public async Task should_exclude_lscache_files_from_default_items()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string> { ["LocalState.lscache"] = "cache" }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();

        Assert.DoesNotContain("LocalState.lscache", properties["NoneItems"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task should_treat_msbuild_warnings_as_errors_on_continuous_integration()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync("-p:CI=true");

        Assert.Equal("true", properties["MSBuildTreatWarningsAsErrors"]);
        Assert.NotEqual("true", properties["RestoreLockedMode"]);
    }

    [Fact]
    public async Task should_enforce_locked_restore_when_on_continuous_integration()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            includePackageReference: false,
            extraPackageReferences: new Dictionary<string, string>(StringComparer.Ordinal) { ["Humanizer"] = "2.14.1" }
        );

        var seedResult = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:CI=true -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=false -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );
        Assert.True(seedResult.ExitCode == 0, seedResult.Output);

        var projectContent = await File.ReadAllTextAsync(
            project.ProjectFilePath,
            TestContext.Current.CancellationToken
        );
        projectContent = projectContent.Replace(
            """<PackageReference Include="Humanizer" Version="2.14.1" />""",
            """<PackageReference Include="Humanizer" Version="2.13.14" />""",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(
            project.ProjectFilePath,
            projectContent,
            Encoding.UTF8,
            TestContext.Current.CancellationToken
        );

        var lockedResult = await project.RunDotNetAsync(
            $"restore {Quote(project.ProjectFilePath)} -p:CI=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.NotEqual(0, lockedResult.ExitCode);
        Assert.Contains("NU1004", lockedResult.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_require_an_explicit_target_framework()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: null,
            includePackageReference: false
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("NETSDK1013", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_include_target_framework_gated_global_usings_when_building()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net8.0",
            additionalFiles: new Dictionary<string, string>
            {
                ["JsonConsumer.cs"] = """
namespace ConsumerProject;

public static class JsonConsumer
{
    public static string Serialize(object value) => JsonSerializer.Serialize(value);
}
""",
            }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Theory]
    [InlineData("net8.0", false, false)]
    [InlineData("net8.0", false, true)]
    [InlineData("net9.0", true, false)]
    [InlineData("net9.0", true, true)]
    public async Task should_honor_in_project_strict_system_text_json_opt_in_by_target_framework(
        string targetFramework,
        bool shouldAddOptions,
        bool usePackageReference
    )
    {
        // RuntimeHostConfigurationOption.props -- off by default; opt-in adds STJ runtime switches
        // only when the consumer target framework supports them.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: usePackageReference ? "Microsoft.NET.Sdk" : $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: targetFramework,
            outputType: "Exe",
            includePackageReference: usePackageReference,
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HeadlessEnableStrictSystemTextJsonRuntimeDefaults"] = "true",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        var options = properties["RuntimeHostConfigurationOptions"];
        var requiredConstructorOption =
            "System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault=true";
        var nullableAnnotationsOption = "System.Text.Json.Serialization.RespectNullableAnnotationsDefault=true";

        if (shouldAddOptions)
        {
            Assert.Contains(requiredConstructorOption, options, StringComparison.Ordinal);
            Assert.Contains(nullableAnnotationsOption, options, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(requiredConstructorOption, options, StringComparison.Ordinal);
            Assert.DoesNotContain(nullableAnnotationsOption, options, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task should_leave_strict_system_text_json_runtime_options_disabled_by_default()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: $"Headless.NET.Sdk/{fixture.PackageVersion}",
            targetFramework: "net9.0",
            outputType: "Exe",
            includePackageReference: false
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync();
        Assert.DoesNotContain(
            "RespectRequiredConstructorParametersDefault",
            properties["RuntimeHostConfigurationOptions"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task should_enable_container_support_when_consumed_as_web_sdk_on_github_actions()
    {
        // SupportWebContainer.targets -- only activates for Microsoft.NET.Sdk.Web on GitHub Actions.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: "Microsoft.NET.Sdk.Web",
            outputType: "Exe"
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync(
            "-p:GITHUB_ACTIONS=true -p:GITHUB_REPOSITORY=xshaheen/headless-sdk -p:GITHUB_REF=refs/heads/main -p:GITHUB_RUN_NUMBER=123"
        );

        Assert.Equal("true", properties["EnableSdkContainerSupport"]);
        Assert.Equal("ghcr.io", properties["ContainerRegistry"]);
        Assert.Equal("xshaheen/headless-sdk", properties["ContainerRepository"]);
        Assert.Equal("1.0.123;latest", properties["ContainerImageTags"]);
    }

    [Fact]
    public async Task should_preserve_explicit_web_container_overrides_on_github_actions()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            sdk: "Microsoft.NET.Sdk.Web",
            outputType: "Exe",
            extraProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EnableSdkContainerSupport"] = "false",
                ["ContainerRegistry"] = "registry.example.test",
                ["ContainerRepository"] = "custom/repository",
                ["ContainerImageTagsMainVersionPrefix"] = "9.8",
                ["ContainerImageTagsIncludeLatest"] = "false",
                ["ContainerImageTags"] = "consumer-tag",
            }
        );

        var properties = await project.EvaluateHeadlessPropertiesAsync(
            "-p:GITHUB_ACTIONS=true -p:GITHUB_REPOSITORY=xshaheen/headless-sdk -p:GITHUB_REF_NAME=main -p:GITHUB_RUN_NUMBER=42"
        );

        Assert.Equal("false", properties["EnableSdkContainerSupport"]);
        Assert.Equal("registry.example.test", properties["ContainerRegistry"]);
        Assert.Equal("custom/repository", properties["ContainerRepository"]);
        Assert.Equal("9.8", properties["ContainerImageTagsMainVersionPrefix"]);
        Assert.Equal("false", properties["ContainerImageTagsIncludeLatest"]);
        Assert.Equal("consumer-tag", properties["ContainerImageTags"]);
    }
}
