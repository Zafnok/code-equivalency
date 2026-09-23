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
        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", config, CancellationToken.None).Match;

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
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { Renames = renames }, CancellationToken.None).Match;

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
        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None).Match;

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

    // Declared as a separate referenced assembly, not inlined into the compilation under test: an
    // inlined fake attribute class's own (explicit) constructor would otherwise show up as just another
    // procedure for ProcedureEnumerator/CSharpFrontend.Analyze to enumerate and match.
    private static readonly MetadataReference LegacyRouteAttributes = RoslynTestCompilations.Compile(
        """
        namespace System.Web.Http
        {
            public class RouteAttribute : System.Attribute { public RouteAttribute(string template = null) { } }
            public class HttpGetAttribute : System.Attribute { }
            public class HttpPostAttribute : System.Attribute { }
        }
        """,
        "LegacyRouteAttributes").ToReference();

    private static readonly MetadataReference ModernRouteAttributes = RoslynTestCompilations.Compile(
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class RouteAttribute : System.Attribute { public RouteAttribute(string template = null) { } }
            public class HttpGetAttribute : System.Attribute { public HttpGetAttribute(string template = null) { } }
            public class HttpPostAttribute : System.Attribute { public HttpPostAttribute(string template = null) { } }
        }
        """,
        "ModernRouteAttributes").ToReference();

    /// <summary>
    /// M2-005 acceptance criterion 3: an endpoint match bridges legacy/modern actions whose plain C#
    /// identities differ (different namespaces here, with no user rename map at all), and it becomes
    /// the pair's identity (both criterion 3's "MatchResult gains nothing" and criterion 4's SARIF
    /// <c>fullyQualifiedName</c> need the pair's own <see cref="ProcedureIdentity.Value"/> to already be
    /// <c>"VERB /template"</c>). A route with no counterpart at all on the other side — legacy-only
    /// (<c>Old.Ns.OrdersController.Delete</c>) and modern-only (<c>New.Ns.OrdersController.Create</c>) —
    /// is unaffected: both fall through to ordinary Removed/Added handling instead of ever reaching the
    /// endpoint rename map.
    /// </summary>
    [Fact]
    public void Analyze_EndpointRenameMapBeforeUserMap()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace Old.Ns
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpPost, Route("api/orders/gone")]
                    public void Delete() { }
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace New.Ns
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpPost("api/orders/new")]
                    public void Create() { }
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None).Match;

        ProcedurePair pair = Assert.Single(result.Pairs);
        Assert.Equal("GET /api/orders/{id}", pair.Old.Value);
        Assert.Equal("GET /api/orders/{id}", pair.New.Value);
        ProcedureIdentity removed = Assert.Single(result.Removed);
        Assert.Contains("Delete", removed.Value, StringComparison.Ordinal);
        ProcedureIdentity added = Assert.Single(result.Added);
        Assert.Contains("Create", added.Value, StringComparison.Ordinal);
        Assert.Empty(result.Ambiguous);
    }

    /// <summary>
    /// M2-005 acceptance criterion 3: "ahead of the user's rename map (user entries win on conflict)".
    /// The legacy action's namespace is explicitly renamed by the user to a namespace that is NOT where
    /// the modern action actually lives, so the config rename map changes this action's own identity;
    /// the endpoint rename map must not override that explicit choice by silently pairing it with the
    /// modern action via the route match instead.
    /// </summary>
    [Fact]
    public void UserRenameMapWinsOverEndpointRenameOnConflict()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace Old.Ns
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace Other.Ns
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "Different.Ns") };
        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { Renames = renames }, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Empty(result.Ambiguous);
        Assert.Single(result.Added);
        Assert.Single(result.Removed);
    }

    /// <summary>
    /// M2-005 acceptance criterion 3: a (Verb, Template) with duplicates on one side but present on both
    /// is ignored for the rename map and every action sharing it lands in <see cref="MatchResult.Ambiguous"/>
    /// instead (SARIF Unknown(UnmatchedOverload) wiring is M3-003).
    /// </summary>
    [Fact]
    public void Analyze_DuplicateEndpointYieldsUnknown()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpGet, Route("api/orders/{id}")]
                    public int GetOrder(int id) => id;
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal(3, result.Ambiguous.Length);
    }

    /// <summary>The mirror of <see cref="Analyze_DuplicateEndpointYieldsUnknown"/>: duplicates on the modern side this time.</summary>
    [Fact]
    public void DuplicateEndpointOnTheModernSideYieldsAmbiguous()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpGet("api/orders/{id}")]
                    public int GetOrder(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [])
            : new LoadedSolution(null!, [modernCompilation], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal(3, result.Ambiguous.Length);
    }
}
