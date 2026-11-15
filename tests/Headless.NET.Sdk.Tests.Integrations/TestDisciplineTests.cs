using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// The rules RS0030 cannot express, checked over this repository's test sources. RS0030 bans sleeps
// and wall-clock reads (tests/BannedSymbols.Tests.txt), because a test that waits on time is slow
// on a fast machine and flaky on a slow one. These tests keep its escape hatch honest and hold the
// two rules around it: a skip that silences a test forever names the issue that brings it back, and
// a test never carries its own timeout, because a flaky test is fixed, not given more time.
public sealed partial class TestDisciplineTests
{
    [Fact]
    public void banned_api_suppressions_should_state_why_the_test_needs_real_time()
    {
        var violations = TestSourceLines()
            .Where(line => BannedApiSuppression().IsMatch(line.Text) && !RealTimeReason().IsMatch(line.Text))
            .Select(line => line.Location)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "RS0030 suppressed without a reason. A test that needs real time says why on the pragma: "
                + "`#pragma warning disable RS0030 // real-wait: <why>` or `// wall-clock: <why>`. Otherwise "
                + "await the signal or inject TimeProvider instead:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations)
        );
    }

    [Fact]
    public void unconditional_skips_should_name_their_tracking_issue()
    {
        var lines = TestSourceLines().ToList();
        var violations = lines
            .Select((line, index) => (line, previous: index > 0 ? lines[index - 1].Text : ""))
            .Where(pair =>
                UnconditionalSkip().IsMatch(pair.line.Text)
                && !TrackingIssue().IsMatch(pair.line.Text)
                && !TrackingIssue().IsMatch(pair.previous)
            )
            .Select(pair => pair.line.Location)
            .ToList();

        // Assert.SkipUnless and SkipWhen are not checked: their condition names the capability.
        Assert.True(
            violations.Count == 0,
            "A skip with no condition needs the issue that tracks it (#123 or a URL) in its reason or on "
                + "the line above. Use Assert.SkipUnless(<capability>, ...) when the test cannot run here:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations)
        );
    }

    [Fact]
    public void tests_should_not_set_their_own_timeout()
    {
        var violations = TestSourceLines()
            .Where(line => PerTestTimeout().IsMatch(line.Text))
            .Select(line => line.Location)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Fact or Theory sets Timeout. Await the signal the test waits for; the run's timeout is the "
                + "ceiling, and a flaky test is fixed, not given more time:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations)
        );
    }

    private static IEnumerable<(string Location, string Text)> TestSourceLines()
    {
        var repositoryRoot = TestRepository.FindRoot("test discipline tests");
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var separator = Path.DirectorySeparatorChar;

        var sources = Directory
            .EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal);

        foreach (var path in sources)
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, path);
            var lineNumber = 0;

            foreach (var text in File.ReadLines(path))
            {
                lineNumber++;
                yield return ($"{relativePath}:{lineNumber}", text);
            }
        }
    }

    [GeneratedRegex(@"^\s*#pragma\s+warning\s+disable\b.*\bRS0030\b", RegexOptions.CultureInvariant)]
    private static partial Regex BannedApiSuppression();

    [GeneratedRegex(@"//.*\b(real-wait|wall-clock):\s*\S", RegexOptions.CultureInvariant)]
    private static partial Regex RealTimeReason();

    [GeneratedRegex(
        @"\bAssert\.Skip\s*\(|\[\s*(Fact|Theory|InlineData|MemberData|ClassData)\b[^\]]*\bSkip\s*=",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex UnconditionalSkip();

    [GeneratedRegex(@"#\d+|https?://", RegexOptions.CultureInvariant)]
    private static partial Regex TrackingIssue();

    [GeneratedRegex(@"\[\s*(Fact|Theory)\b[^\]]*\bTimeout\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex PerTestTimeout();
}
