using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace Equiv.Verify.Z3.Refinement;

/// <summary>
/// Which pure functions a refined query interprets (ADR 0053 decision 1; ticket P1-030). Rung 1 ended
/// Unknown(abstraction) on a candidate counterexample; when everything that candidate depends on is interpretable, the
/// next query gives exactly those functions their real meaning, on top of what earlier rounds interpreted.
/// </summary>
internal static class AbstractionRefinement
{
    /// <summary>How many refined queries a pair gets at most.</summary>
    public const int MaxRounds = 3;

    /// <summary>
    /// How many times the pair's resource limit a refined query gets. The default limit was set on bit-vector queries
    /// (ticket P2-050); bit-blasted, <c>a * 2.0 = a + a</c> on <c>double</c> needs 3.2 million against that default of 2
    /// million, and <c>a * 0.5 = a / 2.0</c> needs 6.8 million. The wall-clock timeout is the pair's own.
    /// </summary>
    public const int ResourceLimitFactor = 10;

    /// <summary>
    /// The options a refined query is asked under: <see cref="ResourceLimitFactor"/> times the resource limit, and no
    /// second solver, whose printer knows no floating point.
    /// </summary>
    public static VerificationOptions Options(VerificationOptions options) =>
        options with { Solver = null, ResourceLimit = (int)Math.Min((long)options.ResourceLimit * ResourceLimitFactor, int.MaxValue) };

    /// <summary>
    /// <paramref name="interpreted"/> and the functions <paramref name="abstractions"/> name, or null when the pair is not
    /// refined further: an abstraction is not an interpretable function of this pair, or all of them are interpreted
    /// already, which is a conversion whose argument has no meaning (<see cref="IrPureMeaning"/>).
    /// </summary>
    public static ImmutableSortedSet<string>? Next(IrProcedure old, IrProcedure @new, ImmutableSortedSet<string> interpreted, ImmutableArray<Abstraction> abstractions)
    {
        ImmutableSortedSet<string> next = interpreted.Union(abstractions.Select(static a => a.Identity.Value));
        return next.Count > interpreted.Count && Interpretable(old, @new).IsSupersetOf(next) ? next : null;
    }

    /// <summary>
    /// The functions of the pair that may be interpreted: every application of the name, on either side, is
    /// <see cref="IrPureMeaning.IsInterpretable"/>. One runtime-sensitive or checked application rules the name out, since
    /// both sides' applications of a name are one function.
    /// </summary>
    public static ImmutableSortedSet<string> Interpretable(IrProcedure old, IrProcedure @new) =>
        old.Blocks.Concat(@new.Blocks)
            .SelectMany(static b => b.Instructions.OfType<IrPure>())
            .GroupBy(static p => p.Function, StringComparer.Ordinal)
            .Where(static g => g.All(IrPureMeaning.IsInterpretable))
            .Select(static g => g.Key)
            .ToImmutableSortedSet(StringComparer.Ordinal);
}
