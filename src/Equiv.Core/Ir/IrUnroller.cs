using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Equiv.Core.Ir;

/// <summary>
/// Loop copying for the loop ladder (VERIFICATION-MODEL.md section 5.1; ticket M3-002). <see cref="Unroll"/> is
/// rung 1: it inlines self-calls <c>k</c> deep and clones every loop <c>k</c> times, innermost first, so the
/// result is acyclic and runs exactly as the procedure does on every input that stays within the bound; an input
/// that would go further reaches an <see cref="IrUnreachable"/> instead. <see cref="UnrollInPlace"/> and
/// <see cref="Peel"/> keep the loop and the semantics, for k-induction. Copy <c>c</c> of a variable is named
/// <c>name$c</c> (a name already taken gets more <c>$</c>), since IR names cannot contain <c>@</c>; a variable
/// defined in a loop and read after it is merged by phis where copies meet.
/// </summary>
public static class IrUnroller
{
    /// <summary>What the back edges of the last copy of a loop jump to.</summary>
    private enum IrLastCopy
    {
        /// <summary>A new block ending in <see cref="IrUnreachable"/> (rung 1).</summary>
        Unreachable,

        /// <summary>The first copy's header: the loop stays, with a body of every copy.</summary>
        First,

        /// <summary>Its own header: the earlier copies are peeled off in front of the loop.</summary>
        Self,
    }

