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

internal static class TestRepository
{
    public static string ReadCentralPackageVersion(string packageId)
    {
        var repositoryRoot = FindRoot($"central package version for {packageId}");
        var document = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        var package = document
            .Descendants("PackageVersion")
            .Single(element => string.Equals(element.Attribute("Include")?.Value, packageId, StringComparison.Ordinal));

        return package.Attribute("Version")?.Value
            ?? throw new InvalidOperationException($"PackageVersion {packageId} does not declare Version.");
    }

    /// <summary>
    /// Names the SDK version that consumer builds run under. Unset, consumer builds use whatever the
    /// <c>dotnet</c> on PATH resolves (the repository-pinned SDK in CI). Set, every consumer project
    /// gets a <c>global.json</c> that pins it, so one suite validates the packages under each
    /// supported SDK.
    /// </summary>
    public const string ConsumerSdkVersionVariable = "HEADLESS_CONSUMER_SDK_VERSION";

    public static bool IsConsumerSdkPinned =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConsumerSdkVersionVariable));

    public static string ConsumerSdkVersion =>
        IsConsumerSdkPinned
            ? Environment.GetEnvironmentVariable(ConsumerSdkVersionVariable)!.Trim()
            : ReadRepositorySdkVersion();

    public static int ConsumerSdkMajor =>
        int.Parse(ConsumerSdkVersion.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The <c>"sdk"</c> entry (with trailing separator) that pins the consumer SDK, or an empty
    /// string when the consumer SDK is not pinned. Splice it first inside a <c>global.json</c> object.
    /// </summary>
    public static string ConsumerSdkPinJson =>
        IsConsumerSdkPinned
            ? $"\"sdk\": {{ \"version\": \"{ConsumerSdkVersion}\", \"rollForward\": \"latestFeature\", \"allowPrerelease\": false }},{Environment.NewLine}  "
            : string.Empty;

    public static string ReadRepositorySdkVersion()
    {
        using var globalJson = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRoot("repository SDK pin"), "global.json"))
        );
        var version = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidOperationException("global.json does not pin an SDK version.")
            : version;
    }

    public static string FindRoot(string purpose)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "headless-sdk.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root for {purpose}.");
    }
}

internal static class DotNetCommandEnvironment
{
    // One HTTP cache for every consumer build, kept across runs. Each consumer restore otherwise
    // starts cold and downloads nuget.org's vulnerability feed (and any package missing from the
    // fallback folder) again, which turns every restore into a network round trip and makes the
    // suite's duration follow network latency. The cache holds feed responses and downloaded
    // .nupkg files only; NuGet locks it across processes, and each consumer keeps its own
    // NUGET_PACKAGES, so no consumer sees another's restored state.
    private static readonly string SharedHttpCachePath = Path.Combine(
        Path.GetTempPath(),
        "Headless.NET.Sdk.Tests",
        "nuget-http-cache"
    );

    public static Dictionary<string, string> CreateIsolatedEnvironment(string tempRoot)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true",
            ["DOTNET_CLI_HOME"] = Path.Combine(tempRoot, "dotnet-cli-home"),
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            // SDK 11 starts a persistent MSBuild server by default. A warm server would carry state
            // across consumer builds and can keep the harness's output pipes open after the command exits.
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",
            ["MSBUILDUSESERVER"] = "0",
            ["NUGET_PACKAGES"] = Path.Combine(tempRoot, ".nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = SharedHttpCachePath,
        };

        foreach (var pathKey in new[] { "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH" })
        {
            Directory.CreateDirectory(environment[pathKey]);
        }

        AddNeutralBuildEnvironment(environment);

        return environment;
    }

    public static void AddNeutralBuildEnvironment(IDictionary<string, string> environment)
    {
        // Workflow-level environment variables should not change default consumer-project behavior.
        environment["CONFIGURATION"] = "Debug";
        environment["CI"] = "false";
        environment["TF_BUILD"] = "false";
        environment["GITHUB_ACTIONS"] = "false";
        environment["GITLAB_CI"] = "false";
        environment["TEAMCITY_VERSION"] = string.Empty;
        environment["BUILD_COMMAND"] = string.Empty;
        environment["APPVEYOR"] = string.Empty;
        environment["TRAVIS"] = "false";
        environment["CIRCLECI"] = "false";
        environment["CODEBUILD_BUILD_ID"] = string.Empty;
        environment["AWS_REGION"] = string.Empty;
        environment["JENKINS_URL"] = string.Empty;
        environment["BUILD_ID"] = string.Empty;
        environment["BUILD_URL"] = string.Empty;
        environment["PROJECT_ID"] = string.Empty;
        environment["JB_SPACE_API_URL"] = string.Empty;

        // AI coding-agent variables (SupportDetectLlmContext.props). These leak from the very
        // sessions that run this suite — Claude Code sets CLAUDECODE — so every one must be
        // neutralized for consumer builds to behave like a plain developer machine.
        environment["CLAUDECODE"] = string.Empty;
        environment["CLAUDE_CODE_ENTRYPOINT"] = string.Empty;
        environment["CURSOR_EDITOR"] = string.Empty;
        environment["CURSOR_AI"] = string.Empty;
        environment["GH_COPILOT_WORKING_DIRECTORY"] = string.Empty;
        environment["COPILOT_CLI"] = string.Empty;
        environment["COPILOT_AGENT"] = string.Empty;
        environment["CODEX_CLI"] = string.Empty;
        environment["CODEX_SANDBOX"] = string.Empty;
        environment["AMP_HOME"] = string.Empty;
        environment["QWEN_CODE"] = string.Empty;
        environment["OPENCODE_AI"] = string.Empty;
        environment["ZED_ENVIRONMENT"] = string.Empty;
        environment["ZED_TERM"] = string.Empty;
        environment["GOOSE_TERMINAL"] = string.Empty;
        environment["CLINE_TASK_ID"] = string.Empty;
        environment["ROO_CODE_TASK_ID"] = string.Empty;
        environment["WINDSURF_SESSION"] = string.Empty;
        environment["GEMINI_CLI"] = string.Empty;
        environment["GITHUB_COPILOT_CLI_MODE"] = string.Empty;
        environment["DROID_CLI"] = string.Empty;
        environment["KIMI_CLI"] = string.Empty;
        environment["AGENT_CLI"] = string.Empty;
        environment["OR_APP_NAME"] = string.Empty;
    }
}

