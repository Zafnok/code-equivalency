using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Matching;

using Xunit;

namespace Equiv.Core.Tests.Matching;

public sealed class ProcedureIdentityNormalizerTests
{
    [Fact]
    public void MemberWithNoRenamesUsesTheRawNamespaceAndType()
    {
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Ns", "Type", "Method", 0, ["int32", "bool"], RenameMap.Empty);
        Assert.Equal("Ns.Type::Method(int32,bool)", identity.Value);
    }

    [Fact]
    public void MemberWithNoParametersHasEmptyParens()
    {
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Ns", "Type", "Method", 0, [], RenameMap.Empty);
        Assert.Equal("Ns.Type::Method()", identity.Value);
    }

    [Fact]
    public void MemberWithEmptyNamespaceOmitsTheLeadingDot()
    {
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member(string.Empty, "Type", "Method", 0, [], RenameMap.Empty);
        Assert.Equal("Type::Method()", identity.Value);
    }

    [Fact]
    public void GenericArityIsAppendedWhenPositive()
    {
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Ns", "Type", "Method", 2, ["T"], RenameMap.Empty);
        Assert.Equal("Ns.Type::Method`2(T)", identity.Value);
    }

    [Fact]
    public void NamespaceRenameAppliesWhenNoTypeRenameMatches()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("Old.Ns", "New.Ns"), []);
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Old.Ns", "Type", "Method", 0, [], renames);
        Assert.Equal("New.Ns.Type::Method()", identity.Value);
    }

    [Fact]
    public void TypeRenameTakesPrecedenceOverNamespaceRename()
    {
        RenameMap renames = new(
            ImmutableDictionary<string, string>.Empty.Add("Old.Ns", "IgnoredNamespaceRename"),
            ImmutableDictionary<string, string>.Empty.Add("Old.Ns.Type", "New.Ns.RenamedType"));
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Old.Ns", "Type", "Method", 0, [], renames);
        Assert.Equal("New.Ns.RenamedType::Method()", identity.Value);
    }

    [Fact]
    public void NamespaceRenameToEmptyOmitsTheLeadingDot()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("Old.Ns", string.Empty), []);
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Old.Ns", "Type", "Method", 0, [], renames);
        Assert.Equal("Type::Method()", identity.Value);
    }

    [Fact]
    public void ParameterTypesAreRenamedTheSameWayAsTheDeclaringType()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("Old.Ns", "New.Ns"), []);
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Old.Ns", "Svc", "Process", 0, ["Old.Ns.Order", "int32"], renames);
        Assert.Equal("New.Ns.Svc::Process(New.Ns.Order,int32)", identity.Value);
    }

    [Fact]
    public void ParameterTypeRenameTakesPrecedenceOverNamespaceRename()
    {
        RenameMap renames = new(
            ImmutableDictionary<string, string>.Empty.Add("Old.Ns", "New.Ns"),
            ImmutableDictionary<string, string>.Empty.Add("Old.Ns.Order", "New.Ns.PurchaseOrder"));
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Member("Old.Ns", "Svc", "Process", 0, ["Old.Ns.Order"], renames);
        Assert.Equal("New.Ns.Svc::Process(New.Ns.PurchaseOrder)", identity.Value);
    }

    [Fact]
    public void RenamedOldSideParametersMatchTheModernSideIdentity()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("Old.Ns", "New.Ns"), []);
        ProcedureIdentity legacy = ProcedureIdentityNormalizer.Member("Old.Ns", "Svc", "Process", 0, ["Old.Ns.Order"], renames);
        ProcedureIdentity modern = ProcedureIdentityNormalizer.Member("New.Ns", "Svc", "Process", 0, ["New.Ns.Order"], RenameMap.Empty);
        Assert.Equal(modern, legacy);
    }

    [Fact]
    public void RenamedOldSideMatchesTheModernSideIdentity()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("System.Web.Http", "Microsoft.AspNetCore.Mvc"), []);
        ProcedureIdentity legacy = ProcedureIdentityNormalizer.Member("System.Web.Http", "OrdersController", "Get", 0, ["int32"], renames);
        ProcedureIdentity modern = ProcedureIdentityNormalizer.Member("Microsoft.AspNetCore.Mvc", "OrdersController", "Get", 0, ["int32"], RenameMap.Empty);
        Assert.Equal(modern, legacy);
    }

    [Fact]
    public void EndpointJoinsTheUppercasedVerbAndRoute()
    {
        ProcedureIdentity identity = ProcedureIdentityNormalizer.Endpoint("get", "/api/orders/{id}");
        Assert.Equal("GET /api/orders/{id}", identity.Value);
    }
}
