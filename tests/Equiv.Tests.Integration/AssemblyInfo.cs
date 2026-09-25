using Xunit.Sdk;
using Xunit.v3;

// Many test classes here load the same samples/*.sln concurrently through MSBuildWorkspace, whose design-time
// build writes an incremental-state file (obj/Debug/*.AssemblyReference.cache) per project. Two workspaces
// opening the same solution at once race on that file (observed on windows-latest: FrontendLoadException /
// SolutionLoadException naming "the process cannot access the file... because it is being used by another
// process", on whichever sample-loading test classes xUnit happened to schedule in parallel). Collections
// already serialize within themselves ("Console", "WebApiBasicSample"), but classes without an explicit
// [Collection] each get their own default collection and still run in parallel with every other one. Disabling
// assembly-wide parallelization is the standard fix for MSBuild-workspace integration tests: none of them are
// hot paths, and correctness beats a few extra seconds of wall-clock time.
[assembly: Parallelization(Mode = ParallelMode.None)]
