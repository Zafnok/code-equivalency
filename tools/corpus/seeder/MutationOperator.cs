namespace Equiv.Corpus.Seeder;

/// <summary>
/// How <see cref="SyntaxMutator"/> derives one method from another (ticket M0-012, moved here by ticket M4-010 so the
/// differential soundness gate and the corpus seeder share one implementation). The first eleven are the preserving
/// family (<see cref="SyntaxMutator.IsPreserving"/>): the two methods behave identically on every input. The rest are
/// the changing family, which usually changes behaviour but may not: neither gate assumes a mutant differs, only that
/// it might.
/// </summary>
public enum MutationOperator
{
    /// <summary>Renames one local variable throughout the method, never one written inside an argument (its name can be a value there).</summary>
    RenameLocals,

    /// <summary>Swaps two adjacent assignments that neither throw nor read or write what the other writes.</summary>
    ReorderIndependentStatements,

    /// <summary><c>if (c) A else B</c> to <c>if (!c) B else A</c>.</summary>
    InvertIf,

    /// <summary><c>x + y</c> to <c>y + x</c>, for a built-in operator that commutes (never <c>string</c> or delegate <c>+</c>) whose operands neither throw nor call (ticket P2-061).</summary>
    Commute,

    /// <summary>Evaluates an assigned or returned value into a new local first: <c>x = e;</c> to <c>var t = e; x = t;</c>, for a bare-name target <c>x</c> (ticket P2-061).</summary>
    IntroduceTemporary,

    /// <summary><see cref="IntroduceTemporary"/> the other way round: the legacy method has the temporary, the modern one inlines it.</summary>
    InlineTemporary,

    /// <summary><c>if (c) x = a; else x = b;</c> to <c>x = c ? a : b;</c>, and the same for two <c>return</c>s, when <c>a</c> and <c>b</c> have exactly the target's type (ticket P2-048).</summary>
    IfToConditional,

    /// <summary><c>x != null ? x : y</c> and <c>x == null ? y : x</c> to <c>x ?? y</c>, for a reference-typed local or parameter <c>x</c> and a <c>y</c> of its type (ticket P2-048).</summary>
    CoalesceNullCheck,

    /// <summary>A <c>+</c> chain of <c>string</c> operands to one interpolated string (ticket P2-048).</summary>
    ConcatToInterpolation,

    /// <summary>A <c>void</c> method's last statement <c>if (c) { S }</c> to <c>if (!c) return;</c> followed by <c>S</c> (ticket P2-048).</summary>
    GuardClause,

    /// <summary><c>for (int i = 0; i &lt; a.Length; i++)</c> over an array whose body reads <c>i</c> only as <c>a[i]</c> to <c>foreach (var item in a)</c> (ticket P2-048).</summary>
    ForToForeach,

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
