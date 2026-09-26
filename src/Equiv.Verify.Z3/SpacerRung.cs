using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ChcAnswer = Equiv.Verify.Z3.ChcEncoder.ChcAnswer;
using Rung = Equiv.Verify.Z3.LoopLadder.Rung;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace Equiv.Verify.Z3;

/// <summary>
/// Rung 4 of the loop ladder (VERIFICATION-MODEL.md section 5.1; ADR 0008; ticket P1-001): the pair's constrained Horn
/// clauses (<see cref="ChcEncoder"/>) go to Z3 Spacer, which synthesises the coupling invariant itself, so the loops need
/// not align. It applies to a pair neither side of which calls or applies a pure function, since a call trace is not a
/// relation Spacer can infer. An unsatisfiable divergence query is unbounded <see cref="ProofMethod.Chc"/> Equivalent with
/// Spacer's invariant. A derivation is replayed from its inputs through both original procedures: a divergence is
/// Divergent, an opaque node reached is Unknown(Opaque), and anything else is <see cref="UnknownReason.ChcSpurious"/> with
/// both runs in the detail. A query that gives up is <see cref="UnknownReason.ChcTimeout"/>.
/// <para>
/// Unless <see cref="VerificationOptions.ChcIntMode"/> is off, rung 4 first asks over the integers
/// (<see cref="IntModeTranslator"/>), where Spacer finds linear invariants far more readily. An invariant found there
/// that also solves the clauses read with wrap-around arithmetic (<see cref="ChcEncoder.Solves"/>) is a proof over the
/// bitvectors. Otherwise the integer answer stands only when a second Spacer query proves that no exactly modelled
/// operation of either side can overflow, since only then is every bitvector run an integer run; a replayed divergence, an
/// opaque node or a query that gave up stands without it. Failing both, rung 4 asks over the bitvectors. Every step names
/// the arithmetic its answer holds in (<see cref="LadderStep.Mode"/>).
/// </para>
/// </summary>
internal sealed class SpacerRung(Func<Context> createContext, VerificationOptions options)
{
    /// <summary>Steps each side's replay of a derivation may take; a replay that runs out does not diverge.</summary>
    public const int ReplayBudget = 1_000_000;

    private uint Timeout => (uint)options.TimeoutMs;

    public Rung Prove(IrProcedure old, IrProcedure @new)
    {
        if ((Obstacle("old", old) ?? Obstacle("new", @new)) is { } obstacle)
        {
            return LoopLadder.NotApplicable(ProofMethod.Chc, $"rung 4 does not model calls: {obstacle}", cause: null);
        }

        using Context context = createContext();
        string bitVectors = "integer mode is off";
        if (options.ChcIntMode)
        {
            ChcEncoder integers = new(context, old, @new, ChcArithmetic.Integers, options.CallIdentityMap);
            ChcAnswer found = integers.Query(overflows: false, Timeout);
            if (found.Status == Status.UNSATISFIABLE && new ChcEncoder(context, old, @new, ChcArithmetic.WrappingIntegers, options.CallIdentityMap).Solves(found.Answer, Timeout))
            {
                return InMode(Conclude(integers, found, old, @new, "over the integers that holds with wrap-around arithmetic too"), ChcMode.BitVectors);
            }

            Rung rung = Conclude(integers, found, old, @new, "over the integers");
            if (rung.Verdict is not (Equivalent or Unknown { Reason: UnknownReason.ChcSpurious }))
            {
                return InMode(rung, ChcMode.Integers);
            }

            ChcAnswer overflow = integers.Query(overflows: true, Timeout);
            if (overflow.Status == Status.UNSATISFIABLE)
            {
                return InMode(rung, ChcMode.Integers);
            }

            bitVectors = overflow.Status == Status.SATISFIABLE
                ? "an integer operation can overflow"
                : $"the overflow query gave up ({overflow.Reason})";
        }

        ChcEncoder bits = new(context, old, @new, ChcArithmetic.BitVectors, options.CallIdentityMap);
        return InMode(Conclude(bits, bits.Query(overflows: false, Timeout), old, @new, $"over the bitvectors, since {bitVectors}"), ChcMode.BitVectors);
    }

