using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ProofCertificatesSpike;

/// <summary>What happened to one exported query: cvc5's answer and proof, and Carcara's verdict on that proof.</summary>
internal sealed record Certificate(
    string Cvc5,
    bool ProofWritten,
    string Carcara,
    long Cvc5Ms,
    long CheckMs,
    long ProofBytes,
    IReadOnlyList<string> Holes,
    string Detail);

/// <summary>
/// Runs cvc5 on an exported query for an Alethe proof, then Carcara on that proof against the query. The flags are those
/// cvc5's own regression tests check Alethe proofs with (<c>test/regress/cli/run_regression.py</c>, <c>AletheTester</c>)
/// against the same Carcara commit, with two differences: no <c>--proof-alethe-testing</c>, which makes cvc5 refuse any
/// bit-vector proof, and no <c>--allowed-rules</c>, which tells Carcara to count unchecked rules as checked. Carcara also
/// gets <c>--ignore-unknown-rules</c>, so a rule it does not know is a hole and not a rejection. ADR 0050's
/// <c>--arrays-exp</c> is kept, as the product passes it.
/// </summary>
internal sealed partial class Certifier(string cvc5, string carcara, string rareFile, int cvc5TimeoutMs, int checkTimeoutMs)
{
    public const string Valid = "valid";
    public const string Holey = "holey";
    public const string Invalid = "invalid";
    public const string MissingRule = "stopped: a RARE rule the rule file lacks";

    public Certificate Certify(string problem)
    {
        string proof = Path.ChangeExtension(problem, ".alethe");
        (int? _, long cvc5Ms, string head, string cvc5Error) = Run(
            cvc5,
            ["--dump-proofs", "--proof-format-mode=alethe", "--proof-granularity=dsl-rewrite", "--arrays-exp", $"--tlimit={cvc5TimeoutMs}", problem],
            proof,
            cvc5TimeoutMs + 30_000);
        string answer = head.Split('\n', 2)[0].Trim();
        string status = answer is "unsat" or "sat" or "unknown" ? answer : cvc5Ms >= cvc5TimeoutMs ? "timeout" : "error";
        if (status != "unsat")
        {
            File.Delete(proof);
            return new Certificate(status, false, "not run", cvc5Ms, 0, 0, [], OneLine(status == "error" ? head + " " + cvc5Error : cvc5Error));
        }

        // cvc5 prints "unsat", then the proof: "(" step... ")", or "(" (error "...") ")" when Alethe cannot express it.
        string text = File.ReadAllText(proof);
        string body = text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..];
        Match error = AletheError().Match(body.Length > 4096 ? body[..4096] : body);
        if (error.Success)
        {
            File.WriteAllText(proof, body);
            return new Certificate(status, false, "not run", cvc5Ms, 0, 0, [], OneLine(error.Groups[1].Value));
        }

        File.WriteAllText(proof, body);
        if (body.Trim().Length == 0)
        {
            // cvc5 found the query unsatisfiable and then ran out of its time limit writing the proof.
            return new Certificate(status, false, "not run", cvc5Ms, 0, 0, [], OneLine("no proof after unsat: " + cvc5Error));
        }

        long bytes = new FileInfo(proof).Length;
        string output = Path.ChangeExtension(problem, ".carcara.txt");
        (int? exit, long checkMs, string checkOutput, string checkError) = Run(
            carcara,
            ["check", "--no-color", "--allow-int-real-subtyping", "--expand-let-bindings", "--ignore-unknown-rules", "--rare-file", rareFile, proof, problem],
            output,
            checkTimeoutMs);
        string last = checkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        checkError = Ansi().Replace(checkError, string.Empty);
        string verdict = exit is null ? "timeout" : last is Valid or Holey or Invalid ? last : "error";

        // Carcara stops at a rare_rewrite step whose rule the rule file does not hold, and says invalid: a step it cannot
        // check, not one it found wrong, so it is counted apart from a rejection.
        if (verdict == Invalid && MissingRareRule().Match(checkError) is { Success: true } missing)
        {
            verdict = MissingRule;
            checkError = "rule " + missing.Groups[1].Value + " is not in the rule file";
        }

