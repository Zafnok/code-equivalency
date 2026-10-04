using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using SolverPortfolioSpike;

namespace TraceEncodingSpike;

/// <summary>
/// Hand-written pairs whose only observable is the call trace (every one returns its first parameter and uses no call's
/// result). On each, Z3 answers the <c>divergence</c> query the same in the sequence encoding and the positional one, and
/// as expected; the positional file holds bit-vectors, arrays and functions only; and Z3 answers that file, read back
/// from its text, the same again.
/// </summary>
internal static partial class SelfTest
{
    private const string Header = "proc \"T::M(int,int)\" (%a \"a\": bv32, %b \"b\": bv32) -> bv32 entry B0\nB0:\n";

    private const string FThenG = Header + """
          call "S::F(int)"(%a)
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FOnce = Header + """
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string FOfSum = Header + """
          %s: bv32 = add %a, %b
          call "S::F(int)"(%s)
          ret %a
        """;

    private const string FOfSumCommuted = Header + """
          %s: bv32 = add %b, %a
          call "S::F(int)"(%s)
          ret %a
        """;

    private const string FThenH = Header + """
          call "S::F(int)"(%a)
          call "S::H(int)"(%b)
          ret %a
        """;

    private const string FOfBThenG = Header + """
          call "S::F(int)"(%b)
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FTwice = Header + """
          call "S::F(int)"(%a)
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string NoCall = Header + """
          ret %a
        """;

    private const string FWhenLess = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B2
        B2:
          ret %a
        """;

    private const string GThenF = Header + """
          call "S::G(int)"(%b)
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string FOnEitherArmThenG = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B3
        B2:
          call "S::F(int)"(%a)
          goto B3
        B3:
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FWhenLessThenG = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B2
        B2:
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FWhenGreaterSwappedThenG = Header + """
          %c: bool = sgt %b, %a
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B2
        B2:
          call "S::G(int)"(%b)
          ret %a
        """;

    public static int Run()
    {
        bool ok = Check("equal traces", FOfSum, FOfSumCommuted, Status.UNSATISFIABLE);
        ok &= Check("a different callee", FThenG, FThenH, Status.SATISFIABLE);
        ok &= Check("a different argument", FThenG, FOfBThenG, Status.SATISFIABLE);
        ok &= Check("a different length", FTwice, FOnce, Status.SATISFIABLE);
        ok &= Check("a call under a branch on one side only", FWhenLess, NoCall, Status.SATISFIABLE);
        ok &= Check("a reordered pair of calls", FThenG, GThenF, Status.SATISFIABLE);
        ok &= Check("equal traces, a call on both arms against one before the branch", FOnEitherArmThenG, FThenG, Status.UNSATISFIABLE);
        ok &= Check("equal traces, a call at a position that depends on the path", FWhenLessThenG, FWhenGreaterSwappedThenG, Status.UNSATISFIABLE);
        ok &= Check("a call under a branch against the same call always made", FWhenLessThenG, FThenG, Status.SATISFIABLE);
        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(string name, string oldText, string newText, Status expected)
    {
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        (Rung1? pair, _) = Rung1.Prepare(IrText.Parse(oldText), IrText.Parse(newText), options);
        using Context context = new();
        Product product = Positional.Build(context, pair!.Old, pair.New, options.CallIdentityMap);
        using Solver sequenceSolver = Z3Backend.Query(context, product.Sequence, options, Rung1.Terms(context, product.Sequence, Rung1.Divergence));
        Status sequence = Z3Backend.Check(context, sequenceSolver, options, Rung1.Divergence);
        using Solver positionalSolver = Z3Backend.Query(context, product.Positional, options, Rung1.Terms(context, product.Positional, Rung1.Divergence));
        Status positional = Z3Backend.Check(context, positionalSolver, options, Rung1.Divergence);

        // The file as another solver gets it, less the commands the parser of Z3's API does not take.
        SmtFile file = SmtFile.Of(context, product.Positional, Rung1.Divergence);
        string script = string.Join('\n', Smt.Unwrap(file.Script()).Split('\n').Where(static l => !l.StartsWith("(set-option", StringComparison.Ordinal) && !l.StartsWith("(check-sat", StringComparison.Ordinal) && !l.StartsWith("(get-value", StringComparison.Ordinal)));
        Status fromFile;
        using (Context fresh = new())
        {
            using Solver reparsed = fresh.MkSolver();
            Z3Backend.Limit(reparsed, options);
            reparsed.Add(fresh.ParseSMTLIB2String(script));
            fromFile = reparsed.Check();
        }

        bool ok = sequence == expected && positional == expected && fromFile == expected && Allowed().IsMatch(file.Logic) && !Unary().IsMatch(script);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: sequence {sequence}, positional {positional}, the positional file {fromFile}, its logic {file.Logic}, pairs of sites {product.Pairs}");
        return ok;
    }

    // Bit-vectors, arrays and uninterpreted functions, and none of datatypes, sequences or integers.
    [GeneratedRegex(@"^QF_A?(UF)?(BV)?$")]
    private static partial Regex Allowed();

    [GeneratedRegex(@"\((or|and)\s+(\|[^|]*\||[^\s()]+)\)")]
    private static partial Regex Unary();
}
