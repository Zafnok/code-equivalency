using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;

using Equiv.Core.Configuration;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// The fingerprint of an ILAst instruction the IL lowering leaves opaque, and the locals it reads (ADR 0024 decision 2,
/// as <see cref="Fingerprinting.FragmentFingerprinter"/> gives one of an IOperation fragment; ticket P1-014). A fragment is a
/// function of what it reads, the heap and its position only when nothing else reaches into it, so it gets none when it
/// writes a local, takes a local's address, reads through a <c>ref</c> or a type the IR has no sort for, branches, cannot
/// complete, or calls a member that does not resolve or whose behaviour changed between runtimes (M3-015's rule). The text
/// hashed is its ILAst with every member and type spelled in full and every local numbered by first appearance.
/// </summary>
internal sealed class IlFragment(IlSymbols symbols, Compilation compilation)
{
    private const InstructionFlags Escapes =
        InstructionFlags.MayWriteLocals | InstructionFlags.MayBranch | InstructionFlags.EndPointUnreachable | InstructionFlags.ControlFlow;

    /// <summary><paramref name="instruction"/>'s fragment, or null when it gets no fingerprint.</summary>
    public Fragment? Of(ILInstruction instruction, Func<ILVariable, bool> readable)
    {
        ImmutableArray<ILInstruction> tree = [.. instruction.Descendants];
        if (instruction.HasFlag(Escapes)
            || tree.Any(static i => i is LdLoca)
            || !tree.OfType<LdLoc>().All(l => readable(l.Variable))
            || !tree.OfType<CallInstruction>().All(Stable))
        {
            return null;
        }

        Output text = new();
        text.Write("il:");
        instruction.WriteTo(text, new ILAstWritingOptions());
        return new Fragment(
            System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))),
            [.. tree.OfType<LdLoc>().Select(static l => l.Variable).Distinct()]);
    }

    /// <summary>Whether a call in a fragment resolves, and to a member no runtime change names.</summary>
    private bool Stable(CallInstruction call) =>
        symbols.Method(call.Method) is { } method && !CallIdentityFactory.Of(method, compilation, RenameMap.Empty, []).RuntimeChanged;

    /// <summary>A fragment's fingerprint and the locals it reads, in order.</summary>
    internal sealed record Fragment(string Text, ImmutableArray<ILVariable> Reads);

    /// <summary>
    /// ILAst's text with each member as its declaring type and documentation ID, each type as its reflection name, and each
    /// local as <c>v</c> and its number, so two fragments share a text exactly when they do the same thing to the same reads.
    /// </summary>
    internal sealed class Output : ITextOutput
    {
        private readonly StringBuilder text = new();
        private readonly Dictionary<object, int> locals = new(ReferenceEqualityComparer.Instance);

        public string IndentationString { get; set; } = "\t";

        public void Indent() => text.Append('{');

        public void Unindent() => text.Append('}');

        public void Write(char ch) => text.Append(ch);

        public void Write(string text) => this.text.Append(text);

        public void WriteLine() => text.Append('\n');

        public void WriteReference(OpCodeInfo opCode, bool omitSuffix = false) => text.Append(opCode.Name);

        public void WriteReference(MetadataFile metadata, Handle handle, string text, string protocol = "decompile", bool isDefinition = false) => this.text.Append(text);

        public void WriteReference(IType type, string text, bool isDefinition = false) => this.text.Append(type.ReflectionName);

        public void WriteReference(IMember member, string text, bool isDefinition = false)
        {
            this.text.Append(member.DeclaringType.ReflectionName).Append("::").Append(IdStringProvider.GetIdString(member.MemberDefinition));
            if (member is IMethod { TypeArguments.Count: > 0 } method)
            {
                this.text.Append('<').AppendJoin(',', method.TypeArguments.Select(static t => t.ReflectionName)).Append('>');
            }
        }

        public void WriteLocalReference(string text, object reference, bool isDefinition = false, bool isHoverOnly = false)
        {
            if (!locals.TryGetValue(reference, out int number))
            {
                number = locals.Count;
                locals[reference] = number;
            }

            this.text.Append('v').Append(number.ToString(CultureInfo.InvariantCulture));
        }

        public void MarkFoldStart(string collapsedText = "...", bool defaultCollapsed = false, bool isDefinition = false) => text.Append('[');

        public void MarkFoldEnd() => text.Append(']');

        public void MarkDefinitionStart() => text.Append('^');

        public override string ToString() => text.ToString();
    }
}
