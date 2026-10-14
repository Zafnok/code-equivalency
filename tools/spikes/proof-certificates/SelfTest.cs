using Equiv.Core;
using Equiv.Core.Ir;

namespace ProofCertificatesSpike;

/// <summary>
/// The ticket's test: one sample pair, <c>a + b</c> against <c>b + a</c> over 32-bit integers, proved <c>bounded</c>.
/// Its queries export, cvc5 proves each unsatisfiable with an Alethe proof, and Carcara accepts each proof with no hole.
/// </summary>
internal static class SelfTest
{
    private const string Header = "proc \"T::M(int,int)\" (%a \"a\": bv32, %b \"b\": bv32) -> bv32 entry B0\nB0:\n";

    private const string Sum = Header + """
          %r: bv32 = add %a, %b
          ret %r
        """;

    private const string SumCommuted = Header + """
          %r: bv32 = add %b, %a
          ret %r
        """;

    public static int Run(Certifier certifier, VerificationOptions options, string outDir)
    {
        Directory.CreateDirectory(outDir);
        (List<ProofQuery> queries, string? why) = ProofQueries.Of(ProofQueries.Bounded, IrText.Parse(Sum), IrText.Parse(SumCommuted), options);
        bool ok = why is null && queries.Count == 2;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} a + b against b + a: {why ?? $"{queries.Count} unsatisfiable queries"}");
        foreach (ProofQuery query in queries)
        {
            ok &= query.Printed == ProofQueries.ByPrint;
            string file = Path.Combine(outDir, $"self-test-{query.Name}.smt2");
            File.WriteAllText(file, query.Script);
            Certificate certificate = certifier.Certify(file);
            bool accepted = certificate is { Cvc5: "unsat", ProofWritten: true, Carcara: Certifier.Valid };
            ok &= accepted;
            Console.WriteLine($"{(accepted ? "ok  " : "FAIL")} {query.Name}: cvc5 {certificate.Cvc5} in {certificate.Cvc5Ms} ms, proof {certificate.ProofBytes} bytes, Carcara {certificate.Carcara} in {certificate.CheckMs} ms {certificate.Detail} {string.Join("; ", certificate.Holes)}");
        }

        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }
}
