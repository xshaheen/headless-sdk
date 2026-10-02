using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// Guards against analyzer version bumps silently introducing rules nobody made a severity
// decision for: every diagnostic the nine mandatory analyzer packages can report must either be
// tuned in a shipped editorconfig or explicitly recorded in AnalyzerRulesAtPackageDefaults.txt
// (a conscious "package default accepted" review record). Pure source/cache check - no packaging
// fixture - modeled as a gate on Meziantou.NET.Sdk's generated-config approach.
public sealed partial class AnalyzerRuleCoverageTests
{
    private static readonly string[] MandatoryAnalyzerPackages =
    [
        "Meziantou.Analyzer",
        "Microsoft.CodeAnalysis.BannedApiAnalyzers",
        "AsyncFixer",
        "Microsoft.VisualStudio.Threading.Analyzers",
        "SmartAnalyzers.MultithreadingAnalyzer",
        "Roslynator.Analyzers",
        "Roslynator.Formatting.Analyzers",
        "ReflectionAnalyzers",
        "ErrorProne.NET.CoreAnalyzers",
    ];

    // Bundled workspace-layer helper types compiled against a newer Roslyn surface than the
    // referenced Workspaces package ("does not have an implementation" loader failures). They are
    // code-generation plumbing, never DiagnosticAnalyzers; any failed type outside these prefixes
    // still fails the gate.
    private static readonly string[] NonAnalyzerFailedTypePrefixes = ["Microsoft.CodeAnalysis.CodeGeneration."];

    private static readonly string[] MandatoryAnalyzerRuleIdPrefixes =
    [
        "AsyncFixer",
        "EPC",
        "ERP",
        "MA",
        "MT",
        "RCS",
        "REFL",
        "RS003",
        "VSTHRD",
    ];

    private static readonly string[] ShippedEditorConfigs =
    [
        "Headless.NET.Sdk.Analyzers.editorconfig",
        "Headless.NET.Sdk.Tests.editorconfig",
        "Headless.NET.Sdk.SingleFileApp.editorconfig",
        "Headless.NET.Sdk.EnforceConfigureAwait.editorconfig",
        "Headless.NET.Sdk.GuardClauses.editorconfig",
    ];

    [Fact]
    public void every_mandatory_analyzer_rule_should_have_a_reviewed_severity_decision()
    {
        var repositoryRoot = TestRepository.FindRoot("analyzer rule coverage");
        var reviewed = ReadReviewedRuleIds(repositoryRoot);
        var tuned = ReadTunedRuleIds(repositoryRoot);

        var allRules = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packageId in MandatoryAnalyzerPackages)
        {
            var version = TestRepository.ReadCentralPackageVersion(packageId);
            foreach (var ruleId in LoadSupportedDiagnosticIds(packageId, version))
            {
                allRules.TryAdd(ruleId, packageId);
            }
        }

        // Sanity floor: reflection-loading silently finding nothing would make the gate useless.
        Assert.True(allRules.Count > 450, $"Expected 450+ rules across the nine analyzers, found {allRules.Count}.");

        var uncovered = allRules.Where(rule => !tuned.Contains(rule.Key) && !reviewed.Contains(rule.Key)).ToList();
        var staleReviewed = reviewed
            .Where(ruleId => !allRules.ContainsKey(ruleId))
            .Order(StringComparer.Ordinal)
            .ToList();
        var staleTuned = tuned
            .Where(ruleId =>
                MandatoryAnalyzerRuleIdPrefixes.Any(prefix =>
                    ruleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Where(ruleId => !allRules.ContainsKey(ruleId))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            uncovered.Count == 0,
            "Analyzer rules without a reviewed severity decision (probably introduced by a version bump). "
                + "Tune each in configurations/Headless.NET.Sdk.Analyzers.editorconfig or record it in "
                + "tests/Headless.NET.Sdk.Tests.Integrations/AnalyzerRulesAtPackageDefaults.txt after review:\n"
                + string.Join('\n', uncovered.Select(rule => $"{rule.Key} ({rule.Value})"))
        );
        Assert.True(
            staleReviewed.Count == 0,
            "Reviewed analyzer rules no longer reported by any mandatory package:\n" + string.Join('\n', staleReviewed)
        );
        Assert.True(
            staleTuned.Count == 0,
            "Configured analyzer rules no longer reported by any mandatory package:\n" + string.Join('\n', staleTuned)
        );
    }