    /// <summary>Why rung 4 does not apply to <paramref name="procedure"/>: the first call or pure function a reachable block holds.</summary>
    private static string? Obstacle(string side, IrProcedure procedure) =>
        IrLoopAnalysis.Of(procedure).ReversePostorder.SelectMany(static b => b.Instructions).FirstOrDefault(static i => i is IrCall or IrPure) switch
        {
            IrCall call => $"the {side} side calls {call.Callee.Value}",
            IrPure pure => $"the {side} side applies {pure.Function}",
            _ => null,
        };

    /// <summary>The unknown outcome of each run that reached an opaque node, as the causes an Unknown(Opaque) points at.</summary>
    private static IEnumerable<UnknownCause> Opaque(Codebase side, IrRun run) =>
        run.Outcome is IrOpaqueReached reached ? [new UnknownCause(side, reached.Reason, reached.Span)] : [];

    private static Rung InMode(Rung rung, ChcMode mode) => rung with { Step = rung.Step with { Mode = mode } };

    /// <summary>What <paramref name="chc"/>'s divergence query answering <paramref name="answer"/> means.</summary>
    private Rung Conclude(ChcEncoder chc, ChcAnswer answer, IrProcedure old, IrProcedure @new, string how) => answer.Status switch
    {
        Status.UNSATISFIABLE => LoopLadder.Proved(ProofMethod.Chc, $"Spacer found a coupling invariant {how}", new Equivalent(ProofMethod.Chc) { Invariant = chc.Invariant(answer.Answer) }),
        Status.SATISFIABLE => Replay(chc, old, @new, chc.DerivationInputs(answer.Answer, Timeout), how),
        _ => TimedOut($"Spacer gave up {how}: {answer.Reason} with a {options.TimeoutMs.ToString(CultureInfo.InvariantCulture)} ms timeout"),
    };

    /// <summary>A derivation's inputs replayed through both original procedures.</summary>
    private static Rung Replay(ChcEncoder chc, IrProcedure old, IrProcedure @new, IrInputs inputs, string how)
    {
        ImmutableArray<SharedParameter> shared = [.. chc.Inputs.Select(static i => i.Shared)];
        IrRun oldRun = IrInterpreter.Run(old, ModelDecoder.Bind(old, shared, inputs, static s => s.Old), NoCalls.Instance, ReplayBudget);
        IrRun newRun = IrInterpreter.Run(@new, ModelDecoder.Bind(@new, shared, inputs, static s => s.New), NoCalls.Instance, ReplayBudget);
        Counterexample replay = new(inputs, oldRun, newRun);
        ImmutableArray<UnknownCause> opaque = [.. Opaque(Codebase.Legacy, oldRun), .. Opaque(Codebase.Modern, newRun)];
        if (!opaque.IsEmpty)
        {
            string reasons = Z3Backend.OpaqueReasons(opaque);
            return new Rung(
                new LadderStep(ProofMethod.Chc, RungOutcome.Inconclusive, $"a derivation {how} reaches an opaque node: {reasons}"),
                new Unknown(UnknownReason.Opaque, reasons) { Causes = opaque });
        }

        bool complete = new[] { oldRun, newRun }.All(static r => r.Outcome is IrReturned or IrThrew);
        if (complete && ModelDecoder.Diverges(old, @new, shared, inputs, oldRun, newRun, chc.Calls))
        {
            return LoopLadder.Refuted(ProofMethod.Chc, $"a derivation {how} replays to a divergence", replay);
        }

        string detail = $"a derivation {how} does not replay to a divergence: {CounterexampleText.Dump(replay)}";
        return new Rung(new LadderStep(ProofMethod.Chc, RungOutcome.Inconclusive, detail), new Unknown(UnknownReason.ChcSpurious, detail));
    }

    private static Rung TimedOut(string detail) =>
        new(new LadderStep(ProofMethod.Chc, RungOutcome.Timeout, detail), new Unknown(UnknownReason.ChcTimeout, detail));

    /// <summary>The call oracle of a replay rung 4 makes, of procedures that make no call.</summary>
    internal sealed class NoCalls : ICallOracle
    {
        public static NoCalls Instance { get; } = new();

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap) =>
            throw new InvalidOperationException($"Rung 4 replays only procedures that make no call, yet one calls {callee?.Value}.");
    }
}
