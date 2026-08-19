namespace BatchRenamePro.Core.Abstractions;

/// <summary>How badly a <see cref="RuleDiagnostic"/> affects the operation.</summary>
public enum DiagnosticSeverity
{
    /// <summary>The rule still runs, but the result may not be what the user intended.</summary>
    Warning,

    /// <summary>The rule cannot run and the operation is blocked.</summary>
    Error
}

/// <summary>
/// A problem found while validating a rule, reported as a stable <see cref="Code"/> plus an English
/// fallback message.
/// </summary>
/// <remarks>
/// Core stays culture-free on purpose: the presentation layer looks the code up in its own string
/// tables so the same engine can be driven from a localized UI, a CLI or a test without carrying
/// display strings through the domain.
/// </remarks>
/// <param name="Code">Stable identifier, for example <c>rule.pattern.empty</c>.</param>
/// <param name="Message">English fallback message used when a code has no translation.</param>
/// <param name="Severity">Whether the problem blocks execution.</param>
public sealed record RuleDiagnostic(string Code, string Message, DiagnosticSeverity Severity = DiagnosticSeverity.Error)
{
    /// <summary>Creates a blocking diagnostic.</summary>
    public static RuleDiagnostic Error(string code, string message) => new(code, message);

    /// <summary>Creates a non-blocking diagnostic.</summary>
    public static RuleDiagnostic Warning(string code, string message) => new(code, message, DiagnosticSeverity.Warning);
}
