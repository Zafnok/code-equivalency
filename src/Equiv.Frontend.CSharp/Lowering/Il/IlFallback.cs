using System.Collections.Frozen;
using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Core.Progress;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// ADR 0039's per-pair rule, under <c>--il-fallback</c> (ticket P1-016). A pair that is not congruent, and where either side's
/// IOperation lowering holds an <see cref="IrOpaque"/> whose fingerprint the other side lacks, is lowered again from IL on
/// both sides (<see cref="IlLowerer"/>). The IL bodies replace the IOperation bodies only when they hold fewer unshared
/// opaques, and never one side alone. A method whose IL cannot be read keeps its IOperation lowering, and so does the pair:
/// the emit failed, the method was not found or has no body (<see cref="IlAstReader"/>'s reasons), or it is an
/// <c>async</c> or iterator method, whose IL is only the kickoff of a state machine the IL lowering does not follow. Each
/// such method is one debug detail line.
/// </summary>
internal static class IlFallback
{
    /// <summary><see cref="Choice.Lowering"/> of bodies from <see cref="IrLowerer"/>.</summary>
    public const string Operation = "operation";

    /// <summary><see cref="Choice.Lowering"/> of bodies from <see cref="IlLowerer"/>.</summary>
    public const string Il = "il";

    /// <summary>An <c>async</c> or iterator method: its IL is the kickoff of its state machine, not its body.</summary>
    public const string StateMachine = "il-state-machine";

    private static readonly FrozenSet<string> Unreadable =
        new[] { IlAstReader.EmitFailed, IlAstReader.MethodNotFound, IlAstReader.NoBody }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The bodies <paramref name="legacy"/> and <paramref name="modern"/> keep, read with the production <see cref="IlLowerer"/>.</summary>
    public static Choice Choose(Side legacy, Side modern, bool congruent, IRunLog log) =>
        Choose(legacy, modern, congruent, log, static (method, compilation, x87) => IlLowerer.Lower(method, compilation, x87));

    /// <summary>
    /// Seam for unit tests: <paramref name="lower"/> reads a method from IL, given whether its floating point is x87's. A
    /// <paramref name="congruent"/> pair is never lowered again (ADR 0024 decides first).
    /// </summary>
    internal static Choice Choose(Side legacy, Side modern, bool congruent, IRunLog log, Func<IMethodSymbol, Compilation, bool, IrProcedure> lower)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(modern);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(lower);
        Choice operation = new(legacy.Body, modern.Body, Tried: false, Operation);
        int unshared = Unshared(legacy.Body, modern.Body);
        if (congruent || unshared == 0)
        {
            return operation;
        }

        // Both sides are read, whatever the first gives, so every unreadable method has its line.
        IrProcedure? old = Relowered("legacy", legacy, PureCatalogue.IsX87(legacy.Compilation), log, lower);
        IrProcedure? @new = Relowered("modern", modern, x87: false, log, lower);
        return old is not null && @new is not null && Unshared(old, @new) < unshared
            ? new Choice(old, @new, Tried: true, Il)
            : operation with { Tried = true };
    }

    /// <summary>
    /// The opaques of <paramref name="old"/> and of <paramref name="new"/> the other body does not share: those with no
    /// fingerprint, and those whose fingerprint the other lacks (ADR 0024 decision 2), counted on both sides.
    /// </summary>
    internal static int Unshared(IrProcedure old, IrProcedure @new)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(@new);
        ImmutableArray<IrOpaque> oldOpaques = Opaques(old);
        ImmutableArray<IrOpaque> newOpaques = Opaques(@new);
        return Lacking(oldOpaques, Fingerprints(newOpaques)) + Lacking(newOpaques, Fingerprints(oldOpaques));
    }

    private static IrProcedure? Relowered(string side, Side procedure, bool x87, IRunLog log, Func<IMethodSymbol, Compilation, bool, IrProcedure> lower)
    {
        IrProcedure? body = procedure.Symbol.IsAsync || procedure.Symbol.IsIterator ? null : lower(procedure.Symbol, procedure.Compilation, x87);
        string? reason = body is null ? StateMachine : body.Blocks is [{ Instructions: [IrOpaque { WholeBody: true } opaque] }] && Unreadable.Contains(opaque.Reason) ? opaque.Reason : null;
        if (reason is null)
        {
            return body;
        }

        if (log.IsDebug)
        {
            log.Detail($"il-fallback: {side} {procedure.Body.Identity.Value} keeps its operation lowering: {reason}");
        }

        return null;
    }

    private static ImmutableArray<IrOpaque> Opaques(IrProcedure body) => [.. body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];

    private static HashSet<string> Fingerprints(ImmutableArray<IrOpaque> opaques) => [.. opaques.Select(static o => o.Fingerprint).OfType<string>()];

    private static int Lacking(ImmutableArray<IrOpaque> opaques, HashSet<string> other) =>
        opaques.Count(o => o.Fingerprint is not { } fingerprint || !other.Contains(fingerprint));

    /// <summary>One side of a matched pair: its method, the compilation it is read from, and its IOperation lowering.</summary>
    internal sealed record Side(IMethodSymbol Symbol, Compilation Compilation, IrProcedure Body);

    /// <summary>
    /// The bodies a pair keeps, both from one <see cref="Lowering"/>, and whether it was <see cref="Tried"/>: lowered again
    /// from IL to choose them.
    /// </summary>
    internal sealed record Choice(IrProcedure Old, IrProcedure New, bool Tried, string Lowering);
}