    /// <summary>
    /// Rung 1's acyclic under-approximation: blocks no input reaches are dropped, self-calls are inlined
    /// <paramref name="bound"/> deep (the call below that is unreachable), then every loop is cloned
    /// <paramref name="bound"/> times with the last copy's back edges unreachable.
    /// </summary>
    /// <exception cref="ArgumentException">The procedure is self-recursive and <see cref="InliningObstacle"/> is not null.</exception>
    public static IrProcedure Unroll(IrProcedure procedure, int bound)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);
        if (InliningObstacle(procedure) is { } obstacle)
        {
            throw new ArgumentException($"{procedure.Identity.Value} cannot be inlined into itself: {obstacle}.", nameof(procedure));
        }

        RequireReducible(procedure);

        IrProcedure current = Inline(Prune(procedure), bound);
        while (IrLoopAnalysis.Of(current).Loops is { IsEmpty: false } loops)
        {
            IrLoop innermost = loops.First(l => !loops.Any(inner => inner.Parent == l.Header));
            current = Copy(current, innermost, bound, IrLastCopy.Unreachable).Procedure;
        }

        return Checked(procedure, current);
    }

    /// <summary>
    /// Clones the loop headed by <paramref name="header"/> <paramref name="copies"/> times in place: each copy's back
    /// edges enter the next copy and the last copy's return to the first, so the procedure computes the same thing
    /// with a loop whose body is <paramref name="copies"/> iterations. Returns the copies' headers, first to last.
    /// </summary>
    public static (IrProcedure Procedure, ImmutableArray<IrBlockId> Headers) UnrollInPlace(IrProcedure procedure, IrBlockId header, int copies) =>
        Transform(procedure, header, copies, IrLastCopy.First);

    /// <summary>
    /// Peels <paramref name="copies"/> - 1 iterations off the loop headed by <paramref name="header"/>: the first
    /// copies run once each, in order, before the last copy, which is the remaining loop. The semantics are unchanged.
    /// Returns the copies' headers, first to last; the last is the remaining loop's header.
    /// </summary>
    public static (IrProcedure Procedure, ImmutableArray<IrBlockId> Headers) Peel(IrProcedure procedure, IrBlockId header, int copies) =>
        Transform(procedure, header, copies, IrLastCopy.Self);

    /// <summary>
    /// Why the self-calls of <paramref name="procedure"/> cannot be inlined, or null when they can (or there are
    /// none). Inlining binds the callee's source-language parameters to the call's arguments and its receiver input
    /// <c>this</c> to the receiver argument; every other synthesised input is the caller's own, which is exact only
    /// while no heap map or by-ref parameter changes and no input is keyed by an array variable (ADR 0015).
    /// </summary>
    public static string? InliningObstacle(IrProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(procedure);
        if (!analysis.IsSelfRecursive)
        {
            return null;
        }

        int sourceParameters = procedure.Parameters.Count(static p => !IrParameterNames.IsSynthesised(p.Var.Name));
        bool hasThis = procedure.Parameters.Any(static p => string.Equals(p.Var.Name, "this", StringComparison.Ordinal));
        IEnumerable<IrInstruction> instructions = analysis.ReversePostorder.SelectMany(static b => b.Instructions);
        return procedure switch
        {
            _ when procedure.Parameters.Any(static p => p.Kind != IrParameterKind.In) => "it has a by-ref parameter",
            _ when procedure.Parameters.Any(static p => p.Var.Name.StartsWith("array.", StringComparison.Ordinal) || p.Var.Name.StartsWith("length.", StringComparison.Ordinal)) =>
                "an input is keyed by an array variable",
            _ when instructions.Any(static i => i is IrMapWrite) => "it writes the heap",
            _ when SelfCalls(procedure, instructions).Any(c => c.Threw is null || Receivers(c, sourceParameters, hasThis) < 0) =>
                "a self-call has no threw flag or its arguments do not match the parameters",
            _ => null,
        };
    }

    /// <summary>Renames every variable <paramref name="instruction"/> defines or reads (a phi's predecessors are the caller's).</summary>
    internal static IrInstruction Rewrite(IrInstruction instruction, Func<IrVar, IrVar> var) => instruction switch
    {
        IrConst constant => constant with { Target = var(constant.Target) },
        IrBinary binary => binary with { Target = var(binary.Target), A = var(binary.A), B = var(binary.B) },
        IrOverflows overflows => overflows with { Target = var(overflows.Target), A = var(overflows.A), B = var(overflows.B) },
        IrUnary unary => unary with { Target = var(unary.Target), A = var(unary.A) },
        IrPhi phi => phi with { Target = var(phi.Target), Incoming = [.. phi.Incoming.Select(i => (i.From, var(i.Value)))] },
        IrCall call => call with { Target = Optional(call.Target, var), Threw = Optional(call.Threw, var), Args = [.. call.Args.Select(var)] },
        IrMapRead read => read with { Target = var(read.Target), Map = var(read.Map), Key = var(read.Key) },
        IrMapWrite write => write with { Target = var(write.Target), Map = var(write.Map), Key = var(write.Key), Value = var(write.Value) },
        _ => (IrOpaque)instruction with { Target = Optional(((IrOpaque)instruction).Target, var) },
    };

    /// <summary>Renames the variables <paramref name="terminator"/> reads (not the parameters its outs name) and retargets its edges.</summary>
    internal static IrTerminator Rewrite(IrTerminator terminator, Func<IrVar, IrVar> var, Func<IrBlockId, IrBlockId> block) => terminator switch
    {
        IrGoto jump => new IrGoto(block(jump.Target)),
        IrBranch branch => new IrBranch(var(branch.Cond), block(branch.Then), block(branch.Else)),
        IrSwitch choice => choice with
        {
            Scrutinee = var(choice.Scrutinee),
            Cases = [.. choice.Cases.Select(c => (c.Value, block(c.Target)))],
            Default = block(choice.Default),
        },
        IrReturn exit => exit with { Value = Optional(exit.Value, var), Outs = [.. exit.Outs.Select(o => o with { Final = var(o.Final) })] },
        IrThrow exit => exit with { Outs = [.. exit.Outs.Select(o => o with { Final = var(o.Final) })] },
        _ => terminator,
    };

    /// <summary>Drops the blocks the entry does not reach, and the phi operands of edges that no longer exist.</summary>
    internal static IrProcedure Prune(IrProcedure procedure)
    {
        HashSet<IrBlockId> reached = [.. IrLoopAnalysis.Of(procedure).ReversePostorder.Select(static b => b.Id)];
        IrBlock[] kept = [.. procedure.Blocks.Where(b => reached.Contains(b.Id))];
        ILookup<IrBlockId, IrBlockId> predecessors = kept
            .SelectMany(static b => b.Terminator.Successors().Distinct().Select(s => (To: s, From: b.Id)))
            .ToLookup(static e => e.To, static e => e.From);
        return procedure with
        {
            Blocks =
            [
                .. kept.Select(b => b with
                {
                    Instructions = [.. b.Instructions.Select(i => i is IrPhi phi ? phi with { Incoming = [.. phi.Incoming.Where(e => predecessors[b.Id].Contains(e.From))] } : i)],
                }),
            ],
        };
    }

    /// <summary>A value of <paramref name="type"/> for a result no caller reads (a call that threw).</summary>
    internal static IrValue Default(IrType type) => type switch
    {
        IrBool => new IrBoolValue(Value: false),
        IrBitVec bitVec => new IrBitVecValue(bitVec.Width, 0),
        IrSort sort => new IrSortValue(sort.Name, 0),
        _ => new IrMapValue((IrMap)type, Default(((IrMap)type).Value), []),
    };

    /// <summary>In debug builds, asserts that a transformation of valid IR produced valid IR (ticket M3-002 pitfall).</summary>
    [Conditional("DEBUG")]
    internal static void Validate(IrProcedure input, IrProcedure output) =>
        Debug.Assert(!IrValidator.Validate(input).IsEmpty || IrValidator.Validate(output).IsEmpty, "A loop transformation of valid IR produced invalid IR.");

    private static IrProcedure Checked(IrProcedure input, IrProcedure output)
    {
        Validate(input, output);
        return output;
    }

    /// <summary>A natural loop of an irreducible CFG may contain the entry; copying it would change the semantics.</summary>
    private static void RequireReducible(IrProcedure procedure)
    {
        if (!IrLoopAnalysis.Of(procedure).IsReducible)
        {
            throw new ArgumentException($"{procedure.Identity.Value} has irreducible control flow, which is never unrolled.", nameof(procedure));
        }
    }

    private static IrVar? Optional(IrVar? var, Func<IrVar, IrVar> map) => var is null ? null : map(var);

    private static IEnumerable<IrCall> SelfCalls(IrProcedure procedure, IEnumerable<IrInstruction> instructions) =>
        instructions.OfType<IrCall>().Where(c => string.Equals(c.Callee.Value, procedure.Identity.Value, StringComparison.Ordinal));

    /// <summary>How many leading arguments of a self-call are the receiver (0 or 1), or -1 when the shape does not fit.</summary>
    private static int Receivers(IrCall call, int sourceParameters, bool hasThis) =>
        (call.Args.Length - sourceParameters) switch
        {
            1 => 1,
            0 when !hasThis => 0,
            _ => -1,
        };

    private static (IrProcedure Procedure, ImmutableArray<IrBlockId> Headers) Transform(IrProcedure procedure, IrBlockId header, int copies, IrLastCopy last)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfLessThan(copies, 1);
        RequireReducible(procedure);
        IrProcedure pruned = Prune(procedure);
        IrLoop loop = IrLoopAnalysis.Of(pruned).Loops.FirstOrDefault(l => l.Header == header)
            ?? throw new ArgumentException($"{IrText.Block(header)} is not a loop header of {procedure.Identity.Value}.", nameof(header));
        (IrProcedure result, ImmutableArray<IrBlockId> headers) = Copy(pruned, loop, copies, last);
        return (Checked(procedure, result), headers);
    }

    /// <summary>Inlines every self-call <paramref name="bound"/> deep; a self-call below that ends its block unreachable.</summary>
    private static IrProcedure Inline(IrProcedure procedure, int bound)
    {
        IrEditor editor = new(procedure);
        Dictionary<IrBlockId, int> depth = procedure.Blocks.ToDictionary(static b => b.Id, static _ => 0);
        int instance = 0;
        while (editor.FindSelfCall() is (IrBlock block, int index))
        {
            if (depth[block.Id] == bound)
            {
                editor.Replace(block with { Instructions = block.Instructions[..index], Terminator = new IrUnreachable() });
                continue;
            }

            instance++;
            (ImmutableArray<IrBlockId> inlined, IrBlockId after) = editor.InlineCall(procedure, block, index, $"$r{instance.ToString(CultureInfo.InvariantCulture)}");
            foreach (IrBlockId id in inlined)
            {
                depth[id] = depth[block.Id] + 1;
            }

            depth[after] = depth[block.Id];
        }

        return Prune(editor.Build());
    }

    private static (IrProcedure Procedure, ImmutableArray<IrBlockId> Headers) Copy(IrProcedure procedure, IrLoop loop, int copies, IrLastCopy last)
    {
        IrEditor editor = new(procedure);
        ImmutableArray<IrBlockId> headers = editor.CopyLoop(loop, copies, last);
        return (Prune(editor.Build()), headers);
    }

    /// <summary>A procedure under edit: its blocks in order, the names in use, and the next free block id.</summary>
    private sealed class IrEditor(IrProcedure procedure)
    {
        private readonly IrProcedure procedure = procedure;
        private readonly List<IrBlock> blocks = [.. procedure.Blocks];
        private readonly HashSet<string> names = new(
            procedure.Parameters.Select(static p => p.Var.Name)
                .Concat(procedure.Blocks.SelectMany(static b => b.Instructions).SelectMany(static i => i.Definitions()).Select(static v => v.Name)),
            StringComparer.Ordinal);

        private int nextBlock = procedure.Blocks.Select(static b => b.Id.Value).DefaultIfEmpty(-1).Max() + 1;

        public IReadOnlyList<IrBlock> Blocks => blocks;

        /// <summary>Replaces every phi operand of <paramref name="block"/> through <paramref name="incoming"/>.</summary>
        public static IrBlock MapPhis(IrBlock block, Func<(IrBlockId From, IrVar Value), IEnumerable<(IrBlockId, IrVar)>> incoming) =>
            block with { Instructions = [.. block.Instructions.Select(i => i is IrPhi phi ? phi with { Incoming = [.. phi.Incoming.SelectMany(incoming)] } : i)] };

        public IrProcedure Build() => procedure with { Blocks = [.. blocks] };

        public IrBlock Get(IrBlockId id) => blocks.First(b => b.Id == id);

        public void Replace(IrBlock block) => blocks[blocks.FindIndex(b => b.Id == block.Id)] = block;

        public void Add(IrBlock block) => blocks.Add(block);

        public IrBlockId NewBlock() => new(nextBlock++);

        public IrVar Fresh(IrVar var, string suffix)
        {
            StringBuilder name = new(var.Name + suffix);
            while (!names.Add(name.ToString()))
            {
                name.Append('$');
            }

            return var with { Name = name.ToString() };
        }

        public (IrBlock Block, int Index)? FindSelfCall() =>
            blocks
                .SelectMany(b => b.Instructions.Select((instruction, index) => (Block: b, Instruction: instruction, Index: index)))
                .Where(s => s.Instruction is IrCall call && string.Equals(call.Callee.Value, procedure.Identity.Value, StringComparison.Ordinal))
                .Select(static s => ((IrBlock, int)?)(s.Block, s.Index))
                .FirstOrDefault();

        /// <summary>
        /// Replaces the self-call at <paramref name="index"/> of <paramref name="block"/> with a copy of
        /// <paramref name="callee"/>'s body. The block keeps what precedes the call and jumps to the copy's entry; a
        /// new block receives every exit of the copy, merges the call's result and <c>threw</c> flag with phis, and
        /// continues with the rest of the block. Returns the copy's blocks and the new block.
        /// </summary>
        public (ImmutableArray<IrBlockId> Inlined, IrBlockId After) InlineCall(IrProcedure callee, IrBlock block, int index, string suffix)
        {
            IrCall call = (IrCall)block.Instructions[index];
            IrBlockId join = NewBlock();
            Dictionary<string, IrVar> bound = Bindings(callee, call, suffix);
            IrVar Var(IrVar var) => bound.GetValueOrDefault(var.Name, var);
            Dictionary<IrBlockId, IrBlockId> ids = callee.Blocks.ToDictionary(static b => b.Id, _ => NewBlock());
            List<(IrBlockId From, IrVar? Result, IrVar Threw)> exits = [];
            foreach (IrBlock original in callee.Blocks)
            {
                IrBlock copy = MapPhis(original with { Id = ids[original.Id], Instructions = [.. original.Instructions.Select(i => Rewrite(i, Var))] }, e => [(ids[e.From], e.Value)]);
                switch (original.Terminator)
                {
                    case IrReturn returned:
                        exits.Add(InlineExit(copy, call.Target, returned, Var, suffix, join));
                        break;
                    case IrThrow:
                        exits.Add(InlineExit(copy, call.Target, returned: null, Var, suffix, join));
                        break;
                    default:
                        Add(copy with { Terminator = Rewrite(original.Terminator, Var, b => ids[b]) });
                        break;
                }
            }

            IrInstruction[] merges = call.Target is null
                ? [new IrPhi(call.Threw!, [.. exits.Select(static e => (e.From, e.Threw))])]
                : [new IrPhi(call.Target, [.. exits.Select(static e => (e.From, e.Result!))]), new IrPhi(call.Threw!, [.. exits.Select(static e => (e.From, e.Threw))])];
            Replace(block with { Instructions = block.Instructions[..index], Terminator = new IrGoto(ids[callee.Entry]) });
            Add(new IrBlock(join, [.. merges, .. block.Instructions[(index + 1)..]], block.Terminator));
            foreach (IrBlockId successor in block.Terminator.Successors().Distinct())
            {
                Replace(MapPhis(Get(successor), e => [(e.From == block.Id ? join : e.From, e.Value)]));
            }

            return ([.. ids.Values], join);
        }

        /// <summary>
        /// Adds <paramref name="copy"/>, an exit of an inlined body (a return when <paramref name="returned"/> is set,
        /// else a throw), ending in a jump to <paramref name="join"/> after setting its <c>threw</c> flag, plus a default
        /// result for a throw into a <paramref name="target"/>. Returns the exit's operands for the join's phis.
        /// </summary>
        private (IrBlockId From, IrVar? Result, IrVar Threw) InlineExit(IrBlock copy, IrVar? target, IrReturn? returned, Func<IrVar, IrVar> map, string suffix, IrBlockId join)
        {
            bool threw = returned is null;
            IrVar flag = Fresh(new IrVar("threw", new IrBool()), suffix);
            IrVar? result = (target, returned) switch
            {
                ({ } t, null) => Fresh(t, suffix),
                ({ }, { } r) => map(r.Value!),
                _ => null,
            };
            IrInstruction[] tail = threw && result is not null
                ? [new IrConst(flag, new IrBoolValue(threw)), new IrConst(result, Default(result.Type))]
                : [new IrConst(flag, new IrBoolValue(threw))];
            Add(copy with { Instructions = [.. copy.Instructions, .. tail], Terminator = new IrGoto(join) });
            return (copy.Id, result, flag);
        }

        /// <summary>
        /// Clones <paramref name="loop"/> <paramref name="copies"/> times. Copy 1 keeps the original blocks and names.
        /// A back edge of copy c enters copy c + 1; the last copy's go where <paramref name="last"/> says. A block
        /// outside the loop that an exit edge enters gets one phi operand per copy, and a read after the loop of a
        /// variable the loop defines is repaired to the copy that reaches it.
        /// </summary>
        public ImmutableArray<IrBlockId> CopyLoop(IrLoop loop, int copies, IrLastCopy last)
        {
            IrLoopCopies clones = new(this, loop, copies, last);
            List<IrBlock> built = [.. Enumerable.Range(1, copies).SelectMany(c => loop.Blocks.Select(b => clones.Clone(c, Get(b))))];
            foreach (IrBlock clone in built)
            {
                if (clones.IsOriginal(clone.Id))
                {
                    Replace(clone);
                }
                else
                {
                    Add(clone);
                }
            }

            if (clones.Unreachable is { } unreachable)
            {
                Add(new IrBlock(unreachable, [], new IrUnreachable()));
            }

            foreach ((IrBlockId from, IrBlockId to) in loop.Exits)
            {
                Replace(MapPhis(Get(to), e => e.From == from ? clones.Exits(from, e.Value) : [e]));
            }

            new IrSsaRepair(this, [.. built.Select(static b => b.Id)], clones.Versions()).Run();
            return clones.Headers;
        }

        /// <summary>The callee's source-language parameters bound to the call's arguments, <c>this</c> to its receiver, and every variable it defines renamed.</summary>
        private Dictionary<string, IrVar> Bindings(IrProcedure callee, IrCall call, string suffix)
        {
            IrParameter[] source = [.. callee.Parameters.Where(static p => !IrParameterNames.IsSynthesised(p.Var.Name))];
            int receivers = call.Args.Length - source.Length;
            Dictionary<string, IrVar> bound = source
                .Select((p, i) => (p.Var.Name, Arg: call.Args[receivers + i]))
                .Concat(call.Args.Take(receivers).Select(static a => (Name: "this", Arg: a)))
                .ToDictionary(static b => b.Name, static b => b.Arg, StringComparer.Ordinal);
            foreach (IrVar defined in callee.Blocks.SelectMany(static b => b.Instructions).SelectMany(static i => i.Definitions()))
            {
                bound[defined.Name] = Fresh(defined, suffix);
            }

            return bound;
        }
    }

    /// <summary>The block and variable names of every copy of one loop, and how each copy is wired.</summary>
    private sealed class IrLoopCopies
    {
        private readonly IrEditor editor;
        private readonly IrLoop loop;
        private readonly int copies;
        private readonly IrLastCopy last;
        private readonly HashSet<IrBlockId> body;
        private readonly List<IrVar> defined;
        private readonly Dictionary<IrBlockId, IrBlockId>[] ids;
        private readonly Dictionary<string, IrVar>[] vars;

        public IrLoopCopies(IrEditor editor, IrLoop loop, int copies, IrLastCopy last)
        {
            this.editor = editor;
            this.loop = loop;
            this.copies = copies;
            this.last = last;
            body = [.. loop.Blocks];
            defined = [.. loop.Blocks.SelectMany(b => editor.Get(b).Instructions).SelectMany(static i => i.Definitions())];
            ids = new Dictionary<IrBlockId, IrBlockId>[copies + 1];
            vars = new Dictionary<string, IrVar>[copies + 1];
            for (int c = 1; c <= copies; c++)
            {
                string suffix = "$" + c.ToString(CultureInfo.InvariantCulture);
                bool original = c == 1;
                ids[c] = loop.Blocks.ToDictionary(static b => b, b => original ? b : editor.NewBlock());
                vars[c] = defined.ToDictionary(static v => v.Name, v => original ? v : editor.Fresh(v, suffix), StringComparer.Ordinal);
            }

            Unreachable = last == IrLastCopy.Unreachable ? editor.NewBlock() : null;
        }

        /// <summary>The block the last copy's back edges enter when <see cref="IrLastCopy.Unreachable"/>.</summary>
        public IrBlockId? Unreachable { get; }

        public ImmutableArray<IrBlockId> Headers => [.. Enumerable.Range(1, copies).Select(c => ids[c][loop.Header])];

        public bool IsOriginal(IrBlockId block) => body.Contains(block);

        /// <summary>Copy <paramref name="c"/> of <paramref name="original"/>, with its phis and edges wired to the right copies.</summary>
        public IrBlock Clone(int c, IrBlock original)
        {
            ImmutableArray<IrInstruction> instructions =
            [
                .. original.Instructions.Select(i => i switch
                {
                    IrPhi phi when original.Id == loop.Header => new IrPhi(Var(c, phi.Target), [.. HeaderIncoming(phi, c)]),
                    IrPhi phi => new IrPhi(Var(c, phi.Target), [.. phi.Incoming.Select(e => (ids[c][e.From], Var(c, e.Value)))]),
                    _ => Rewrite(i, v => Var(c, v)),
                }),
            ];
            IrTerminator terminator = Rewrite(original.Terminator, v => Var(c, v), t => t == loop.Header ? BackEdge(c) : ids[c].GetValueOrDefault(t, t));
            return new IrBlock(ids[c][original.Id], instructions, terminator);
        }

        /// <summary>One operand per copy for a phi operand <paramref name="value"/> that an exit edge from <paramref name="from"/> carries.</summary>
        public IEnumerable<(IrBlockId, IrVar)> Exits(IrBlockId from, IrVar value) =>
            Enumerable.Range(1, copies).Select(c => (ids[c][from], Var(c, value)));

        /// <summary>For each variable the loop defines, its version in each copy, keyed by the copy's block that defines it.</summary>
        public Dictionary<string, Dictionary<IrBlockId, IrVar>> Versions() =>
            defined.ToDictionary(
                static v => v.Name,
                v =>
                {
                    IrBlockId home = loop.Blocks.First(b => editor.Get(b).Instructions.Any(i => i.Definitions().Contains(v)));
                    return Enumerable.Range(1, copies).ToDictionary(c => ids[c][home], c => vars[c][v.Name]);
                },
                StringComparer.Ordinal);

        private IrVar Var(int c, IrVar var) => vars[c].GetValueOrDefault(var.Name, var);

        private IrBlockId BackEdge(int c) => c < copies ? ids[c + 1][loop.Header] : LastBackEdge;

        /// <summary>Where the last copy's back edges go: the unreachable block when there is one, else the header <c>last</c> names.</summary>
        private IrBlockId LastBackEdge => Unreachable ?? ids[last == IrLastCopy.First ? 1 : copies][loop.Header];

        /// <summary>
        /// A header phi's operands in copy <paramref name="c"/>: the edges from outside the loop in copy 1, the
        /// previous copy's latches after that, plus the last copy's latches where the last copy loops back to this one.
        /// </summary>
        private IEnumerable<(IrBlockId, IrVar)> HeaderIncoming(IrPhi phi, int c)
        {
            IEnumerable<(IrBlockId, IrVar)> entering = c == 1
                ? phi.Incoming.Where(e => !body.Contains(e.From))
                : Latches(phi, c - 1);
            bool closes = (last == IrLastCopy.First && c == 1) || (last == IrLastCopy.Self && c == copies);
            return closes ? entering.Concat(Latches(phi, copies)) : entering;
        }

        private IEnumerable<(IrBlockId, IrVar)> Latches(IrPhi phi, int c) =>
            phi.Incoming.Where(e => body.Contains(e.From)).Select(e => (ids[c][e.From], Var(c, e.Value)));
    }

    /// <summary>
    /// Braun et al., "Simple and Efficient Construction of Static Single Assignment Form" (2013), applied to the
    /// variables a copied loop defines: a read outside the copies takes the version that reaches it, with a phi
    /// wherever versions from several predecessors meet. New phis go after a block's existing phis, so a header
    /// copy's phis keep the positions of its original's.
    /// </summary>
    private sealed class IrSsaRepair(IrEditor editor, HashSet<IrBlockId> copied, Dictionary<string, Dictionary<IrBlockId, IrVar>> versions)
    {
        private readonly Dictionary<(string, IrBlockId), IrVar> entry = [];
        private readonly Dictionary<IrBlockId, List<IrPhi>> inserted = [];
        private readonly ILookup<IrBlockId, IrBlockId> predecessors = editor.Blocks
            .SelectMany(static b => b.Terminator.Successors().Distinct().Select(s => (To: s, From: b.Id)))
            .ToLookup(static e => e.To, static e => e.From);

        public void Run()
        {
            List<IrBlock> repaired = [.. editor.Blocks.Select(b => copied.Contains(b.Id) ? b : Repair(b))];
            foreach (IrBlock block in repaired)
            {
                editor.Replace(inserted.TryGetValue(block.Id, out List<IrPhi>? phis)
                    ? block with { Instructions = [.. block.Instructions.TakeWhile(static i => i is IrPhi), .. phis, .. block.Instructions.SkipWhile(static i => i is IrPhi)] }
                    : block);
            }
        }

        private IrBlock Repair(IrBlock block)
        {
            IrVar Read(IrVar var) => versions.ContainsKey(var.Name) ? AtEntry(var.Name, block.Id) : var;
            return block with
            {
                Instructions =
                [
                    .. block.Instructions.Select(i => i is IrPhi phi
                        ? phi with { Incoming = [.. phi.Incoming.Select(e => (e.From, versions.ContainsKey(e.Value.Name) && !copied.Contains(e.From) ? AtEnd(e.Value.Name, e.From) : e.Value))] }
                        : Rewrite(i, Read)),
                ],
                Terminator = Rewrite(block.Terminator, Read, static b => b),
            };
        }

        private IrVar AtEnd(string name, IrBlockId block) =>
            versions[name].TryGetValue(block, out IrVar? version) ? version : AtEntry(name, block);

        private IrVar AtEntry(string name, IrBlockId block)
        {
            if (entry.TryGetValue((name, block), out IrVar? known))
            {
                return known;
            }

            IrBlockId[] from = [.. predecessors[block]];
            if (from.Length == 1)
            {
                IrVar single = AtEnd(name, from[0]);
                entry[(name, block)] = single;
                return single;
            }

            IrVar merged = editor.Fresh(versions[name].Values.First(), "$phi");
            entry[(name, block)] = merged;
            IrPhi phi = new(merged, [.. from.Select(p => (p, AtEnd(name, p)))]);
            if (!inserted.TryGetValue(block, out List<IrPhi>? phis))
            {
                phis = [];
                inserted.Add(block, phis);
            }

            phis.Add(phi);
            return merged;
        }
    }
}
