using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Equiv.Execute;

/// <summary>
/// Starts real driver processes (ticket M3-032): a <c>.exe</c> directly, a <c>.dll</c> through <c>dotnet</c>. While a
/// case runs, the process is polled, and killed when it outlives the case timeout or its private memory passes
/// <paramref name="memoryLimitBytes"/>. With a <paramref name="workingRoot"/>, every process starts in a fresh folder under
/// it (ticket P2-040); without one it inherits the caller's working directory.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "M3-032: starts the real driver processes; covered by RuntimeDiffTests and (P2-040) DriverWorkingDirectoryTests in Equiv.Tests.Integration")]
public sealed class ChildProcessHost(long memoryLimitBytes, string? workingRoot = null) : IDriverHost
{
    public const long DefaultMemoryLimitBytes = 1L << 30;

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    /// <summary>How long disposing a session waits for its killed process to exit and release the driver's files.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

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
        if (workingRoot is not null)
        {
            info.WorkingDirectory = Directory.CreateDirectory(Path.Combine(workingRoot, "cwd-" + Path.GetRandomFileName())).FullName;
        }

        if (modern)
        {
            info.ArgumentList.Add(driver);
        }

        return new Session(Process.Start(info) ?? throw new InvalidOperationException($"could not start {driver}"), memoryLimitBytes);
    }

    public IDriverHost Within(string directory) => new ChildProcessHost(memoryLimitBytes, directory);

    private sealed class Session(Process process, long memoryLimitBytes) : IDriverSession
    {
        public string? Exchange(string line, TimeSpan timeout)
        {
            // Writing is inside the deadline too: a driver that never reads stdin fills the pipe (4 KB on Windows), and a
            // synchronous write then blocks with no deadline at all (ticket P2-039). Killing the process breaks the pipe,
            // which ends the write or read still running on the pool.
            Task<string?> answer = Task.Run(() => Send(line));
            Stopwatch clock = Stopwatch.StartNew();
            while (!answer.Wait(Poll))
            {
                process.Refresh();
                if (clock.Elapsed > timeout)
                {
                    Kill();
                    throw new TimeoutException(OutcomeLine.TimedOut(timeout));
                }

                if (process.HasExited || process.PrivateMemorySize64 > memoryLimitBytes)
                {
                    Kill();
                    return null;
                }
            }

            return answer.IsCompletedSuccessfully ? answer.Result : null;
        }

        public void Dispose()
        {
            Kill();

            // Kill only signals: until the process is gone it still holds the driver's files, so a caller that deletes
            // the driver's folder next can fail with UnauthorizedAccessException (seen once in RuntimeDiffTests, PR #291).
            process.WaitForExit(ExitWait);
            process.Dispose();
        }

        /// <summary>Writes one case and reads one answer; null when the pipe broke, as when the driver died.</summary>
        private string? Send(string line)
        {
            try
            {
                process.StandardInput.WriteLine(line);
                process.StandardInput.Flush();
                return process.StandardOutput.ReadLine();
            }
            catch (IOException)
            {
                return null;
            }
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
