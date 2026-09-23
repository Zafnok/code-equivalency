using System.Collections.Immutable;

using CsCheck;

using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>
/// The loop transformations on 200 generated looping procedures each (ticket M3-002 criterion 6): every unrolled,
/// peeled and fragmented procedure validates with zero diagnostics; unrolling agrees with the original on every
/// input within the bound; in-place unrolling and peeling agree on every input; and every segment is acyclic.
/// </summary>
public sealed class LoopTransformPropertyTests
{
    private static readonly Gen<(IrProcedure Procedure, IrInputs Input)> Looping =
        IrGen.Procedure
            .Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty)
            .SelectMany(static p => IrGen.Inputs(p).Select(i => (p, i)));

    [Fact]
    public void UnrollingValidatesAndAgreesWithinTheBound()
    {
        Looping.Sample(
            static s =>
            {
                (IrProcedure procedure, IrInputs input) = s;
                IrProcedure unrolled = IrUnroller.Unroll(procedure, 3);
                Assert.Empty(IrValidator.Validate(unrolled));
                Assert.Empty(IrLoopAnalysis.Of(unrolled).Loops);
                IrRun run = IrGen.Run(unrolled, input);
                Assert.True(run.Outcome is IrInfeasible || run == IrGen.Run(procedure, input));
            },
            iter: 200,
            print: static s => IrText.Dump(s.Procedure));
    }

    [Fact]
    public void UnrollingInPlaceAndPeelingValidateAndAgreeEverywhere()
    {
        Looping.Sample(
            static s =>
            {
                (IrProcedure procedure, IrInputs input) = s;
                IrRun expected = IrGen.Run(procedure, input);
                foreach (IrLoop loop in IrLoopAnalysis.Of(procedure).Loops)
                {
                    foreach ((IrProcedure copied, ImmutableArray<IrBlockId> _) in new[] { IrUnroller.UnrollInPlace(procedure, loop.Header, 3), IrUnroller.Peel(procedure, loop.Header, 3) })
                    {
                        Assert.Empty(IrValidator.Validate(copied));
                        Assert.Equal(expected, IrGen.Run(copied, input));
                    }
                }
            },
            iter: 200,
            print: static s => IrText.Dump(s.Procedure));
    }

    [Fact]
    public void EverySegmentValidatesAndIsAcyclic()
    {
        Looping.Sample(
            static s =>
            {
                IrProcedure procedure = s.Procedure;
                ImmutableArray<(IrBlockId Header, ImmutableArray<IrVar> State)> cuts =
                    [.. IrLoopAnalysis.Of(procedure).Loops.Select(l => (l.Header, IrFragmenter.State(procedure, l.Header)))];
                foreach (IrBlockId? start in cuts.Select(static c => (IrBlockId?)c.Header).Prepend(element: null))
                {
                    IrProcedure segment = IrFragmenter.Segment(procedure, start, cuts);
                    Assert.Empty(IrValidator.Validate(segment));
                    Assert.Empty(IrLoopAnalysis.Of(segment).Loops);
                }
            },
            iter: 200,
            print: static s => IrText.Dump(s.Procedure));
    }
}
