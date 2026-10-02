using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.BlankLineVerifier;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// Unit tests for the HLS0001/HLS0002 blank-line analyzer and its code fix. Repository-only: they
// need no packed packages, so they run in seconds with the static tests.
public sealed class BlankLineAnalyzerTests
{
    // --- HLS0001: blank line before a control-transfer statement ---

    [Theory]
    [InlineData("return;", "return")]
    [InlineData("throw new System.Exception();", "throw")]
    public async Task should_report_hls0001_when_a_control_transfer_statement_follows_a_statement(
        string statement,
        string keyword
    )
    {
        var markup = $$"""
            class C
            {
                void M()
                {
                    System.Console.WriteLine();
                    {|HLS0001:{{keyword}}|}{{statement[keyword.Length..]}}
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_hls0001_for_loop_iterator_and_goto_transfers_when_no_blank_line_precedes_them()
    {
        const string markup = """
            class C
            {
                System.Collections.Generic.IEnumerable<int> M(int[] values)
                {
                    foreach (var value in values)
                    {
                        System.Console.WriteLine(value);
                        {|HLS0001:continue|};
                    }

                    while (true)
                    {
                        System.Console.WriteLine();
                        {|HLS0001:break|};
                    }

                    System.Console.WriteLine();
                    {|HLS0001:yield|} return 1;
                    System.Console.WriteLine();
                    {|HLS0001:yield|} break;
                }

                void N(int value)
                {
                    switch (value)
                    {
                        case 1:
                            System.Console.WriteLine();
                            {|HLS0001:goto|} case 2;
                        case 2:
                            System.Console.WriteLine();
                            {|HLS0001:goto|} default;
                        default:
                            System.Console.WriteLine();
                            {|HLS0001:goto|} done;
                    }

                done:
                    System.Console.WriteLine();
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_report_hls0001_when_the_statement_is_first_in_its_block_or_switch_section()
    {
        const string markup = """
            class C
            {
                int M(int value)
                {
                    if (value > 0)
                    {
                        return 1;
                    }

                    switch (value)
                    {
                        case 0:
                            return 0;
                        case -1:
                        {
                            return -1;
                        }
                        default:
                            throw new System.ArgumentOutOfRangeException(nameof(value));
                    }
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_hls0001_when_break_follows_a_statement_in_a_switch_section()
    {
        const string markup = """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 1:
                            System.Console.WriteLine();
                            {|HLS0001:break|};
                        case 2:
                            System.Console.WriteLine();

                            break;
                    }
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_report_hls0001_when_a_blank_line_precedes_the_attached_comment()
    {
        const string markup = """
            class C
            {
                int M()
                {
                    System.Console.WriteLine();

                    // The comment belongs to the return, so the blank line goes above it.
                    return 1;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_hls0001_when_only_a_comment_separates_the_statements()
    {
        const string markup = """
            class C
            {
                int M()
                {
                    System.Console.WriteLine();
                    // A comment line is not a blank line.
                    {|HLS0001:return|} 1;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_report_when_statements_share_a_line_or_a_directive_sits_between_them()
    {
        const string markup = """
            class C
            {
                int M()
                {
                    System.Console.WriteLine(); return 1;
                }

                int N()
                {
                    System.Console.WriteLine();
            #if DEBUG
                    return 1;
            #else
                    return 2;
            #endif
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_hls0001_after_a_single_line_embedded_statement_but_not_inside_it()
    {
        const string markup = """
            class C
            {
                int M(bool flag)
                {
                    if (flag) return 1;
                    {|HLS0001:return|} 0;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_top_level_statements()
    {
        const string markup = """
            System.Console.WriteLine();
            {|HLS0001:return|};
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_report_generated_code()
    {
        const string markup = """
            // <auto-generated/>
            class C
            {
                int M()
                {
                    System.Console.WriteLine();
                    return 1;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    // --- HLS0002: blank line before and after a multiline statement ---

    [Fact]
    public async Task should_report_hls0002_before_and_after_a_multiline_statement()
    {
        // "After" reports on the multiline statement's own last token, so both reports point at it.
        const string markup = """
            class C
            {
                void M(System.Collections.Generic.List<int> values)
                {
                    var count = values.Count;
                    {|HLS0002:values|}
                        .ForEach(value => System.Console.WriteLine(value)){|HLS0002:;|}
                    var total = count;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_report_hls0002_when_the_multiline_statement_is_first_and_last_in_its_block()
    {
        const string markup = """
            class C
            {
                void M(System.Collections.Generic.List<int> values)
                {
                    values
                        .ForEach(value => System.Console.WriteLine(value));
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_hls0002_for_compound_statements_that_span_lines()
    {
        const string markup = """
            class C
            {
                void M(bool flag)
                {
                    System.Console.WriteLine();
                    {|HLS0002:if|} (flag)
                    {
                        System.Console.WriteLine();
                    {|HLS0002:}|}
                    System.Console.WriteLine();
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_report_both_rules_when_a_multiline_statement_precedes_a_return()
    {
        const string markup = """
            class C
            {
                int M(System.Collections.Generic.List<int> values)
                {
                    values.ForEach(value =>
                        System.Console.WriteLine(value)
                    ){|HLS0002:;|}
                    {|HLS0001:return|} values.Count;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_not_treat_a_comment_or_documentation_comment_as_part_of_the_statement_span()
    {
        const string markup = """
            class C
            {
                void M()
                {
                    System.Console.WriteLine();
                    // A leading comment does not make the next statement multiline.
                    System.Console.WriteLine(); /* nor does a trailing one */
                    /// <summary>A misplaced documentation comment followed by a blank line.</summary>

                    return;
                }
            }
            """;

        await VerifyDiagnosticsAsync(markup, TestContext.Current.CancellationToken);
    }

    // --- Code fix ---

    [Fact]
    public async Task should_insert_one_blank_line_above_attached_comments_when_fixing()
    {
        const string markup = """
            class C
            {
                int M(System.Collections.Generic.List<int> values)
                {
                    var count = values.Count;
                    {|HLS0002:var|} doubled = values
                        .Where(value => value > 0)
                        .Select(value => value * 2)
                        .OrderBy(value => value)
                        .ToList(){|HLS0002:;|}
                    // The comment stays attached to the return.
                    {|HLS0001:return|} count;
                }
            }
            """;
        const string expected = """
            class C
            {
                int M(System.Collections.Generic.List<int> values)
                {
                    var count = values.Count;

                    var doubled = values
                        .Where(value => value > 0)
                        .Select(value => value * 2)
                        .OrderBy(value => value)
                        .ToList();

                    // The comment stays attached to the return.
                    return count;
                }
            }

            """;

        await VerifyFixAsync(markup + "\n", expected, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_fix_switch_sections_and_loops_when_fixing_all()
    {
        const string markup = """
            class C
            {
                void M(int[] values)
                {
                    foreach (var value in values)
                    {
                        switch (value)
                        {
                            case 1:
                                System.Console.WriteLine(value);
                                {|HLS0001:break|};
                            default:
                                System.Console.WriteLine();
                                {|HLS0001:continue|};
                        }
                    }
                }
            }
            """;
        const string expected = """
            class C
            {
                void M(int[] values)
                {
                    foreach (var value in values)
                    {
                        switch (value)
                        {
                            case 1:
                                System.Console.WriteLine(value);

                                break;
                            default:
                                System.Console.WriteLine();

                                continue;
                        }
                    }
                }
            }

            """;

        await VerifyFixAsync(markup + "\n", expected, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_keep_crlf_line_endings_when_fixing()
    {
        const string markup =
            "class C\r\n{\r\n    int M()\r\n    {\r\n        System.Console.WriteLine();\r\n        {|HLS0001:return|} 1;\r\n    }\r\n}\r\n";
        const string expected =
            "class C\r\n{\r\n    int M()\r\n    {\r\n        System.Console.WriteLine();\r\n\r\n        return 1;\r\n    }\r\n}\r\n";

        await VerifyFixAsync(markup, expected, TestContext.Current.CancellationToken);
    }
}
