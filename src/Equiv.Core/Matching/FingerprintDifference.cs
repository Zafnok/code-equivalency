namespace Equiv.Core.Matching;

/// <summary>
/// What kept a matched pair out of congruence (ADR 0024 decision 1; ticket P2-173): the first component of the bound
/// fingerprint on which the two sides differ. <see cref="Component"/> is the component's kind, one of the constants here,
/// and <see cref="Identity"/> what it concerns: a callee, a type, a member, an operation's kind, the target of an
/// attribute, or the side. Neither ever holds source text or a constant's value, so a result that carries one says
/// nothing of the code beyond the names its other properties already use.
/// </summary>
public sealed record FingerprintDifference(string Component, string Identity)
{
    /// <summary>The method's kind, staticness, asyncness, return type or parameter types. <see cref="Identity"/> is <c>method</c>.</summary>
    public const string Signature = "signature";

    /// <summary>One side is an auto-accessor or a partial method's implementing part and the other is not. <see cref="Identity"/> names which.</summary>
    public const string BodyKind = "body-kind";

    /// <summary>An operation's kind, syntax kind, implicitness or depth: the bound trees have different shapes. <see cref="Identity"/> is the operation's kind.</summary>
    public const string Operation = "operation";

    /// <summary>The type of an operation that is otherwise the same. <see cref="Identity"/> is the type.</summary>
    public const string Type = "type";

    /// <summary>
    /// A constant, or the caller location the compiler supplies. <see cref="Identity"/> is the member the operation
    /// references when it has one (a <c>const</c> field), else the operation's kind.
    /// </summary>
    public const string Constant = "constant";

    /// <summary>The method a call or an object creation binds to. <see cref="Identity"/> is the callee.</summary>
    public const string Callee = "callee";

    /// <summary>Any other symbol an operation references: a field, a property, a local's type, an operator, a type operand. <see cref="Identity"/> is the symbol.</summary>
    public const string Symbol = "symbol";

    /// <summary>
    /// What an operation carries besides its type and symbols: its <c>checked</c> context, how an interpolated string is
    /// bound, the exception an unmatched <c>switch</c> expression throws. <see cref="Identity"/> is the operation's kind.
    /// </summary>
    public const string Context = "context";

    /// <summary>What an <c>extern</c> function imports. <see cref="Identity"/> is the function: <c>method</c>, or a local function's number.</summary>
    public const string Import = "import";

    /// <summary>
    /// An attribute the text holds: of the method, its return value or a parameter, of a local function, or the
    /// marshalling attributes of the assembly or the declaring type. <see cref="Identity"/> is what carries it.
    /// </summary>
    public const string Attribute = "attribute";

    /// <summary>The declaration of a type the body reads the layout of, or hands over. <see cref="Identity"/> is the type.</summary>
    public const string TypeLayout = "type-layout";

    /// <summary>A side has no fingerprint: its declaration holds no code to compare. <see cref="Identity"/> is the side or sides.</summary>
    public const string NoFingerprint = "no-fingerprint";

    /// <summary>
    /// The two texts are equal and a runtime rule applies between the pair's runtimes. <see cref="Identity"/> is every rule that
    /// applies, the legacy side's first, each once, joined by <c>, </c>: a callee, field or event a <c>runtime-changes.json</c> row
    /// flags, <c>x87</c>, <c>float-to-integer</c> or <c>extern</c> (ticket P2-181).
    /// </summary>
    public const string RuntimeRule = "runtime-rule";

    /// <summary>The two texts are equal and a body does not bind (ADR 0029 decision 2). <see cref="Identity"/> is the side or sides.</summary>
    public const string Unbound = "unbound";

    /// <summary>The legacy side, as <see cref="Identity"/> names it.</summary>
    public const string Legacy = "legacy";

    /// <summary>The modern side, as <see cref="Identity"/> names it.</summary>
    public const string Modern = "modern";

    /// <summary><see cref="Identity"/> for the sides among <paramref name="legacy"/> and <paramref name="modern"/> that are named.</summary>
    public static string Sides(bool legacy, bool modern) => string.Join(", ", ((string?[])[legacy ? Legacy : null, modern ? Modern : null]).OfType<string>());
}
