using Equiv.Core.Matching;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// One part of a line of <see cref="BoundSerialiser"/>'s text: the fingerprint component it belongs to and the identity
/// it concerns, as a <see cref="FingerprintDifference"/> names them, and its <see cref="Value"/>, the text itself, which
/// is only ever compared.
/// </summary>
internal sealed record BoundField(string Component, string Identity, string Value);
