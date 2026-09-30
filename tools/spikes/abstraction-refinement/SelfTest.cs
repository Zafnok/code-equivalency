using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3;

namespace AbstractionSpike;

/// <summary>
/// The ticket's two hand-written pairs. Each is Unknown(abstraction) with <c>IntPtr</c>'s operators shared, and each must
/// reach its expected outcome once they are interpreted.
/// </summary>
internal static class SelfTest
{
    private const string Header = "proc \"T::M(System.IntPtr,System.IntPtr)\" (%a: sort \"System.IntPtr\", %b: sort \"System.IntPtr\") -> bool entry B0\nB0:\n";

    // a == b, with the exception flag every user-defined operator carries (PureCatalogue.AnyException).
    private const string EqualityText = Header + """
          %r: bool = pure "op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)"(%a, %b) throws(%t: bool "System.Exception")
          br %t, B1, B2
        B1:
          throw "System.Exception"
        B2:
          ret %r
        """;

    // !(a != b): equal to a == b only if the solver knows what != means.
    private const string NegatedInequalityText = Header + """
          %n: bool = pure "op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)"(%a, %b) throws(%t: bool "System.Exception")
          br %t, B1, B2
        B1:
          throw "System.Exception"
        B2:
          %r: bool = boolnot %n
          ret %r
        """;

    // a != b: differs from a == b on every input.
    private const string InequalityText = Header + """
          %r: bool = pure "op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)"(%a, %b) throws(%t: bool "System.Exception")
          br %t, B1, B2
        B1:
          throw "System.Exception"
        B2:
          ret %r
        """;

    public static int Run()
    {
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        Z3Backend backend = new();
        bool ok = Check(backend, options, "a == b against !(a != b)", EqualityText, NegatedInequalityText, "Equivalent");
        ok &= Check(backend, options, "a == b against a != b", EqualityText, InequalityText, "Divergent");
        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(Z3Backend backend, VerificationOptions options, string name, string oldText, string newText, string expected)
    {
        IrProcedure old = IrText.Parse(oldText);
        IrProcedure @new = IrText.Parse(newText);
        (Verdict baseline, Verdict refined) = Refiner.Query(backend, old, @new, options);
        bool ok = baseline is Unknown { Reason: UnknownReason.Abstraction }
            && Refiner.Outcome(refined).StartsWith(expected, StringComparison.Ordinal)
            && Refiner.Count(Refiner.Interpret(old)) == 0;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: shared {Refiner.Outcome(baseline)}, interpreted {Refiner.Outcome(refined)} (expected {expected})");
        return ok;
    }
}
