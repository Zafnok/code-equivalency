using CheckCoverage;

using Xunit;

namespace CheckCoverage.Tests;

public sealed class CoberturaCoverageReaderTests
{
    [Fact]
    public void SingleFullyCoveredFileReportsFullRates()
    {
        const string Xml = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="false" />
                        <line number="2" hits="1" branch="true" condition-coverage="100% (2/2)">
                          <conditions>
                            <condition number="0" type="jump" coverage="100%" />
                            <condition number="1" type="jump" coverage="100%" />
                          </conditions>
                        </line>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        IReadOnlyDictionary<string, AssemblyCoverage> result = CoberturaCoverageReader.Merge([Xml]);

        AssemblyCoverage coverage = result["Equiv.Core"];
        Assert.Equal(1.0, coverage.LineRate);
        Assert.Equal(1.0, coverage.BranchRate);
    }

    [Fact]
    public void ReportsWithDifferentSourceRootsMergeTheSameFile()
    {
        const string FromRepoRoot = """
            <coverage>
              <sources><source>D:/repo/</source></sources>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="src/Equiv.Core/Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="false" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        const string FromProjectDir = """
            <coverage>
              <sources><source>D:\repo\src\Equiv.Core\</source></sources>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="0" branch="false" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        AssemblyCoverage coverage = CoberturaCoverageReader.Merge([FromRepoRoot, FromProjectDir])["Equiv.Core"];

        Assert.Equal(1, coverage.LinesValid);
        Assert.Equal(1, coverage.LinesCovered);
    }

    [Fact]
    public void WithSeveralSourcesAFileIsAnchoredWhereItExists()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "Foo.cs"), string.Empty);
        string several = $"""
            <coverage>
              <sources><source>/_/src/</source><source>{root}</source></sources>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="src/Foo.cs">
                      <lines><line number="1" hits="1" branch="false" /></lines>
                    </class>
                  </classes>
                </package>
                <package name="ThirdParty">
                  <classes>
                    <class name="ThirdParty.Bar" filename="Bar.cs">
                      <lines><line number="1" hits="1" branch="false" /></lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        string single = $"""
            <coverage>
              <sources><source>{Path.Combine(root, "src")}</source></sources>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines><line number="1" hits="0" branch="false" /></lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        IReadOnlyDictionary<string, AssemblyCoverage> result = CoberturaCoverageReader.Merge([several, single]);

        Assert.Equal((1, 1), (result["Equiv.Core"].LinesValid, result["Equiv.Core"].LinesCovered));
        Assert.Equal(1, result["ThirdParty"].LinesCovered);
    }

    [Fact]
    public void UncoveredLineLowersLineRate()
    {
        const string Xml = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="false" />
                        <line number="2" hits="0" branch="false" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        AssemblyCoverage coverage = CoberturaCoverageReader.Merge([Xml])["Equiv.Core"];

        Assert.Equal(0.5, coverage.LineRate);
        Assert.Equal(1, coverage.LinesCovered);
        Assert.Equal(2, coverage.LinesValid);
    }

    [Fact]
    public void UncoveredConditionLowersBranchRate()
    {
        const string Xml = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="true" condition-coverage="50% (1/2)">
                          <conditions>
                            <condition number="0" type="jump" coverage="100%" />
                            <condition number="1" type="jump" coverage="0%" />
                          </conditions>
                        </line>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        AssemblyCoverage coverage = CoberturaCoverageReader.Merge([Xml])["Equiv.Core"];

        Assert.Equal(0.5, coverage.BranchRate);
        Assert.Equal(1, coverage.BranchesCovered);
        Assert.Equal(2, coverage.BranchesValid);
    }

    [Fact]
    public void SameLineCoveredInOneReportButNotAnotherMergesAsCovered()
    {
        const string RunOneUncovered = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="0" branch="true" condition-coverage="0% (0/2)">
                          <conditions>
                            <condition number="0" type="jump" coverage="0%" />
                            <condition number="1" type="jump" coverage="0%" />
                          </conditions>
                        </line>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        const string RunTwoCoveredBothBranches = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="3" branch="true" condition-coverage="100% (2/2)">
                          <conditions>
                            <condition number="0" type="jump" coverage="100%" />
                            <condition number="1" type="jump" coverage="100%" />
                          </conditions>
                        </line>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        AssemblyCoverage coverage = CoberturaCoverageReader.Merge([RunOneUncovered, RunTwoCoveredBothBranches])["Equiv.Core"];

        Assert.Equal(1.0, coverage.LineRate);
        Assert.Equal(1.0, coverage.BranchRate);
    }

    [Fact]
    public void ComplementaryBranchesAcrossReportsMergeToFullBranchCoverage()
    {
        const string RunOneCoversFirstBranch = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="true" condition-coverage="50% (1/2)">
                          <conditions>
                            <condition number="0" type="jump" coverage="100%" />
                            <condition number="1" type="jump" coverage="0%" />
                          </conditions>
                        </line>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        const string RunTwoCoversSecondBranch = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="true" condition-coverage="50% (1/2)">
                          <conditions>
                            <condition number="0" type="jump" coverage="0%" />
                            <condition number="1" type="jump" coverage="100%" />
                          </conditions>
                        </line>
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        AssemblyCoverage coverage = CoberturaCoverageReader.Merge([RunOneCoversFirstBranch, RunTwoCoversSecondBranch])["Equiv.Core"];

        Assert.Equal(1.0, coverage.BranchRate);
    }

    [Fact]
    public void MultiplePackagesInOneReportAreKeptSeparate()
    {
        const string Xml = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs">
                      <lines>
                        <line number="1" hits="1" branch="false" />
                      </lines>
                    </class>
                  </classes>
                </package>
                <package name="Equiv.Cli">
                  <classes>
                    <class name="Equiv.Cli.Bar" filename="Bar.cs">
                      <lines>
                        <line number="1" hits="0" branch="false" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        IReadOnlyDictionary<string, AssemblyCoverage> result = CoberturaCoverageReader.Merge([Xml]);

        Assert.Equal(1.0, result["Equiv.Core"].LineRate);
        Assert.Equal(0.0, result["Equiv.Cli"].LineRate);
    }

    [Fact]
    public void NoPackagesReturnsEmptyResult()
    {
        const string Xml = "<coverage><packages /></coverage>";

        IReadOnlyDictionary<string, AssemblyCoverage> result = CoberturaCoverageReader.Merge([Xml]);

        Assert.Empty(result);
    }

    [Fact]
    public void ClassWithoutLinesElementContributesNoLines()
    {
        const string Xml = """
            <coverage>
              <packages>
                <package name="Equiv.Core">
                  <classes>
                    <class name="Equiv.Core.Foo" filename="Foo.cs" />
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        AssemblyCoverage coverage = CoberturaCoverageReader.Merge([Xml])["Equiv.Core"];

        Assert.Equal(0, coverage.LinesValid);
        Assert.Equal(1.0, coverage.LineRate);
    }

    [Fact]
    public void NullDocumentsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => CoberturaCoverageReader.Merge(null!));
    }
}
