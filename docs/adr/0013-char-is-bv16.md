# ADR 0013: Lower C# `char` to `BitVec(16)`, not `BitVec(32)`

Status: proposed (2026-09-19)

## Context
The M2-003 Design type map says `int/uint/char`=bv32. C# `char` is a 16-bit unsigned
integer (values U+0000 to U+FFFF, the same range as `ushort`). If it is bv32, an `int`-to-`char` conversion is
same-width, so the lowerer emits no `Trunc`. `(int)(char)70000` would lower to 70000, but
C# evaluates it to 4464. The checked form `checked((char)70000)` would not throw in the IR,
but C# throws `System.OverflowException`. The lowering oracle cannot catch this because its
generator only covers `int`/`long`/`bool`. Found while starting M2-003, before any code was
written.

## Decision
`TypeMapper` maps `char` to `BitVec(16)` and treats it as unsigned, the same as `ushort`.
The M2-003 type map becomes: `sbyte/byte`=bv8, `short/ushort/char`=bv16, `int/uint`=bv32,
`long/ulong`=bv64.

## Why
- The IR types exist to model C# values exactly (VERIFICATION-MODEL.md section 2: "`BitVec(n)`
  for integral types"). A width that is wrong for a type gives wrong IR for every narrowing
  conversion into that type, both checked and unchecked.
- A wrong lowering is the same on both sides, so it rarely causes a false Divergent. It can
  still cause a false Equivalent. For example, legacy `(char)x` and modern `(char)(x & 0xFFFF)`
  are the same function in C#, but they lower differently. In the other direction, legacy
  `(int)(char)x` and modern `x` differ in C#, yet with bv32 they lower to the same IR. Two
  different programs lowering to the same IR is the unsound case the model forbids
  ("false alarms are cheaper than false proofs").
- `char` arithmetic is already promoted to `int` by an explicit `IConversionOperation` in
  the CFG, so the M2-003 pitfall about operand widths still holds. With bv16, that
  conversion becomes the `ZExt` it actually is.

## Rejected
- Keep bv32 as written: the lowering would be knowingly wrong for `int`-to-`char`
  conversions, with no test that notices.
- Lower `char` to `Sort`: this sends every `char` comparison and conversion to `IrOpaque`,
  losing precision for no reason.

## Consequences
- The M2-003 Design sentence changes as above. No other doc names `char`'s width.
- The M2-003 unit tests for `TypeMapper` and the conversion lowering pin bv16. The lowering
  oracle generator stays `int`/`long`/`bool` as the ticket says.
- No effect on M3 encoding: bv16 is already an allowed width (`IrBitVec` accepts 8/16/32/64).
