# runtime-row-guarded-member

The same calls on .NET Framework 4.8 and on .NET 10 (ticket P2-217). `Scratch.cs` is the same text on both sides.
Every path call is `File.Delete`, which has a path-validation row in `runtime-changes.json`: .NET Framework rejects a
path with an invalid character up front, and .NET 10 does not. `File.Exists` is a validating guard: it returns false,
and does not throw, for a path the validation rejects. Here the guarded value is a field, read once at the guard and
again at the call.

- `TearDown` deletes the `readonly` field `log` under `if (File.Exists(this.log))`, as Duplicati's `BorderTests` does.
  Nothing runs between the guard and the call, so both read one value and the row does not fire.
- `Dispose` deletes the mutable field `path` under `!keep && path != null && File.Exists(path)`, then clears it, as
  Duplicati's `TempFile` does. The write comes after the call, so the row does not fire.
- `Move` calls `Retarget` between the guard and the call, and `Retarget` writes `path`. The call may delete a path no
  guard has seen, so the row fires.
- `Retarget` calls no path member.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Scratch..ctor(string,string)` | Equivalent | `congruence` |
| `Scratch.TearDown()` | Equivalent | `congruence` |
| `Scratch.Dispose()` | Equivalent | `congruence` |
| `Scratch.Move(string)` | Runtime exposure with a witness (EQ007: unedited body; exits as a Divergent) | none |
| `Scratch.Retarget(string)` | Equivalent | `congruence` |

Exit code: 1 (a new Divergent result).
