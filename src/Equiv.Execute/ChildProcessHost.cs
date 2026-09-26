using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Equiv.Execute;

/// <summary>
/// Starts real driver processes (ticket M3-032): a <c>.exe</c> directly, a <c>.dll</c> through <c>dotnet</c>. While a
/// case runs, the process is polled, and killed when it outlives the case timeout or its private memory passes
/// <paramref name="memoryLimitBytes"/>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "M3-032: starts the real driver processes; covered by RuntimeDiffTests in Equiv.Tests.Integration")]
public sealed class ChildProcessHost(long memoryLimitBytes) : IDriverHost
{
    public const long DefaultMemoryLimitBytes = 1L << 30;

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    // Every line either way is ASCII (JsonText), so no code page can change it.
    private static readonly Encoding Wire = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public IDriverSession Start(string driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        bool modern = driver.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        ProcessStartInfo info = new(modern ? "dotnet" : driver)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = Wire,
            StandardOutputEncoding = Wire,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (modern)
        {
            info.ArgumentList.Add(driver);
        }

        return new Session(Process.Start(info) ?? throw new InvalidOperationException($"could not start {driver}"), memoryLimitBytes);
    }

    private sealed class Session(Process process, long memoryLimitBytes) : IDriverSession
    {
        public string? Exchange(string line, TimeSpan timeout)
        {
            try
            {
                process.StandardInput.WriteLine(line);
                process.StandardInput.Flush();
            }
            catch (IOException)
            {
                return null;
            }

            Task<string?> answer = process.StandardOutput.ReadLineAsync();
            Stopwatch clock = Stopwatch.StartNew();
            while (!answer.Wait(Poll))
            {
                process.Refresh();
                if (process.HasExited || clock.Elapsed > timeout || process.PrivateMemorySize64 > memoryLimitBytes)
                {
                    Kill();
                    return null;
                }
            }

            return answer.Result;
        }

        public void Dispose()
        {
            Kill();
            process.Dispose();
        }

        private void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }
    }
}
