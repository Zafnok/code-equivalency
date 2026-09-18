using CheckCoverage;

using Xunit;

namespace CheckCoverage.Tests;

public sealed class ExcludeFromCoverageAuditorTests
{
    [Fact]
    public void NoAttributeNoViolations()
    {
        const string Source = "public sealed class Foo { }";

        Assert.Empty(ExcludeFromCoverageAuditor.AuditSource("Foo.cs", Source));
    }

    [Fact]
    public void AttributeWithTicketedJustificationNoViolations()
    {
        const string Source = """
            [ExcludeFromCodeCoverage(Justification = "M0-003: process entry point")]
            public sealed class Foo { }
            """;

        Assert.Empty(ExcludeFromCoverageAuditor.AuditSource("Foo.cs", Source));
    }

    [Fact]
    public void AttributeWithoutJustificationIsAViolation()
    {
        const string Source = """
            [ExcludeFromCodeCoverage]
            public sealed class Foo { }
            """;

        IReadOnlyList<CoverageExclusionViolation> violations = ExcludeFromCoverageAuditor.AuditSource("Foo.cs", Source);

        CoverageExclusionViolation violation = Assert.Single(violations);
        Assert.Equal("Foo.cs", violation.FilePath);
        Assert.Equal(1, violation.LineNumber);
        Assert.Contains("no Justification", violation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeWithJustificationMissingTicketIdIsAViolation()
    {
        const string Source = """
            [ExcludeFromCodeCoverage(Justification = "just because")]
            public sealed class Foo { }
            """;

        IReadOnlyList<CoverageExclusionViolation> violations = ExcludeFromCoverageAuditor.AuditSource("Foo.cs", Source);

        CoverageExclusionViolation violation = Assert.Single(violations);
        Assert.Contains("does not name a ticket id", violation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeOnSecondLineReportsCorrectLineNumber()
    {
        const string Source = "namespace Foo;\n\n[ExcludeFromCodeCoverage]\npublic sealed class Foo { }";

        CoverageExclusionViolation violation = Assert.Single(ExcludeFromCoverageAuditor.AuditSource("Foo.cs", Source));

        Assert.Equal(3, violation.LineNumber);
    }

    [Fact]
    public void MultipleAttributesInOneFileAreAllAudited()
    {
        const string Source = """
            [ExcludeFromCodeCoverage(Justification = "M0-003: fine")]
            public sealed class Foo { }

            [ExcludeFromCodeCoverage]
            public sealed class Bar { }
            """;

        IReadOnlyList<CoverageExclusionViolation> violations = ExcludeFromCoverageAuditor.AuditSource("Foo.cs", Source);

        CoverageExclusionViolation violation = Assert.Single(violations);
        Assert.Equal(4, violation.LineNumber);
    }

    [Fact]
    public void AuditDirectoryScansAllCsFilesRecursively()
    {
        string root = Directory.CreateTempSubdirectory("check-coverage-tests").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "Clean.cs"), "public sealed class Clean { }");
            File.WriteAllText(
                Path.Combine(root, "nested", "Dirty.cs"),
                "[ExcludeFromCodeCoverage]\npublic sealed class Dirty { }");

            IReadOnlyList<CoverageExclusionViolation> violations = ExcludeFromCoverageAuditor.AuditDirectory(root);

            CoverageExclusionViolation violation = Assert.Single(violations);
            Assert.EndsWith("Dirty.cs", violation.FilePath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}