internal sealed record BuildDiagnosticsResult(
    int ExitCode,
    string Output,
    IReadOnlyCollection<string> BinLogFiles,
    SarifFile Sarif
)
{
    public string SarifSummary =>
        string.Join(Environment.NewLine, Sarif.AllResults().Select(result => result.ToString()));

    public bool HasWarning(string ruleId) =>
        Sarif
            .AllResults()
            .Any(result =>
                string.Equals(result.Level, "warning", StringComparison.Ordinal)
                && string.Equals(result.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)
            );

    public IReadOnlyCollection<string> GetBinLogFiles() => BinLogFiles;
}

internal sealed class SarifFile
{
    [JsonPropertyName("runs")]
    public SarifRun[] Runs { get; init; } = [];

    public static async Task<SarifFile> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new SarifFile();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SarifFile>(stream) ?? new SarifFile();
    }

    public IEnumerable<SarifResult> AllResults() => Runs.SelectMany(run => run.Results);
}

internal sealed class SarifRun
{
    [JsonPropertyName("results")]
    public SarifResult[] Results { get; init; } = [];
}

internal sealed class SarifResult
{
    [JsonPropertyName("level")]
    public string? Level { get; init; }

    [JsonPropertyName("message")]
    public JsonElement Message { get; init; }

    [JsonPropertyName("ruleId")]
    public string? RuleId { get; init; }

    public override string ToString() => $"{Level}: {RuleId}: {GetMessageText()}";

    private string? GetMessageText()
    {
        if (Message.ValueKind == JsonValueKind.String)
        {
            return Message.GetString();
        }

        if (
            Message.ValueKind == JsonValueKind.Object
            && Message.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
        )
        {
            return text.GetString();
        }

        return Message.ValueKind == JsonValueKind.Undefined ? null : Message.ToString();
    }
}

internal static class DotNetCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static readonly string[] InheritedSdkLocationVariables =
    [
        "MSBUILD_EXE_PATH",
        "MSBuildExtensionsPath",
        "MSBuildSDKsPath",
        "DOTNET_HOST_PATH",
        "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER",
    ];

    public static async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        string arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo("dotnet", AddDisableBuildServersArgument(arguments))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        // MTP sets this marker on the outer dotnet test process. Nested clean-consumer test runs
        // must be independent CLI invocations; the marker's presence, even empty, suppresses them.
        startInfo.Environment.Remove("DOTNET_CLI_TEST_COMMAND_WORKING_DIRECTORY");

        // The outer `dotnet test` (the repository-pinned SDK) exports its own MSBuild and SDK locations.
        // A consumer pinned to a different SDK would mix that SDK's CLI with these paths and fail to
        // start MSBuild task hosts, so each nested invocation must resolve its own SDK from scratch.
        foreach (var name in InheritedSdkLocationVariables)
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILDNOINPROCNODE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                // A token already fired (caller cancellation or our timeout); wait unconditionally for the kill to settle.
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (InvalidOperationException) { }

            // Distinguish caller cancellation from the internal timeout: if the caller's token fired,
            // propagate real cancellation semantics rather than masking it as a command timeout.
            cancellationToken.ThrowIfCancellationRequested();

            var timeoutOutput = $"{await standardOutputTask}{await standardErrorTask}".Trim();
            throw new TimeoutException(
                $"dotnet {arguments} timed out after {Timeout}.{Environment.NewLine}{timeoutOutput}"
            );
        }

        var output = $"{await standardOutputTask}{await standardErrorTask}";
        return new DotNetCommandResult(process.ExitCode, output.Trim());
    }

    private static string AddDisableBuildServersArgument(string arguments)
    {
        if (arguments.Contains("--disable-build-servers", StringComparison.Ordinal))
        {
            return arguments;
        }

        var separatorIndex = arguments.IndexOf(' ', StringComparison.Ordinal);
        return separatorIndex < 0
            ? $"{arguments} --disable-build-servers"
            : $"{arguments[..separatorIndex]} --disable-build-servers{arguments[separatorIndex..]}";
    }

    public static string Quote(string value) => $"\"{value}\"";
}

internal sealed record DotNetCommandResult(int ExitCode, string Output);
