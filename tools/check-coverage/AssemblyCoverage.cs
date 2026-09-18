namespace CheckCoverage;

internal sealed record AssemblyCoverage(string AssemblyName, int LinesValid, int LinesCovered, int BranchesValid, int BranchesCovered)
{
    private const double FullyCovered = 1.0;

    public double LineRate => this.LinesValid == 0 ? FullyCovered : (double)this.LinesCovered / this.LinesValid;

    public double BranchRate => this.BranchesValid == 0 ? FullyCovered : (double)this.BranchesCovered / this.BranchesValid;
}
