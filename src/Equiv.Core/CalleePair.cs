using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>
/// A matched callee pair handed to <see cref="IVerificationBackend.VerifyUnderContracts"/> (ADR 0036 decision 2; ticket
/// P1-010): its identity in the run, as <c>assumedCallees</c> names it, and both lowered bodies.
/// </summary>
public sealed record CalleePair(string Identity, IrProcedure Old, IrProcedure New);
