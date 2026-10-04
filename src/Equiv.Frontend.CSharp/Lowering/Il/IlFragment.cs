using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;

using Equiv.Core;
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
/// complete, or calls or takes the pointer of a member that does not resolve or whose behaviour changed between the pair's
/// runtimes (M3-015's rule, inside the interval both sides are given; ADR 0040, ticket P2-055). The text
/// hashed is its ILAst with every member and type spelled in full and every local numbered by first appearance. It gets
/// none either when that text names a method or a type the compiler generated (a lambda, a local function, a closure
/// class, a state machine, an anonymous type): the name is an ordinal and the code it names is elsewhere, so two
/// fragments with one text can do different things (ticket P2-079).
/// </summary>
internal sealed class IlFragment(IlSymbols symbols, Compilation compilation, RuntimeInterval interval)
{
    private const InstructionFlags Escapes =
        InstructionFlags.MayWriteLocals | InstructionFlags.MayBranch | InstructionFlags.EndPointUnreachable | InstructionFlags.ControlFlow;

    /// <summary><paramref name="instruction"/>'s fragment, or null when it gets no fingerprint.</summary>
    public Fragment? Of(ILInstruction instruction, Func<ILVariable, bool> readable)
    {
        ImmutableArray<ILInstruction> tree = [.. instruction.Descendants];
        if (instruction.HasFlag(Escapes)
            || tree.Any(static i => i is LdLoca)
            || !tree.OfType<LdLoc>().All(l => readable(l.Variable)))
        {
            return null;
        }

        Output text = new();
        text.Write("il:");
        instruction.WriteTo(text, new ILAstWritingOptions());
        return text.NamesGeneratedCode || !tree.All(Stable)
            ? null
            : new Fragment(
                System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))),
                [.. tree.OfType<LdLoc>().Select(static l => l.Variable).Distinct()]);
    }

    /// <summary>
    /// Whether the method a call or a function pointer in a fragment names resolves, and to a member no runtime change
    /// inside the interval names.
    /// </summary>
    private bool Stable(ILInstruction instruction) => instruction switch
    {
        CallInstruction call => Stable(call.Method),
        LdFtn function => Stable(function.Method),
        LdVirtFtn function => Stable(function.Method),
        _ => true,
    };

    private bool Stable(IMethod named) =>
        symbols.Method(named) is { } method && !CallIdentityFactory.Of(method, compilation, RenameMap.Empty, [], interval).RuntimeChanged;

    /// <summary>A fragment's fingerprint and the locals it reads, in order.</summary>
    internal sealed record Fragment(string Text, ImmutableArray<ILVariable> Reads);

    /// <summary>
    /// ILAst's text with each member as its declaring type and documentation ID, each type as its reflection name, and each
    /// local as <c>v</c> and its number, so two fragments share a text exactly when they do the same thing to the same reads,
    /// unless it <see cref="NamesGeneratedCode"/>.
    /// </summary>
    internal sealed class Output : ITextOutput
    {
        private readonly StringBuilder text = new();
        private readonly Dictionary<object, int> locals = new(ReferenceEqualityComparer.Instance);

        public string IndentationString { get; set; } = "\t";

        /// <summary>
        /// Whether a type written is one the compiler generated, or a method written is: C# has no name with a
        /// <c>&lt;</c>, so a reflection name that holds one, in itself, a type it is nested in or a type argument, is
        /// generated, and so is a method whose own name starts with one. A field's own name is not looked at: an
        /// auto-property's backing field is named from its property, not by an ordinal.
        /// </summary>
        public bool NamesGeneratedCode { get; private set; }

        public void Indent() => text.Append('{');

        public void Unindent() => text.Append('}');

        public void Write(char ch) => text.Append(ch);

        public void Write(string text) => this.text.Append(text);

        public void WriteLine() => text.Append('\n');

        public void WriteReference(OpCodeInfo opCode, bool omitSuffix = false) => text.Append(opCode.Name);

        public void WriteReference(MetadataFile metadata, Handle handle, string text, string protocol = "decompile", bool isDefinition = false) => this.text.Append(text);

        public void WriteReference(IType type, string text, bool isDefinition = false) => Type(type);

        public void WriteReference(IMember member, string text, bool isDefinition = false)
        {
            // A member of a reference that did not load has no metadata, and so no documentation ID, only its name.
            Type(member.DeclaringType);
            this.text.Append("::").Append(member.MetadataToken.IsNil ? member.Name : IdStringProvider.GetIdString(member.MemberDefinition));
            NamesGeneratedCode |= member is IMethod && member.Name.StartsWith('<');
            if (member is IMethod { TypeArguments.Count: > 0 } method)
            {
                this.text.Append('<').AppendJoin(',', method.TypeArguments.Select(Name)).Append('>');
            }
        }

        private void Type(IType type) => text.Append(Name(type));

        private string Name(IType type)
        {
            NamesGeneratedCode |= type.ReflectionName.Contains('<', StringComparison.Ordinal);
            return type.ReflectionName;
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
