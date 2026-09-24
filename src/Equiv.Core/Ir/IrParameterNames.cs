namespace Equiv.Core.Ir;

/// <summary>
/// The rule that tells a source-language parameter from an input the frontend synthesised (VERIFICATION-MODEL.md
/// section 2; ADR 0021). A synthesised input is the receiver <c>this</c> or has a dot in its name; a source-language
/// parameter never is. A frontend spells a parameter whose name would break the rule so that it does not (the C#
/// frontend spells a parameter declared <c>@this</c> as <c>$this</c>).
/// </summary>
public static class IrParameterNames
{
    /// <summary>The receiver of an instance method.</summary>
    public const string Receiver = "this";

    /// <summary>Whether <paramref name="name"/> is a synthesised input's name.</summary>
    public static bool IsSynthesised(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return string.Equals(name, Receiver, StringComparison.Ordinal) || name.Contains('.', StringComparison.Ordinal);
    }
}
