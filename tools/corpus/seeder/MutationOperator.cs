namespace Equiv.Corpus.Seeder;

/// <summary>
/// How <see cref="SyntaxMutator"/> derives one method from another (ticket M0-012, moved here by ticket M4-010 so the
/// differential soundness gate and the corpus seeder share one implementation). The first six are the preserving
/// family (<see cref="SyntaxMutator.IsPreserving"/>): the two methods behave identically on every input. The rest are
/// the changing family, which usually changes behaviour but may not: neither gate assumes a mutant differs, only that
/// it might.
/// </summary>
public enum MutationOperator
{
    /// <summary>Renames one local variable throughout the method.</summary>
    RenameLocals,

    /// <summary>Swaps two adjacent assignments that neither throw nor read or write what the other writes.</summary>
    ReorderIndependentStatements,

    /// <summary><c>if (c) A else B</c> to <c>if (!c) B else A</c>.</summary>
    InvertIf,

    /// <summary><c>x + y</c> to <c>y + x</c>, for a commutative operator whose operands cannot throw.</summary>
    Commute,

    /// <summary>Evaluates an assigned or returned value into a new local first: <c>x = e;</c> to <c>var t = e; x = t;</c>.</summary>
    IntroduceTemporary,

    /// <summary><see cref="IntroduceTemporary"/> the other way round: the legacy method has the temporary, the modern one inlines it.</summary>
    InlineTemporary,

    /// <summary>Turns one comparison into its neighbour: <c>&lt;</c> and <c>&lt;=</c>, <c>&gt;</c> and <c>&gt;=</c>, <c>==</c> and <c>!=</c>.</summary>
    FlipComparison,

    /// <summary>Adds one to a literal, or subtracts one where adding would make a divisor zero.</summary>
    ChangeConstant,

    /// <summary>Replaces an <c>if</c> on <c>x == null</c> or <c>x != null</c> with the branch a non-null <c>x</c> takes.</summary>
    DropNullCheck,

    /// <summary>Removes one write of a field, property or other identifier the method does not itself declare.</summary>
    DropFieldWrite,

    /// <summary>Swaps the operands of a binary operator or comparison (not a shift).</summary>
    SwapArguments,

    /// <summary>Swaps an <c>if</c> that throws with an adjacent write of a field or an array element.</summary>
    MoveThrowAcrossSideEffect,
}
