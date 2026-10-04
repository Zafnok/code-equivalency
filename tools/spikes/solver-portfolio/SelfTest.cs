using System.Text;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace SolverPortfolioSpike;

/// <summary>
/// Exports one sample's <c>divergence</c> query each way it can answer, and checks that Z3 answers the exported file as it
/// answered the in-memory query, and that the satisfiable one's values, read back as another solver's would be, replay
/// to a Divergent.
/// </summary>
internal static class SelfTest
{
    private const string Header = "proc \"T::M(int,int)\" (%a \"a\": bv32, %b \"b\": bv32) -> bv32 entry B0\nB0:\n";

    private const string Sum = Header + """
          %r: bv32 = add %a, %b
          ret %r
        """;

    private const string SumCommuted = Header + """
          %r: bv32 = add %b, %a
          ret %r
        """;

    private const string Doubled = Header + """
          %r: bv32 = add %a, %a
          ret %r
        """;

    public static int Run()
    {
        bool ok = Check("a + b against b + a", Sum, SumCommuted, Status.UNSATISFIABLE);
        ok &= Check("a + b against a + a", Sum, Doubled, Status.SATISFIABLE);
        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(string name, string oldText, string newText, Status expected)
    {
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        (Rung1? pair, _) = Rung1.Prepare(IrText.Parse(oldText), IrText.Parse(newText), options);
        using Context context = new();
        ProductEncoding encoding = pair!.Encode(context, options);
        using Solver solver = Z3Backend.Query(context, encoding, options, Rung1.Terms(context, encoding, Rung1.Divergence));
        Status inMemory = Z3Backend.Check(context, solver, options, Rung1.Divergence);
        SmtFile file = SmtFile.Of(context, encoding, Rung1.Divergence);

        // The file as another solver gets it, less the commands the parser of Z3's API does not take.
        string script = string.Join('\n', file.Script().Split('\n').Where(static l => !l.StartsWith("(set-option", StringComparison.Ordinal) && !l.StartsWith("(check-sat", StringComparison.Ordinal) && !l.StartsWith("(get-value", StringComparison.Ordinal)));
        Status fromFile;
        using (Context fresh = new())
        {
            using Solver reparsed = Pipeline(fresh);
            Z3Backend.Limit(reparsed, options);
            reparsed.Add(fresh.ParseSMTLIB2String(script));
            fromFile = reparsed.Check();
        }

        bool ok = inMemory == expected && fromFile == inMemory && file.Z3Only.Count == 0 && file.Logic == "QF_BVDTSLIA";
        string readBack = "n/a";
        if (inMemory == Status.SATISFIABLE)
        {
            // What a solver prints for (get-value ...), built from Z3's own model.
            StringBuilder values = new("sat\n(");
            foreach (FuncDecl constant in solver.Model.ConstDecls.Where(d => file.Scalars.ContainsKey(d.Name.ToString())))
            {
                values.Append('(').Append(constant.Name).Append(' ').Append(solver.Model.ConstInterp(constant)).Append(")\n");
            }

            readBack = ReadBack.Run(context, encoding, options, pair, Rung1.Divergence, file, values.Append(')').ToString());
            ok &= readBack == ReadBack.Divergent;
        }

        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: in memory {inMemory}, from the exported file {fromFile}, logic {file.Logic}, read back {readBack}");
        return ok;
    }

    private static Solver Pipeline(Context context)
    {
        using Tactic solveEqs = context.MkTactic("solve-eqs");
        using Tactic simplify = context.MkTactic("simplify");
        using Tactic propagate = context.MkTactic("propagate-values");
        using Tactic smt = context.MkTactic("smt");
        using Tactic pipeline = context.AndThen(solveEqs, simplify, propagate, solveEqs, smt);
        return context.MkSolver(pipeline);
    }
}
