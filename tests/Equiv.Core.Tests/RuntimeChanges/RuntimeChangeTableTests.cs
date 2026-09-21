using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.RuntimeChanges;

using Xunit;

namespace Equiv.Core.Tests.RuntimeChanges;

/// <summary>The runtime-changes table (ticket M2-006, VERIFICATION-MODEL.md section 3; ADR 0008).</summary>
public sealed class RuntimeChangeTableTests
{
    [Fact]
    public void Table_LoadsAndValidatesRows()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.NotEmpty(table.Rows);
        Assert.Same(table, RuntimeChangeTable.Load());
        Assert.All(table.Rows, static row => Assert.StartsWith("https://learn.microsoft.com/", row.Url.OriginalString, StringComparison.Ordinal));
        Assert.All(table.Rows, static row => Assert.True(RuntimeChangeTable.IsIdentityPrefix(row.Member), $"'{row.Member}' does not parse as an identity prefix"));
    }

    [Theory]
    [InlineData("System.String::IndexOf(char)")]
    [InlineData("System.String::LastIndexOf(string,int32)")]
    [InlineData("System.String::StartsWith(string)")]
    [InlineData("System.String::EndsWith(string)")]
    [InlineData("System.String::Compare(string,string)")]
    [InlineData("System.String::CompareTo(string)")]
    [InlineData("System.String::GetHashCode()")]
    [InlineData("System.Globalization.CompareInfo::Compare(string,string)")]
    [InlineData("System.Text.Encoding::get_Default()")]
    [InlineData("System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::Serialize(System.IO.Stream,object)")]
    [InlineData("System.Double::ToString()")]
    [InlineData("System.Single::ToString()")]
    [InlineData("System.Double::Parse(string)")]
    public void Table_MatchesByPrefix(string identityValue)
    {
        Assert.True(RuntimeChangeTable.Load().TryMatch(new CallIdentity(identityValue), out RuntimeChange match));
        Assert.StartsWith(match.Member, identityValue, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.String::Concat(string,string)")]
    [InlineData("System.Math::Abs(int32)")]
    [InlineData("System.Int32::Parse(string)")]
    public void UnrelatedMembersDoNotMatch(string identityValue)
    {
        Assert.False(RuntimeChangeTable.Load().TryMatch(new CallIdentity(identityValue), out _));
    }

    [Fact]
    public void Table_SuppressionRemovesMatch()
    {
        CallIdentity identity = new("System.String::IndexOf(char)");
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        Assert.True(table.TryMatch(identity, out RuntimeChange unsuppressed));

        ImmutableArray<string> suppressed = [unsuppressed.Member];
        Assert.False(table.TryMatch(identity, suppressed, out _));
    }

    [Fact]
    public void SuppressingAnUnrelatedMemberDoesNotAffectOtherMatches()
    {
        CallIdentity identity = new("System.String::IndexOf(char)");
        ImmutableArray<string> suppressed = ["System.String::GetHashCode("];

        Assert.True(RuntimeChangeTable.Load().TryMatch(identity, suppressed, out _));
    }

    [Fact]
    public void NullIdentityThrows()
    {
        Assert.Throws<ArgumentNullException>(static () => RuntimeChangeTable.Load().TryMatch(null!, out _));
    }

    [Theory]
    [InlineData("NoSeparator")]
    [InlineData("")]
    [InlineData("::NoType")]
    [InlineData("Too::Many::Separators")]
    public void InvalidPrefixesAreRejected(string member)
    {
        Assert.False(RuntimeChangeTable.IsIdentityPrefix(member));
    }
}
