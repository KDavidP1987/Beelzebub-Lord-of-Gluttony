using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Beelzebub.Logic;
using Xunit;

namespace Beelzebub.Tests;

public class ExclusionResolverTests
{
    static ExclusionGroupDef G(string name, int max, params int[] guids) => new() { Name = name, Max = max, Guids = new HashSet<int>(guids) };
    static LoadoutBinding L(string key, int guid) => new(key, guid);

    [Fact]
    public void PairLock_KeepsEarlierSlot()
    {
        var r = ExclusionResolver.Resolve(new[] { L("slot:1", 10), L("slot:2", 20), L("slot:3", 30) }, new[] { G("nocombo", 1, 10, 30) });
        Assert.True(r.IsKept("slot:1"));
        Assert.True(r.IsKept("slot:2"));
        var s = Assert.Single(r.Suppressed);
        Assert.Equal("slot:3", s.Key);
        Assert.Equal("nocombo", s.Primary);
    }

    [Fact]
    public void MaxN_AndDuplicatesCountOnce()
    {
        var r = ExclusionResolver.Resolve(new[] { L("slot:1", 1), L("slot:2", 1), L("slot:3", 2), L("slot:4", 3) }, new[] { G("two", 2, 1, 2, 3) });
        Assert.True(r.IsKept("slot:2"));   // duplicate of kept ability 1
        Assert.True(r.IsKept("slot:3"));
        Assert.Equal("slot:4", Assert.Single(r.Suppressed).Key);
    }

    [Fact]
    public void MultiGroup_AllRejectingGroupsReportedSorted()
    {
        var r = ExclusionResolver.Resolve(new[] { L("slot:1", 1), L("slot:2", 2), L("slot:3", 3) },
            new[] { G("zeta", 1, 1, 3), G("alpha", 1, 2, 3) });
        var s = Assert.Single(r.Suppressed);
        Assert.Equal(new[] { "alpha", "zeta" }, s.Groups);
    }

    [Fact]
    public void MustFitEveryGroup_NoPartialOccupancy()
    {
        // 3 is rejected by 'a' (full) — it must NOT then occupy 'b', so 4 still fits in 'b'.
        var r = ExclusionResolver.Resolve(new[] { L("slot:1", 1), L("slot:2", 3), L("slot:3", 4) },
            new[] { G("a", 1, 1, 3), G("b", 1, 3, 4) });
        Assert.True(r.IsKept("slot:3"));
        Assert.Equal("slot:2", Assert.Single(r.Suppressed).Key);
    }

    [Fact]
    public void Categories()
    {
        var g = new ExclusionGroupDef { Name = "onesummon", Max = 1 };
        g.Categories.Add("Summon");
        var r = ExclusionResolver.Resolve(new[] { L("slot:1", 1), L("slot:2", 2), L("hotkey:x", 3) }, new[] { g },
            guid => guid == 2 ? "Other" : "Summon");
        Assert.True(r.IsKept("slot:2"));
        Assert.Equal("hotkey:x", Assert.Single(r.Suppressed).Key);
    }

    [Fact]
    public void ProspectiveCheck()
    {
        var groups = new[] { G("g", 1, 5, 6) };
        Assert.NotNull(ExclusionResolver.Check(new[] { L("slot:1", 5), L("slot:4", 6) }, "slot:4", groups));
        Assert.Null(ExclusionResolver.Check(new[] { L("slot:1", 6) }, "slot:1", groups));
    }

    [Fact]
    public void EmptyGroupsKeepEverything()
    {
        var r = ExclusionResolver.Resolve(new[] { L("slot:1", 1), L("slot:2", 2) }, new List<ExclusionGroupDef>());
        Assert.Empty(r.Suppressed);
        Assert.Equal(2, r.KeptKeys.Count);
    }
}

public class RulesMergeTests
{
    static JsonObject J(string json) => (JsonObject)JsonNode.Parse(json);
    static int Resolve(string key) => key switch { "AB_A" or "111" => 111, "AB_B" => 222, "AB_C" => 333, _ => 0 };

    [Fact]
    public void AdoptsShipChange_WhenAdminUntouched()
    {
        var b = J("""{"AbilityMap":{"AB_A":{"Enabled":true,"DamageScale":1}}}""");
        var s = J("""{"AbilityMap":{"AB_A":{"Enabled":true,"DamageScale":1}}}""");
        var t = J("""{"AbilityMap":{"AB_A":{"Enabled":false,"DamageScale":1}}}""");
        var r = RulesMerge.Merge(b, s, t, Resolve);
        Assert.Equal("false", r.Merged["AbilityMap"]!["AB_A"]!["Enabled"]!.ToJsonString());
        Assert.Single(r.Adopted);
    }

    [Fact]
    public void KeepsAdminChange_WhenShipUnchanged()
    {
        var b = J("""{"AbilityMap":{"AB_A":{"DamageScale":1}}}""");
        var s = J("""{"AbilityMap":{"AB_A":{"DamageScale":2}}}""");
        var r = RulesMerge.Merge(b, s, b, Resolve);
        Assert.Equal("2", r.Merged["AbilityMap"]!["AB_A"]!["DamageScale"]!.ToJsonString());
        Assert.Single(r.KeptAdmin);
        Assert.Empty(r.Conflicts);
    }

