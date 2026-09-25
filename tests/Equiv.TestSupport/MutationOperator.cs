namespace Equiv.TestSupport;

/// <summary>
/// How <see cref="PairGen"/> derives the modern method of a pair from the legacy one (ticket M0-012). The first six
/// are the preserving family (<see cref="PairGen.IsPreserving"/>): the two methods behave identically on
/// every input. The rest are the changing family, which usually changes behaviour but may not: the differential
/// soundness gate asserts only against what execution observes. M4-010 reuses them on the corpus.
/// </summary>
public enum MutationOperator
{
    /// <summary>Renames the locals <c>x</c>, <c>y</c> and <c>z</c> throughout.</summary>
    RenameLocals,

    /// <summary>Swaps two adjacent assignments that neither throw nor read or write what the other writes.</summary>
    ReorderIndependentStatements,

    /// <summary><c>if (c) A else B</c> to <c>if (!c) B else A</c>.</summary>
    InvertIf,

    /// <summary><c>x + y</c> to <c>y + x</c>, for a commutative operator whose operands cannot throw.</summary>
    Commute,

    /// <summary>Evaluates an assigned or returned value into a new local first: <c>x = e</c> to <c>int t = e; x = t</c>.</summary>
    IntroduceTemporary,

    /// <summary><see cref="IntroduceTemporary"/> the other way round: the legacy method has the temporary, the modern one inlines it.</summary>
    InlineTemporary,

    /// <summary>Turns one comparison into its neighbour: <c>&lt;</c> and <c>&lt;=</c>, <c>&gt;</c> and <c>&gt;=</c>, <c>==</c> and <c>!=</c>.</summary>
    FlipComparison,

    /// <summary>Adds one to a literal, or subtracts one where adding would make a divisor zero.</summary>
    ChangeConstant,

    /// <summary>Replaces an <c>if</c> on <c>s == null</c> or <c>s != null</c> with the branch a non-null <c>s</c> takes.</summary>
    DropNullCheck,

    /// <summary>Removes one write of the static field <c>F</c>.</summary>
    DropFieldWrite,

    /// <summary>Swaps the operands of a binary operator or comparison whose operands have the same type.</summary>
    SwapArguments,

    /// <summary>Swaps an <c>if</c> that throws with an adjacent write of <c>F</c> or of an element of <c>u</c>.</summary>
    MoveThrowAcrossSideEffect,
}
