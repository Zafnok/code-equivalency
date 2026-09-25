namespace Equiv.TestSupport;

/// <summary>
/// Arguments for an <see cref="OracleMethod"/>, in parameter order. <see cref="SIsNull"/> chooses
/// between <c>null</c> and a non-null string for the reference parameter <c>s</c>. The array parameters are
/// <c>u = { A, B }</c> and, as <see cref="V"/> says, <c>v = { B, A }</c>, <c>u</c> passed twice, or <c>null</c>. The list
/// parameter is <c>l = { A, B }</c>.
/// </summary>
public sealed record OracleInput(int A, int B, long C, long D, bool E, bool SIsNull, ArrayBinding V);