        string detail = verdict is Valid ? string.Empty : OneLine(FailedStep().Match(checkError) is { Success: true } step ? $"rule {step.Groups[1].Value}: {FirstLine(checkError)}" : checkError.Length > 0 ? checkError : checkOutput);
        return new Certificate(status, true, verdict, cvc5Ms, checkMs, bytes, verdict == Holey ? Holes(proof) : [], detail);
    }

    /// <summary>
    /// The holes of a holey proof, by name: each <c>hole</c> step's first argument (what cvc5 says it could not
    /// translate), and each rule Carcara does not check. Rule names only, never terms.
    /// </summary>
    private static List<string> Holes(string proof)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(proof))
        {
            Match rule = Rule().Match(line);
            if (!rule.Success)
            {
                continue;
            }

            string name = rule.Groups[1].Value;
            string key = name == "hole" ? "hole: " + (HoleArgument().Match(line) is { Success: true } a ? a.Groups[1].Value : "no argument")
                : Checked.Contains(name) ? string.Empty : "unchecked rule: " + name;
            if (key.Length > 0)
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return [.. counts.OrderByDescending(static c => c.Value).Select(static c => $"{c.Key} x{c.Value}")];
    }

    /// <summary>
    /// Runs a process with stdout streamed to <paramref name="stdoutFile"/>; returns its exit code (null when killed),
    /// the time, the first 4 KiB of stdout, and stderr.
    /// </summary>
    private static (int? Exit, long Ms, string Head, string Error) Run(string exe, string[] arguments, string stdoutFile, int killAfterMs)
    {
        using Process process = new() { StartInfo = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Stopwatch clock = Stopwatch.StartNew();
        process.Start();
        Task<string> error = process.StandardError.ReadToEndAsync();
        Task copy;
        using (FileStream file = File.Create(stdoutFile))
        {
            copy = process.StandardOutput.BaseStream.CopyToAsync(file);
            if (!process.WaitForExit(killAfterMs))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                copy.Wait();
                return (null, clock.ElapsedMilliseconds, string.Empty, string.Empty);
            }

            copy.Wait();
        }

        long took = clock.ElapsedMilliseconds;
        using FileStream read = File.OpenRead(stdoutFile);
        byte[] head = new byte[Math.Min(4096, read.Length)];
        read.ReadExactly(head);
        string stdout = Encoding.UTF8.GetString(head);
        if (read.Length <= 4096)
        {
            return (process.ExitCode, took, stdout, error.Result);
        }

        // Carcara prints its verdict last; keep the tail too.
        read.Seek(-1024, SeekOrigin.End);
        byte[] tail = new byte[1024];
        read.ReadExactly(tail);
        return (process.ExitCode, took, stdout + "\n" + Encoding.UTF8.GetString(tail), error.Result);
    }

    private static string FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    private static string OneLine(string text)
    {
        string line = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length > 300 ? line[..300] : line;
    }

    // The rules Carcara 20bf49c checks (`get_rule` in src/checker/rules/mod.rs), less `hole` and `lia_generic`, which it
    // counts as holes; a step with any other rule is a hole under --ignore-unknown-rules.
    private static readonly HashSet<string> Checked = new(StringComparer.Ordinal)
    {
        "ac_simp", "aci_simp", "and", "and_intro", "and_neg", "and_pos", "and_simplify", "arrays_ext", "arrays_idx",
        "arrays_row", "arrays_row_contra", "beta_equiv", "bfun_elim", "bind", "bind_let", "bitblast_add",
        "bitblast_and", "bitblast_ashr", "bitblast_comp", "bitblast_concat", "bitblast_const", "bitblast_equal",
        "bitblast_extract", "bitblast_lshr", "bitblast_mult", "bitblast_neg", "bitblast_not", "bitblast_or",
        "bitblast_shl", "bitblast_sign_extend", "bitblast_slt", "bitblast_udiv", "bitblast_ult", "bitblast_urem",
        "bitblast_var", "bitblast_xnor", "bitblast_xor", "bool_simplify", "bounded_farkas", "bv_bitwise_slicing",
        "comp_simplify", "concat_conflict", "concat_cprop_prefix", "concat_cprop_suffix", "concat_csplit_prefix",
        "concat_csplit_suffix", "concat_eq", "concat_lprop_prefix", "concat_lprop_suffix", "concat_split_prefix",
        "concat_split_suffix", "concat_unify", "cong", "connective_def", "contraction", "cp_addition", "cp_division",
        "cp_literal", "cp_multiplication", "cp_normalize", "cp_saturation", "distinct_elim", "div_intro",
        "div_simplify", "drat", "drup", "eq_congruent", "eq_congruent_pred", "eq_mp", "eq_reflexive", "eq_simplify",
        "eq_symmetric", "eq_transitive", "equiv1", "equiv2", "equiv_neg1", "equiv_neg2", "equiv_pos1", "equiv_pos2",
        "equiv_simplify", "evaluate", "false", "forall_inst", "ho_cong", "implies", "implies_neg1", "implies_neg2",
        "implies_pos", "implies_simplify", "ite1", "ite2", "ite_intro", "ite_neg1", "ite_neg2", "ite_pos1", "ite_pos2",
        "ite_simplify", "la_disequality", "la_generic", "la_mult_abs_comparison", "la_mult_neg", "la_mult_pos",
        "la_mult_sign", "la_rw_eq", "la_tautology", "la_totality", "let", "log2_intro", "miniscope_distribute",
        "miniscope_ite", "miniscope_split", "minus_simplify", "mod_simplify", "nary_elim", "not_and", "not_equiv1",
        "not_equiv2", "not_implies1", "not_implies2", "not_ite1", "not_ite2", "not_not", "not_or", "not_simplify",
        "not_symm", "not_xor1", "not_xor2", "onepoint", "or", "or_neg", "or_pos", "or_simplify", "pbblast_bvand",
        "pbblast_bvand_ith_bit", "pbblast_bveq", "pbblast_bvsge", "pbblast_bvsgt", "pbblast_bvsle", "pbblast_bvslt",
        "pbblast_bvuge", "pbblast_bvugt", "pbblast_bvule", "pbblast_bvult", "pbblast_bvxor", "pbblast_bvxor_ith_bit",
        "pbblast_pbbconst", "pbblast_pbbvar", "poly_simp", "poly_simp_rel", "prod_simplify", "qnt_cnf", "qnt_join",
        "qnt_rm_unused", "qnt_simplify", "rare_rewrite", "re_concat_unfold_pos", "re_inter",
        "re_kleene_star_unfold_pos", "re_unfold_neg", "re_unfold_neg_concat_fixed_prefix",
        "re_unfold_neg_concat_fixed_suffix", "refl", "reordering", "resolution", "shuffle", "sko_ex", "sko_forall",
        "strict_refl", "strict_resolution", "string_decompose", "string_length_non_empty", "string_length_pos",
        "subproof", "sum_simplify", "symm", "tautology", "th_resolution", "to_int_intro", "trans", "true",
        "unary_minus_simplify", "weakening", "xor1", "xor2", "xor_neg1", "xor_neg2", "xor_pos1", "xor_pos2",
    };

    [GeneratedRegex("""^\s*\(\s*\(error\s+"([^"]*)"\)""")]
    private static partial Regex AletheError();

    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex Ansi();

    [GeneratedRegex(@"the rule (\S+) was not found")]
    private static partial Regex MissingRareRule();

    [GeneratedRegex(@"with rule (\S+)")]
    private static partial Regex FailedStep();

    [GeneratedRegex(@":rule\s+([^\s()]+)")]
    private static partial Regex Rule();

    [GeneratedRegex(@":rule\s+hole\s+:args\s+\(\s*""([^""]*)""")]
    private static partial Regex HoleArgument();
}
