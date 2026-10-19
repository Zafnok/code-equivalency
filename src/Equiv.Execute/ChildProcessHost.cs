using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Equiv.Execute;

/// <summary>
/// Starts real driver processes (ticket M3-032): a <c>.exe</c> directly, a <c>.dll</c> through <c>dotnet</c>. While a
/// case runs, the process is polled, and killed when it outlives the case timeout or its private memory passes
/// <paramref name="memoryLimitBytes"/>. A host bound by <see cref="Within"/> starts every process in one folder under the
/// bound directory (ticket P2-040), so both drivers of a replay or <c>probe</c> case see the same working directory and an
/// outcome that holds it is the runtimes' own (ticket P2-254). The folder is emptied whenever a process starts in it while
/// no other runs there, so a replay's modern side never sees what its legacy side wrote. An unbound host's processes
/// inherit the caller's working directory.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "M3-032: starts the real driver processes; covered by RuntimeDiffTests, (P2-040) DriverWorkingDirectoryTests and (P2-254) ProbeTests in Equiv.Tests.Integration")]
public sealed class ChildProcessHost(long memoryLimitBytes) : IDriverHost
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
        if (modern)
        {
            info.ArgumentList.Add(driver);
        }

        WorkingDirectory? working = Working;
        if (working is not null)
        {
            info.WorkingDirectory = working.Enter();
        }

        try
        {
            return new Session(Process.Start(info) ?? throw new InvalidOperationException($"could not start {driver}"), memoryLimitBytes, working);
        }
        catch
        {
            working?.Leave();
            throw;
        }
    }

    public IDriverHost Within(string directory) => new ChildProcessHost(memoryLimitBytes) { Working = new WorkingDirectory(directory) };

    /// <summary>The folder a bound host's processes start in; null for an unbound host.</summary>
    private WorkingDirectory? Working { get; init; }

    /// <summary>A bound host's one <c>cwd-&lt;random&gt;</c> folder under its directory, and how many processes run in it.</summary>
    private sealed class WorkingDirectory(string directory)
    {
        private readonly Lock gate = new();

        private readonly string path = Path.Combine(directory, "cwd-" + Path.GetRandomFileName());

        private int running;

        /// <summary>The folder's full path, emptied first when no process runs in it.</summary>
        public string Enter()
        {
            lock (gate)
            {
                if (running == 0 && Directory.Exists(path))
                {
                    Empty(path);
                }

                running++;
                return Directory.CreateDirectory(path).FullName;
            }
        }

        public void Leave()
        {
            lock (gate)
            {
                running--;
            }
        }

        private static void Empty(string folder)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A file a killed process tree still holds outlives it; what is left stays, as it did in the caller's
                // directory before ticket P2-040, rather than end the run.
            }
        }
    }

    private sealed class Session(Process process, long memoryLimitBytes, WorkingDirectory? working) : IDriverSession
    {
        private bool disposed;

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
            if (disposed)
            {
                return;
            }

            disposed = true;
            Kill();

            // Kill only signals: until the process is gone it still holds the driver's files, so a caller that deletes
            // the driver's folder next can fail with UnauthorizedAccessException (seen once in RuntimeDiffTests, PR #291).
            process.WaitForExit(ExitWait);
            process.Dispose();
            working?.Leave();
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
