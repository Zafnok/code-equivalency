using System.Security.Cryptography;
using System.Text;

using Equiv.Core.Verdicts;

namespace Equiv.Core.Reporting;

/// <summary>
/// A stable identifier for one <see cref="VerificationResult"/>, used to compute
/// <c>baselineState</c> (VERIFICATION-MODEL.md section 6): "procedure identity + verdict + model
/// hash". "Model hash" is read here as the verdict's payload (the counterexample for
/// <see cref="Divergent"/>, the reason and detail for <see cref="Unknown"/>) so that, for example,
/// two different <see cref="Divergent"/> results for the same procedure fingerprint differently —
/// otherwise a genuinely new counterexample on an already-divergent procedure would baseline as
/// <c>unchanged</c>, hiding it.
/// </summary>
internal static class ResultFingerprint
{
    public static string Compute(VerificationResult result)
    {
        string payload = string.Join('\0', result.Identity.Value, Kind(result.Verdict), Model(result.Verdict));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// <see cref="Verdict"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="Removed"/>, mirroring <see cref="VerdictRule.Describe"/>.
    /// </summary>
    private static string Kind(Verdict verdict) => verdict switch
    {
        Equivalent => nameof(Equivalent),
        Divergent => nameof(Divergent),
        Unknown => nameof(Unknown),
        Added => nameof(Added),
        _ => nameof(Removed),
    };

    private static string Model(Verdict verdict) => verdict switch
    {
        Divergent { Observed: { } observed } => ObservationText.Dump(observed),
        Divergent divergent => CounterexampleText.Dump(divergent.Counterexample),
        Unknown unknown => string.Join('\0', unknown.Reason, unknown.Detail),
        _ => string.Empty,
    };
}
