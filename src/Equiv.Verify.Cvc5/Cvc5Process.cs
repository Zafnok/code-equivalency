using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Equiv.Verify.Cvc5;

/// <summary>The one place a solver process is started (ADR 0050 decision 1; ticket P1-033).</summary>
internal static class Cvc5Process
{
    /// <summary>
    /// Runs <paramref name="path"/> with <paramref name="arguments"/> and, when given, <paramref name="script"/> as a
    /// file after them, and returns what it wrote to standard output; null when it was still running after
    /// <paramref name="killAfter"/> and was killed.
    /// </summary>
    internal delegate string? Runner(string path, IReadOnlyList<string> arguments, string? script, TimeSpan killAfter);

    /// <summary>
    /// The script goes in a temporary file, deleted afterwards: a query prints to megabytes, and a file is what cvc5's
    /// documented command line takes. A process killed at the limit is waited for, with every process it started.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "P1-033: starts the cvc5 process; covered by Cvc5IntegrationTests on CI's Windows leg, where the executable is fetched.")]
    internal static string? Run(string path, IReadOnlyList<string> arguments, string? script, TimeSpan killAfter)
    {
        string? file = script is null ? null : Path.Combine(Path.GetTempPath(), $"equiv-{Guid.NewGuid():N}.smt2");
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true },
            };
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (file is not null)
            {
                File.WriteAllText(file, script);
                process.StartInfo.ArgumentList.Add(file);
            }

            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(killAfter))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return null;
            }

            // What cvc5 cannot read it says on standard error, with nothing on standard output; the parser reads either.
            string text = output.GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(text) ? error.GetAwaiter().GetResult() : text;
        }
        finally
        {
            if (file is not null)
            {
                File.Delete(file);
            }
        }
    }
}
