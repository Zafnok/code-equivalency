using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

string work = Directory.CreateTempSubdirectory("runtime-diff-").FullName;
try
{
    // Each driver runs in its own folder under the temporary one (ticket P2-051): a member that creates, deletes or
    // opens a relative path with a generated name must not touch the caller's working directory.
    IDriverHost host = new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(work);
    RuntimeDiff tool = new(new DriverFactory(), host, Console.Out, Console.Error);
    return tool.Run(args, OperatingSystem.IsWindows(), work);
}
finally
{
    Directory.Delete(work, recursive: true);
}
