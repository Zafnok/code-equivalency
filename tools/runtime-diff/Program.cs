using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

string work = Directory.CreateTempSubdirectory("runtime-diff-").FullName;
try
{
    RuntimeDiff tool = new(new DriverFactory(), new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes), Console.Out, Console.Error);
    return tool.Run(args, OperatingSystem.IsWindows(), work);
}
finally
{
    Directory.Delete(work, recursive: true);
}
