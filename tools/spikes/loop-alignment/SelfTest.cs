using Equiv.Core.Ir;

namespace LoopAlignmentSpike;

/// <summary>
/// The ticket's two hand-written pairs: a counting loop that calls once per iteration against its 2:1 unrolling, and
/// against the same loop with its first iteration peeled. Each must get exactly its schedule, and the loop against itself
/// must be lockstep.
/// </summary>
internal static class SelfTest
{
    private const string Header = "proc \"T::M(int)\" (%n \"n\": bv32) -> bv32 entry B0\n";

    private const string Loop = Header + """
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i "i": bv32 = phi [B0: %z, B2: %i1]
          %c: bool = slt %i, %n
          br %c, B2, B3
        B2:
          %u: bv32 = call "T::F(int)"(%i)
          %i1 "i": bv32 = add %i, %one
          goto B1
        B3:
          ret %i
        """;

    // Two iterations per trip, with the exit test kept between them.
    private const string Unrolled = Header + """
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i "i": bv32 = phi [B0: %z, B4: %i2]
          %c: bool = slt %i, %n
          br %c, B2, B5
        B2:
          %u: bv32 = call "T::F(int)"(%i)
          %i1 "i": bv32 = add %i, %one
          %c1: bool = slt %i1, %n
          br %c1, B4, B6
        B4:
          %u1: bv32 = call "T::F(int)"(%i1)
          %i2 "i": bv32 = add %i1, %one
          goto B1
        B5:
          ret %i
        B6:
          ret %i1
        """;

    // The first iteration runs before the loop, under its own test.
    private const string Peeled = Header + """
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          %c0: bool = slt %z, %n
          br %c0, B1, B5
        B1:
          %u0: bv32 = call "T::F(int)"(%z)
          %j "i": bv32 = add %z, %one
          goto B2
        B2:
          %i "i": bv32 = phi [B1: %j, B3: %i1]
          %c: bool = slt %i, %n
          br %c, B3, B4
        B3:
          %u: bv32 = call "T::F(int)"(%i)
          %i1 "i": bv32 = add %i, %one
          goto B2
        B4:
          ret %i
        B5:
          ret %z
        """;

    public static int Run()
    {
        bool ok = Check("a loop against itself", Loop, Loop, "1:1, no offset");
        ok &= Check("a loop against its 2:1 unrolling", Loop, Unrolled, "2:1, no offset");
        ok &= Check("a 2:1 unrolling against the loop", Unrolled, Loop, "1:2, no offset");
        ok &= Check("a loop against one peeled iteration", Loop, Peeled, "1:1, offset 1 legacy and 0 modern");
        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(string name, string oldText, string newText, string expected)
    {
        Row row = Program.Analyse("self-test", name, "n/a", IrText.Parse(oldText), IrText.Parse(newText));
        bool ok = string.Equals(row.Schedule, expected, StringComparison.Ordinal) && row.OutcomesAgree;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {row.Schedule ?? row.Cause} (expected {expected}); {row.Usable} usable runs, {row.Reaching} reach the loop, at most {row.MostVisits} header visits");
        return ok;
    }
}