    [Fact]
    public void formatting_analyzer_policy_should_enable_only_csharpier_compatible_rules()
    {
        var repositoryRoot = TestRepository.FindRoot("formatting analyzer policy");
        var version = TestRepository.ReadCentralPackageVersion("Roslynator.Formatting.Analyzers");
        var formattingRules = LoadSupportedDiagnosticIds("Roslynator.Formatting.Analyzers", version);
        string[] expectedRules =
        [
            "RCS0001=suggestion",
            "RCS0002=suggestion",
            "RCS0003=suggestion",
            "RCS0005=suggestion",
            "RCS0006=suggestion",
            "RCS0008=suggestion",
            "RCS0009=suggestion",
            "RCS0010=suggestion",
            "RCS0012=none",
            "RCS0045=suggestion",
            "RCS0046=none",
            "RCS0056=none",
            "RCS0057=suggestion",
            "RCS0058=suggestion",
        ];

        foreach (var fileName in new[] { "Headless.NET.Sdk.Analyzers.editorconfig" })
        {
            var analyzerConfig = File.ReadAllText(
                Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "configurations", fileName)
            );
            var configuredFormattingRules = SeverityLine
                .Matches(analyzerConfig)
                .Select(match => (RuleId: match.Groups[1].Value, Severity: match.Groups[2].Value))
                .Where(setting => formattingRules.Contains(setting.RuleId, StringComparer.OrdinalIgnoreCase))
                .OrderBy(setting => setting.RuleId, StringComparer.Ordinal)
                .Select(setting => $"{setting.RuleId}={setting.Severity}")
                .ToArray();

            Assert.Equal(expectedRules, configuredFormattingRules);
            Assert.Matches(@"(?m)^roslynator_max_line_length\s*=\s*120\s*$", analyzerConfig);
            Assert.DoesNotMatch(@"(?m)^dotnet_diagnostic\.RCS0007\.severity\s*=", analyzerConfig);
            Assert.DoesNotMatch(@"(?m)^dotnet_diagnostic\.RCS0011\.severity\s*=", analyzerConfig);
        }
    }

    // This repository builds with plain Microsoft.NET.Sdk, so its own projects get analyzer settings
    // only from the root .editorconfig. eng/tools/sync_root_editorconfig.py generates it from the
    // injected configs; this test fails when they drift apart.
    [Theory]
    [InlineData("Headless.NET.Sdk.Analyzers.editorconfig", "[*.cs]")]
    [InlineData("Headless.NET.Sdk.Tests.editorconfig", "[tests/**/*.cs]")]
    public void root_editorconfig_should_match_the_injected_configs(string injectedConfig, string section)
    {
        var repositoryRoot = TestRepository.FindRoot("root editorconfig");
        var injected = ReadSettings(
            File.ReadAllLines(
                Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "configurations", injectedConfig)
            ),
            section: null
        );
        injected.Remove("is_global");
        injected.Remove("global_level");
        var root = ReadSettings(File.ReadAllLines(Path.Combine(repositoryRoot, ".editorconfig")), section);

        var mismatches = injected
            .Where(setting => !root.TryGetValue(setting.Key, out var value) || value != setting.Value)
            .Select(setting =>
                $"{setting.Key}: injected {setting.Value}, root {root.GetValueOrDefault(setting.Key) ?? "(missing)"}"
            )
            .Concat(
                root.Keys.Where(key =>
                        AnalyzerSettingPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))
                        && !injected.ContainsKey(key)
                    )
                    .Select(key => $"{key}: in root .editorconfig only")
            )
            .ToList();

        Assert.True(
            mismatches.Count == 0,
            $"Root .editorconfig {section} differs from {injectedConfig}; run "
                + "`python3 eng/tools/sync_root_editorconfig.py`:\n"
                + string.Join('\n', mismatches)
        );
    }

    // Collects key/value settings, from every occurrence of `section` when given, else from the whole file.
    private static Dictionary<string, string> ReadSettings(IEnumerable<string> lines, string? section)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                current = line;
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#') || (section is not null && current != section))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                settings[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return settings;
    }

    // A consumer .editorconfig outranks the injected configs, so a scaffold that copied analyzer
    // settings would pin each consuming repository to the SDK version it was copied from. The
    // scaffold carries editor settings only; severities, code style, and naming ship injected.
    [Fact]
    public void editorconfig_scaffold_should_not_carry_analyzer_settings()
    {
        var repositoryRoot = TestRepository.FindRoot("editorconfig scaffold");
        var scaffold = File.ReadAllLines(
            Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "configurations", "editorconfig.txt")
        );

        var analyzerSettings = scaffold
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .Where(line => AnalyzerSettingPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            analyzerSettings.Count == 0,
            "editorconfig.txt must not carry analyzer settings; they belong in the injected configs:\n"
                + string.Join('\n', analyzerSettings)
        );
    }

    private static readonly string[] AnalyzerSettingPrefixes =
    [
        "dotnet_diagnostic.",
        "dotnet_analyzer_diagnostic.",
        "dotnet_style_",
        "csharp_style_",
        "dotnet_naming_",
        "dotnet_code_quality",
        "roslynator_",
    ];

    // Multiline: the formatting policy test matches it against a whole config file.
    [GeneratedRegex(
        @"^dotnet_diagnostic\.([A-Za-z0-9]+)\.severity\s*=\s*([a-z]+)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline
    )]
    private static partial Regex SeverityLine { get; }

    [GeneratedRegex("in type '([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex LoaderFailureTypeName { get; }

    [GeneratedRegex(@"dotnet_diagnostic\.([A-Za-z0-9]+)\.severity", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex DiagnosticRegex { get; }

    private static HashSet<string> ReadTunedRuleIds(string repositoryRoot)
    {
        var tuned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configurationsDirectory = Path.Combine(repositoryRoot, "src", "Headless.NET.Sdk", "configurations");

        foreach (var fileName in ShippedEditorConfigs)
        {
            var content = File.ReadAllText(Path.Combine(configurationsDirectory, fileName));

            foreach (Match match in DiagnosticRegex.Matches(content))
            {
                tuned.Add(match.Groups[1].Value);
            }
        }

        return tuned;
    }

    private static HashSet<string> ReadReviewedRuleIds(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "tests",
            "Headless.NET.Sdk.Tests.Integrations",
            "AnalyzerRulesAtPackageDefaults.txt"
        );

        var reviewed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Split('#', 2)[0].Trim();

            if (line.Length > 0)
            {
                reviewed.Add(line);
            }
        }

        return reviewed;
    }

    private static IReadOnlyCollection<string> LoadSupportedDiagnosticIds(string packageId, string version)
    {
        var packageRoot = Path.Combine(
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
            packageId.ToLowerInvariant(),
            version
        );

        Assert.True(
            Directory.Exists(packageRoot),
            $"{packageId} {version} is not in the NuGet cache; run a repository restore first."
        );

        var analyzerDirectory = SelectAnalyzerDirectory(packageRoot);
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadContext = new AnalyzerLoadContext(analyzerDirectory);

        try
        {
            foreach (var dllPath in Directory.EnumerateFiles(analyzerDirectory, "*.dll"))
            {
                // Code-fix assemblies contain CodeFixProvider implementations, never
                // DiagnosticAnalyzers, and routinely reference IDE-only dependencies that cannot
                // resolve here (e.g. Meziantou CodeFixers -> Microsoft.Bcl.AsyncInterfaces).
                if (
                    dllPath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)
                    || dllPath.EndsWith(".CodeFixers.dll", StringComparison.OrdinalIgnoreCase)
                    || dllPath.EndsWith(".CodeFixes.dll", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                Type[] types;

                try
                {
                    types = loadContext.LoadFromAssemblyPath(dllPath).GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    // A partial load would silently drop the failed types' diagnostics from the
                    // gate - the exact false negative this test exists to prevent. The only
                    // tolerated failures are bundled workspace-layer helper types (waived by
                    // exact type-name prefix below); everything else is fatal.
                    var loaderMessages = exception
                        .LoaderExceptions.Select(loader => loader?.Message)
                        .Where(message => message is not null)
                        .Distinct()
                        .ToList();

                    var unwaived = loaderMessages
                        .Where(message =>
                        {
                            var typeMatch = LoaderFailureTypeName.Match(message!);

                            return !typeMatch.Success
                                || !NonAnalyzerFailedTypePrefixes.Any(prefix =>
                                    typeMatch.Groups[1].Value.StartsWith(prefix, StringComparison.Ordinal)
                                );
                        })
                        .ToList();

                    if (unwaived.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"{Path.GetFileName(dllPath)} failed to load "
                                + $"{exception.Types.Count(type => type is null)} type(s): "
                                + string.Join(" | ", unwaived.Take(3))
                        );
                    }

                    types = exception.Types.Where(type => type is not null).ToArray()!;
                }

                foreach (var type in types)
                {
                    if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (type.GetConstructor(Type.EmptyTypes) is null)
                    {
                        continue;
                    }

                    var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;

                    foreach (var descriptor in analyzer.SupportedDiagnostics)
                    {
                        ruleIds.Add(descriptor.Id);
                    }
                }
            }
        }
        finally
        {
            loadContext.Unload();
        }

        Assert.True(ruleIds.Count > 0, $"{packageId} {version}: no DiagnosticAnalyzer rules were discovered.");

        return ruleIds;
    }

    private static string SelectAnalyzerDirectory(string packageRoot)
    {
        // Layouts in the wild: analyzers/dotnet/roslynX.Y/cs (versioned; pick the highest folder
        // our loaded Roslyn supports), analyzers/dotnet/cs, and analyzers/cs (VS.Threading).
        var dotnetDirectory = Path.Combine(packageRoot, "analyzers", "dotnet");

        if (Directory.Exists(dotnetDirectory))
        {
            var roslynVersion = typeof(DiagnosticAnalyzer).Assembly.GetName().Version!;
            var versioned = Directory
                .EnumerateDirectories(dotnetDirectory, "roslyn*")
                .Select(directory => new
                {
                    Directory = directory,
                    Version = Version.TryParse(Path.GetFileName(directory)["roslyn".Length..], out var version)
                        ? version
                        : null,
                })
                .Where(candidate =>
                    candidate.Version is not null
                    && candidate.Version <= new Version(roslynVersion.Major, roslynVersion.Minor)
                )
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();

            if (versioned is not null)
            {
                var csDirectory = Path.Combine(versioned.Directory, "cs");

                return Directory.Exists(csDirectory) ? csDirectory : versioned.Directory;
            }

            var plainCsDirectory = Path.Combine(dotnetDirectory, "cs");

            if (Directory.Exists(plainCsDirectory))
            {
                return plainCsDirectory;
            }

            return dotnetDirectory;
        }

        var legacyCsDirectory = Path.Combine(packageRoot, "analyzers", "cs");

        Assert.True(Directory.Exists(legacyCsDirectory), $"No analyzer directory found under {packageRoot}.");

        return legacyCsDirectory;
    }

    // Redirects Roslyn/BCL references to the test's own (newer) assemblies and resolves analyzer
    // dependencies from the package's analyzer folder; collectible so packages stay isolated.
    private sealed class AnalyzerLoadContext(string analyzerDirectory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Local-first: packages like BannedApiAnalyzers name their own companion assemblies
            // Microsoft.CodeAnalysis.*, so a prefix-based Roslyn redirect would misroute them.
            // Anything not bundled in the analyzer folder (Roslyn itself, the BCL) defers to the
            // default context, ignoring the older assembly versions analyzers were compiled
            // against.
            var candidate = Path.Combine(analyzerDirectory, $"{assemblyName.Name}.dll");

            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