    [Fact]
    public void BothChanged_ConflictKeepsAdmin()
    {
        var b = J("""{"AbilityMap":{"AB_A":{"DamageScale":1}}}""");
        var s = J("""{"AbilityMap":{"AB_A":{"DamageScale":2}}}""");
        var t = J("""{"AbilityMap":{"AB_A":{"DamageScale":3}}}""");
        var r = RulesMerge.Merge(b, s, t, Resolve);
        Assert.Equal("2", r.Merged["AbilityMap"]!["AB_A"]!["DamageScale"]!.ToJsonString());
        Assert.Single(r.Conflicts);
    }

    [Fact]
    public void Tombstones_EntryAddRemove()
    {
        var b = J("""{"AbilityMap":{"AB_A":{"Enabled":true},"AB_B":{"Enabled":true}}}""");
        var s = J("""{"AbilityMap":{"AB_A":{"Enabled":true}}}""");                        // admin deleted B
        var t = J("""{"AbilityMap":{"AB_B":{"Enabled":true},"AB_C":{"Enabled":false}}}"""); // ship deleted A, added C
        var r = RulesMerge.Merge(b, s, t, Resolve);
        var map = (JsonObject)r.Merged["AbilityMap"]!;
        Assert.False(map.ContainsKey("AB_A"));   // ship deletion adopted (server == base)
        Assert.False(map.ContainsKey("AB_B"));   // admin deletion kept (ship == base)
        Assert.True(map.ContainsKey("AB_C"));    // new ship entry added
        Assert.Contains("AbilityMap.AB_C", r.Added);
    }

    [Fact]
    public void ListsCompareNormalized_AndAtomic()
    {
        var b = J("""{"AbilityMap":{"AB_A":{"Weapons":["Sword","!Axe"]}}}""");
        var s = J("""{"AbilityMap":{"AB_A":{"Weapons":["!axe"," sword"]}}}""");   // same set, different order/case
        var t = J("""{"AbilityMap":{"AB_A":{"Weapons":["Spear"]}}}""");
        var r = RulesMerge.Merge(b, s, t, Resolve);
        Assert.Equal("""["Spear"]""", r.Merged["AbilityMap"]!["AB_A"]!["Weapons"]!.ToJsonString());
    }

    [Fact]
    public void IdentityByGuid_NumericAliasMergesOntoServerKey()
    {
        var b = J("""{"AbilityMap":{"AB_A":{"Enabled":true}}}""");
        var s = J("""{"AbilityMap":{"111":{"Enabled":true}}}""");
        var t = J("""{"AbilityMap":{"AB_A":{"Enabled":false}}}""");
        var r = RulesMerge.Merge(b, s, t, Resolve);
        var map = (JsonObject)r.Merged["AbilityMap"]!;
        Assert.False(map.ContainsKey("AB_A"));
        Assert.Equal("false", map["111"]!["Enabled"]!.ToJsonString());
    }

    [Fact]
    public void DuplicateServerIdentity_Refused()
    {
        var s = J("""{"AbilityMap":{"111":{},"AB_A":{}}}""");
        var r = RulesMerge.Merge(J("{}"), s, J("{}"), Resolve);
        Assert.True(r.Refused);
    }

    [Fact]
    public void BaselineHashIgnored()
    {
        var b = J("""{"Version":1}""");
        var s = J("""{"Version":1,"BaselineHash":"abc"}""");
        var r = RulesMerge.Merge(b, s, b, Resolve);
        Assert.False(r.HasChanges);
        Assert.Empty(r.Conflicts);
        Assert.False(r.Merged.ContainsKey("BaselineHash"));
    }

    [Fact]
    public void SafeMode_AddsMissingAndAdoptsBlocksOnly()
    {
        var s = J("""{"AbilityMap":{"AB_A":{"Enabled":true,"DamageScale":5},"AB_B":{"Enabled":true}}}""");
        var t = J("""{"AbilityMap":{"AB_A":{"Enabled":false,"DamageScale":1},"AB_B":{"Enabled":true,"DamageScale":2},"AB_C":{"Enabled":true}}}""");
        var r = RulesMerge.Merge(null, s, t, Resolve);
        Assert.True(r.SafeMode);
        var map = (JsonObject)r.Merged["AbilityMap"]!;
        Assert.Equal("false", map["AB_A"]!["Enabled"]!.ToJsonString());   // block adopted
        Assert.Equal("5", map["AB_A"]!["DamageScale"]!.ToJsonString());   // admin value untouched
        Assert.Null(map["AB_B"]!["DamageScale"]);                          // nothing else adopted
        Assert.True(map.ContainsKey("AB_C"));
    }

    [Fact]
    public void TopLevelAtomic()
    {
        var b = J("""{"Defaults":{"DamageScale":1,"CooldownScale":1}}""");
        var s = J("""{"Defaults":{"DamageScale":2,"CooldownScale":1}}""");
        var t = J("""{"Defaults":{"DamageScale":1,"CooldownScale":3}}""");
        var r = RulesMerge.Merge(b, s, t, Resolve);
        Assert.Single(r.Conflicts);   // nested object is atomic → both changed → conflict, admin kept
        Assert.Equal("2", r.Merged["Defaults"]!["DamageScale"]!.ToJsonString());
    }

    [Fact]
    public void Sha256Stable() => Assert.Equal(RulesMerge.Sha256(new byte[] { 1, 2 }), RulesMerge.Sha256(new byte[] { 1, 2 }));
}
