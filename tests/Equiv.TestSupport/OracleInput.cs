namespace Equiv.TestSupport;

/// <summary>
/// Arguments for an <see cref="OracleMethod"/>, in parameter order. <see cref="SIsNull"/> chooses
/// between <c>null</c> and a non-null string for the reference parameter <c>s</c>. The array parameters are
/// <c>u = { A, B }</c> and <c>v = { B, A }</c>, or, when <see cref="Aliased"/>, <c>u</c> passed twice.
/// </summary>
public sealed record OracleInput(int A, int B, long C, long D, bool E, bool SIsNull, bool Aliased);
