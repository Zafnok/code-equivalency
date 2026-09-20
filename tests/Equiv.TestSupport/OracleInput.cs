namespace Equiv.TestSupport;

/// <summary>
/// Arguments for an <see cref="OracleMethod"/>, in parameter order. <see cref="SIsNull"/> chooses
/// between <c>null</c> and a non-null string for the reference parameter <c>s</c>.
/// </summary>
public sealed record OracleInput(int A, int B, long C, long D, bool E, bool SIsNull);
