using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;

namespace FailureRefinementSpike;

/// <summary>
/// The ticket's two hand-written pairs. Neither is Equivalent. On the first no input makes either side throw, so both
/// weaker queries are unsatisfiable; on the second the modern side alone throws on one input, a model for
/// <c>newFailures</c>.
/// </summary>
internal static class SelfTest
{
    private const string Header = "proc \"T::M(int)\" (%a: bv32) -> bv32 entry B0\nB0:\n";

    private const string PlusOneText = Header + """
          %one: bv32 = const bv32 1
          %r: bv32 = add %a, %one
          ret %r
        """;

    private const string PlusTwoText = Header + """
          %two: bv32 = const bv32 2
          %r: bv32 = add %a, %two
          ret %r
        """;

    private const string IdentityText = Header + """
          ret %a
        """;

    private const string ThrowsOnZeroText = Header + """
          %zero: bv32 = const bv32 0
          %isZero: bool = eq %a, %zero
          br %isZero, B1, B2
        B1:
          throw "System.ArgumentException"
        B2:
          ret %a
        """;

    public static int Run()
    {
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames) { ResourceLimit = config.ResourceLimit };
        bool ok = Check(options, "a + 1 against a + 2", PlusOneText, PlusTwoText, "none-proved", "none-proved");
        ok &= Check(options, "a against a, throwing when a is 0", IdentityText, ThrowsOnZeroText, "found", "none-proved");
        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(VerificationOptions options, string name, string oldText, string newText, string newFailures, string removedFailures)
    {
        PairMeasure measure = Measured.Run(IrText.Parse(oldText), IrText.Parse(newText), options, encodable: true);
        bool ok = measure.NewFailures.Outcome == newFailures && measure.RemovedFailures.Outcome == removedFailures;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: newFailures {measure.NewFailures.Outcome} (expected {newFailures}), removedFailures {measure.RemovedFailures.Outcome} (expected {removedFailures})");
        return ok;
    }
}
