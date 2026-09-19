using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

public sealed class CSharpFrontendTests
{
    private sealed class StubLoader(Func<string, LoadedSolution> load) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct) => Task.FromResult(load(solutionPath));
    }

    private sealed class ThrowingLoader(SolutionLoadException exception) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct) => throw exception;
    }

    [Theory]
    [InlineData("a.sln", true)]
    [InlineData("a.slnx", true)]
    [InlineData("a.SLN", true)]
    [InlineData("a.csproj", false)]
    [InlineData("a.txt", false)]
    public void SupportsOnlySlnAndSlnx(string path, bool expected) =>
        Assert.Equal(expected, new CSharpFrontend(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher()).Supports(path));

    [Fact]
    public void PublicConstructorWiresProductionCollaborators() =>
        Assert.Equal("csharp", new CSharpFrontend().Language);

    [Fact]
    public void LanguageIsCsharp() =>
        Assert.Equal("csharp", new CSharpFrontend(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher()).Language);

    [Fact]
    public void AppliesRenameMapBeforeMatching()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace Old.Ns { public class C { public void M() {} } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace New.Ns { public class C { public void M() {} } }");

        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "New.Ns") };
        EquivConfig config = EquivConfig.Default with { Renames = renames };

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());
        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", config, CancellationToken.None);

        Assert.Single(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void LowersBothBodiesOfEveryMatchedPair()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace Old.Ns { public class C { public int M(int a) => a + 1; public void F(int a) {} public void F(long a) {} } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace New.Ns { public class C { public int M(int a) => a + 2; } }");

        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "New.Ns") };
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { Renames = renames }, CancellationToken.None);

        ProcedurePair pair = Assert.Single(result.Pairs);
        Assert.Equal(pair.Old, pair.OldBody!.Identity);
        Assert.Equal(pair.New, pair.NewBody!.Identity);
        Assert.Empty(IrValidator.Validate(pair.OldBody));
        Assert.Empty(IrValidator.Validate(pair.NewBody));
        Assert.NotEqual(pair.OldBody, pair.NewBody);
    }

    [Fact]
    public void ProducesAddedAndRemovedWithLocations()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace N { public class C { public void OnlyLegacy() {} } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace N { public class C { public void OnlyModern() {} } }");

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());
        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None);

        ProcedureIdentity removed = Assert.Single(result.Removed);
        ProcedureIdentity added = Assert.Single(result.Added);
        Assert.NotNull(removed.Location);
        Assert.NotNull(added.Location);
        Assert.Equal("N.C::OnlyLegacy()", removed.Value);
        Assert.Equal("N.C::OnlyModern()", added.Value);
    }

    [Fact]
    public void WrapsLoaderFailure()
    {
        SolutionLoadException loadException = new("legacy.sln", []);
        ThrowingLoader loader = new(loadException);
        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());

        FrontendLoadException exception = Assert.Throws<FrontendLoadException>(
            () => frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None));

        Assert.Equal("legacy.sln", exception.Path);
    }

    [Fact]
    public void NullConfigThrows()
    {
        CSharpFrontend frontend = new(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher());
        Assert.Throws<ArgumentNullException>(() => frontend.Analyze("a.sln", "b.sln", null!, CancellationToken.None));
    }
}
