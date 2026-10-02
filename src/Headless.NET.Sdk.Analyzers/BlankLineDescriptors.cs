using Microsoft.CodeAnalysis;

namespace Headless.NET.Sdk.Analyzers;

/// <summary>The blank-line rules shipped with the Headless SDK.</summary>
public static class BlankLineDescriptors
{
    /// <summary>Rule ID for a missing blank line before a control-transfer statement.</summary>
    public const string BeforeControlTransferId = "HLS0001";

    /// <summary>Rule ID for a missing blank line before or after a multiline statement.</summary>
    public const string AroundMultilineStatementId = "HLS0002";

    /// <summary>
    /// Diagnostic property naming where the blank line goes relative to the reported token. Only
    /// <c>after</c> is set: the reported token then ends the statement that needs the blank line
    /// after it.
    /// </summary>
    public const string PlacementProperty = "Placement";

    /// <summary>The <see cref="PlacementProperty"/> value for a blank line after the reported token.</summary>
    public const string PlacementAfter = "After";

    private const string _Category = "Formatting";
    private const string _HelpLinkBase = "https://github.com/xshaheen/headless-sdk#blank-line-rules";

    public static readonly DiagnosticDescriptor BeforeControlTransfer = new(
        BeforeControlTransferId,
        title: "Add a blank line before a control-transfer statement",
        messageFormat: "Add a blank line before '{0}'",
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A return, throw, break, continue, goto, or yield statement is preceded by a blank line unless it is the first statement of its block or switch section.",
        helpLinkUri: _HelpLinkBase
    );

    public static readonly DiagnosticDescriptor AroundMultilineStatement = new(
        AroundMultilineStatementId,
        title: "Add a blank line around a multiline statement",
        messageFormat: "Add a blank line {0} this multiline statement",
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A statement that spans more than one line is separated from its sibling statements by a blank line before and after it.",
        helpLinkUri: _HelpLinkBase
    );
}
