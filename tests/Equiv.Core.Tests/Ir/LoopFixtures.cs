namespace Equiv.Core.Tests.Ir;

/// <summary>Hand-written looping IR shared by the loop analysis, unroller and fragmenter tests (ticket M3-002 deliverable 1).</summary>
internal static class LoopFixtures
{
    /// <summary><c>s = 0; for (i = 0; i &lt; n; i++) s += i; return s;</c></summary>
    public const string Single = """
        proc "T::Sum(int)" (%n "n": bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i "i": bv32 = phi [B0: %z, B2: %i1]
          %s "s": bv32 = phi [B0: %z, B2: %s1]
          %c: bool = slt %i, %n
          br %c, B2, B3
        B2:
          %s1 "s": bv32 = add %s, %i
          %i1 "i": bv32 = add %i, %one
          goto B1
        B3:
          ret %s
        """;

    /// <summary>
    /// <c>for (i = 0; i &lt; n; i++) for (j = 0; j &lt; i; j++) s++;</c>; the outer latch reads the inner header's
    /// phi <c>t</c> after the inner loop exits.
    /// </summary>
    public const string Nested = """
        proc "T::Nested(int)" (%n "n": bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i "i": bv32 = phi [B0: %z, B4: %i1]
          %s "s": bv32 = phi [B0: %z, B4: %t]
          %c: bool = slt %i, %n
          br %c, B2, B5
        B2:
          %j "j": bv32 = phi [B1: %z, B3: %j1]
          %t "s": bv32 = phi [B1: %s, B3: %t1]
          %d: bool = slt %j, %i
          br %d, B3, B4
        B3:
          %t1 "s": bv32 = add %t, %one
          %j1 "j": bv32 = add %j, %one
          goto B2
        B4:
          %i1 "i": bv32 = add %i, %one
          goto B1
        B5:
          ret %s
        """;

    /// <summary><c>for (i = 0; i &lt; n; i++) if (i == k) return i; return n;</c></summary>
    public const string EarlyReturn = """
        proc "T::Find(int,int)" (%n "n": bv32, %k "k": bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i "i": bv32 = phi [B0: %z, B4: %i1]
          %c: bool = slt %i, %n
          br %c, B2, B5
        B2:
          %e: bool = eq %i, %k
          br %e, B3, B4
        B3:
          ret %i
        B4:
          %i1 "i": bv32 = add %i, %one
          goto B1
        B5:
          ret %n
        """;

    /// <summary>
    /// <c>for (i = 0; i &lt; n; i++) { if (i == k) throw; r = i; }</c> with <c>r</c> a <c>ref</c> parameter whose
    /// final value is live on both exits.
    /// </summary>
    public const string Throws = """
        proc "T::Check(int,int,ref int)" (%n "n": bv32, %k "k": bv32, ref %r "r": bv32) entry B0
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i "i": bv32 = phi [B0: %z, B4: %i1]
          %r1 "r": bv32 = phi [B0: %r, B4: %i]
          %c: bool = slt %i, %n
          br %c, B2, B5
        B2:
          %e: bool = eq %i, %k
          br %e, B3, B4
        B3:
          throw "System.InvalidOperationException" outs(%r = %r1)
        B4:
          %i1 "i": bv32 = add %i, %one
          goto B1
        B5:
          ret outs(%r = %r1)
        """;
}
