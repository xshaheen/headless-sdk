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
    public static Dictionary<string, string> CreateIsolatedEnvironment(string tempRoot)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true",
            ["DOTNET_CLI_HOME"] = Path.Combine(tempRoot, "dotnet-cli-home"),
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK"] = "true",
            ["NUGET_PACKAGES"] = Path.Combine(tempRoot, ".nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(tempRoot, ".nuget-http-cache"),
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
