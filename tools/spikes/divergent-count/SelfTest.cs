using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;

namespace DivergentCount;

/// <summary>
/// Criterion 3: ten hand-written pairs whose divergent share is known exactly. Each must classify as countable with the
/// expected number of bits, and its estimate must be within the tolerance of the exact count.
/// </summary>
internal static class SelfTest
{
    private const string Int = "proc \"T::M(int)\" (%a: bv32) -> bv32 entry B0\nB0:\n";

    private const string Identity = Int + "  ret %a\n";

    private static readonly (string Name, string Old, string New, int Bits, double Log2Count)[] Pairs =
    [
        ("every int: a against a + 1", Identity, Int + """
              %one: bv32 = const bv32 1
              %r: bv32 = add %a, %one
              ret %r
            """, 32, 32),
        ("one int: a against 0 when a == 12345", Identity, Int + """
              %k: bv32 = const bv32 12345
              %c: bool = eq %a, %k
              br %c, B1, B2
            B1:
              %z: bv32 = const bv32 0
              ret %z
            B2:
              ret %a
            """, 32, 0),
        ("both bools: b against !b",
            "proc \"T::M(bool)\" (%b: bool) -> bool entry B0\nB0:\n  ret %b\n",
            "proc \"T::M(bool)\" (%b: bool) -> bool entry B0\nB0:\n  %n: bool = boolnot %b\n  ret %n\n", 1, 1),
        ("half the ints: a against 0 when a < 0", Identity, Int + """
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              br %c, B1, B2
            B1:
              ret %z
            B2:
              ret %a
            """, 32, 31),
        ("a quarter of bool and int: a against 0 when b and a < 0",
            "proc \"T::M(bool,int)\" (%b: bool, %a: bv32) -> bv32 entry B0\nB0:\n  ret %a\n",
            "proc \"T::M(bool,int)\" (%b: bool, %a: bv32) -> bv32 entry B0\nB0:\n" + """
              %z: bv32 = const bv32 0
              %lt: bool = slt %a, %z
              %c: bool = and %b, %lt
              br %c, B1, B2
            B1:
              ret %z
            B2:
              ret %a
            """, 33, 31),
        ("100 of 256 bytes: a against a + 1 when a < 100",
            "proc \"T::M(byte)\" (%a: bv8) -> bv8 entry B0\nB0:\n  ret %a\n",
            "proc \"T::M(byte)\" (%a: bv8) -> bv8 entry B0\nB0:\n" + """
              %k: bv8 = const bv8 100
              %c: bool = ult %a, %k
              br %c, B1, B2
            B1:
              %one: bv8 = const bv8 1
              %r: bv8 = add %a, %one
              ret %r
            B2:
              ret %a
            """, 8, Math.Log2(100)),
        ("a low byte of zero: a against a + 1 when (a & 255) == 0", Identity, Int + """
              %mask: bv32 = const bv32 255
              %low: bv32 = and %a, %mask
              %z: bv32 = const bv32 0
              %c: bool = eq %low, %z
              br %c, B1, B2
            B1:
              %one: bv32 = const bv32 1
              %r: bv32 = add %a, %one
              ret %r
            B2:
              ret %a
            """, 32, 24),
        ("a non-null reference: its null shadow against true",
            "proc \"T::M(S)\" (%s: sort \"S\", %null.S: map<sort \"S\", bool>) -> bool entry B0\nB0:\n  %n: bool = mapread %null.S, %s\n  ret %n\n",
            "proc \"T::M(S)\" (%s: sort \"S\", %null.S: map<sort \"S\", bool>) -> bool entry B0\nB0:\n  %n: bool = mapread %null.S, %s\n  %t: bool = const bool true\n  ret %t\n", 1, 0),
        ("1000 of 2^17: a against a + 1 when b and a < 1000",
            "proc \"T::M(bool,ushort)\" (%b: bool, %a: bv16) -> bv16 entry B0\nB0:\n  ret %a\n",
            "proc \"T::M(bool,ushort)\" (%b: bool, %a: bv16) -> bv16 entry B0\nB0:\n" + """
              %k: bv16 = const bv16 1000
              %lt: bool = ult %a, %k
              %c: bool = and %b, %lt
              br %c, B1, B2
            B1:
              %one: bv16 = const bv16 1
              %r: bv16 = add %a, %one
              ret %r
            B2:
              ret %a
            """, 17, Math.Log2(1000)),

        // The call's result is not a parameter: a positive a diverges for some result (7), so every positive a counts.
        ("the positive ints, for some call result: F(a) against 0 when a > 0 and F(a) == 7",
            Int + "  %r: bv32 = call \"T::F(int)\"(%a)\n  ret %r\n",
            Int + """
              %r: bv32 = call "T::F(int)"(%a)
              %z: bv32 = const bv32 0
              %p: bool = sgt %a, %z
              %k: bv32 = const bv32 7
              %q: bool = eq %r, %k
              %c: bool = and %p, %q
              br %c, B1, B2
            B1:
              ret %z
            B2:
              ret %r
            """, 32, Math.Log2(int.MaxValue)),
    ];

    /// <summary>
    /// Not one of the ten: an equality between two parameters. Under parity rows it is a dense linear system over GF(2), which
    /// Z3 has no reasoning for, so the count gives up. Printed as the measured limit, and it does not fail the self-test.
    /// </summary>
    private static readonly (string Name, string Old, string New, int Bits, double Log2Count) Diagonal = ("the diagonal: a against a + 1 when a == b",
            "proc \"T::M(int,int)\" (%a: bv32, %b: bv32) -> bv32 entry B0\nB0:\n  ret %a\n",
            "proc \"T::M(int,int)\" (%a: bv32, %b: bv32) -> bv32 entry B0\nB0:\n" + """
              %c: bool = eq %a, %b
              br %c, B1, B2
            B1:
              %one: bv32 = const bv32 1
              %r: bv32 = add %a, %one
              ret %r
            B2:
              ret %a
            """, 64, 32);

    public static int Run()
    {
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        Console.WriteLine($"threshold {Counter.Threshold}, iterations {Counter.Iterations}, tolerance {Counter.Tolerance}, confidence {Counter.Confidence}");
        bool ok = true;
        for (int i = 0; i <= Pairs.Length; i++)
        {
            bool limit = i == Pairs.Length;
            (string name, string oldText, string newText, int bits, double log2Count) = limit ? Diagonal : Pairs[i];
            IrProcedure old = IrText.Parse(oldText);
            IrProcedure @new = IrText.Parse(newText);
            Classification classification = Inputs.Classify(old, @new);
            Count count = classification.Countable ? new Counter(options, seed: i).Run(old, @new, classification) : new Count("not countable: " + classification.Blocker, 0, double.NaN, false, 0, 0);
            double expected = log2Count - bits;
            bool within = count.Outcome == Counter.Counted
                && classification.Bits == bits
                && Math.Abs(count.Log2Share - expected) <= Math.Log2(1 + Counter.Tolerance) + 1e-9
                && (!count.Exact || Math.Abs(count.Log2Share - expected) < 1e-9);
            ok &= within || limit;
            Console.WriteLine(FormattableString.Invariant(
                $"{(limit ? "limit" : within ? "ok  " : "FAIL")} {name}: {count.Outcome}, {classification.Bits} bits, log2 share {count.Log2Share:F2} (exact {expected:F2}){(count.Exact ? ", enumerated" : string.Empty)}, {count.Checks} checks, {count.Milliseconds} ms"));
        }

        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }
}
