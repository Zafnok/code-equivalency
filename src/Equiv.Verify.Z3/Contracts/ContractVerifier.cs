using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// Decides whether a matched callee pair satisfies a candidate contract K (ADR 0036 decisions 1 and 2; ticket P1-010).
/// It is the loop ladder with the pair's product asking whether K can fail in place of whether some observable differs:
/// rung 1 on the pair unrolled <see cref="VerificationOptions.Bound"/> times, which admits K for an acyclic pair and
/// rejects it with the model of any input that breaks it within the bound, then, for a pair with loops, rungs 2 and 3. Rungs 4 and 5 build their own query and are not asked. The proof is modular: the pair's own calls to
/// matched callees are shared functions, as in any pair's proof.
/// </summary>
internal sealed class ContractVerifier(Func<Context> createContext, VerificationOptions options)
{
    public ContractCheck Verify(IrProcedure old, IrProcedure @new, CalleeContract contract)
    {
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(@new);
        if (new[] { oldShape, newShape }.Any(static s => s.IsSelfRecursive || !s.IsReducible))
        {
            return new ContractCheck.Unknown("a side calls itself or has irreducible control flow");
        }

        bool looping = !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        using Context context = createContext();
        ProductEncoding encoding = ProductEncoder.Encode(
            context, IrUnroller.Unroll(old, options.Bound), IrUnroller.Unroll(@new, options.Bound), options.CallIdentityMap, relation: contract);
        BoolExpr[] reachable = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        using Solver broken = Z3Backend.Query(context, encoding, options, [encoding.Differs, .. reachable]);
        return broken.Check() switch
        {
            Status.SATISFIABLE => new ContractCheck.Rejected(new ContractModel(
                [.. encoding.Conjuncts.Select((c, i) => (c, i)).Where(t => broken.Model.Eval(t.c, completion: true).IsFalse).Select(static t => t.i)],
                broken.Model.ToString())),
            Status.UNKNOWN => new ContractCheck.Unknown(Z3Backend.Timeout(broken, options)),
            _ when looping => Induction(old, @new, oldShape, newShape, contract),
            _ => new ContractCheck.Admitted(ProofMethod.Bounded),
        };
    }

    /// <summary>Rungs 2 and 3 with the contract as the pair's exit condition.</summary>
    private ContractCheck Induction(IrProcedure old, IrProcedure @new, IrLoopAnalysis oldShape, IrLoopAnalysis newShape, CalleeContract contract)
    {
        LoopLadder ladder = new(createContext, options) { Relation = contract, Traces = null };
        LockstepInduction lockstep = new(ladder, old, @new, oldShape, newShape);
        LoopLadder.Rung rung = lockstep.Prove();
        if (rung.Verdict is Equivalent)
        {
            return new ContractCheck.Admitted(ProofMethod.LockstepInduction);
        }

        rung = new KInduction(ladder, lockstep).Prove();
        return rung.Verdict is Equivalent
            ? new ContractCheck.Admitted(ProofMethod.KInduction)
            : new ContractCheck.Unknown(string.Create(CultureInfo.InvariantCulture, $"no rung decided the contract past the bound {options.Bound}: {rung.Step.Detail}"));
    }
}
