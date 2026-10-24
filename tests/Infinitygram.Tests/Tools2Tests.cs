using Infinitygram.Application;

namespace Infinitygram.Tests;

public class Tools2Tests
{
    [Fact]
    public void Filter_SplitsWithoutMixing()
    {
        var members = new[]
        {
            new MemberInfo("1", "alice", "+201000000001", true),
            new MemberInfo("2", null, null, false),
            new MemberInfo("3", "bob", null, true),
        };
        var (withTg, withoutTg, withUser, withPhone) = MemberFilter.Split(members);
        Assert.Equal(2, withTg.Count);
        Assert.Single(withoutTg);
        Assert.Equal(2, withUser.Count);
        Assert.Single(withPhone);
    }

    [Fact]
    public void UsernameGenerator_Valid_Unique_AndInvalidRejected()
    {
        var names = UsernameGenerator.Generate("inf", 50, 10, new Random(42));
        Assert.Equal(50, names.Count);
        Assert.Equal(50, names.Distinct().Count());
        Assert.All(names, n => Assert.True(UsernameGenerator.IsValid(n)));
        Assert.False(UsernameGenerator.IsValid("1abc"));
        Assert.False(UsernameGenerator.IsValid("ab"));
        Assert.False(UsernameGenerator.IsValid("a-b_c"));
    }

    [Fact]
    public void ProxyPool_ParsesForms_AndSkipsDeadProxies()
    {
        Assert.NotNull(ProxyPool.Parse("127.0.0.1:8080"));
        Assert.Equal("user", ProxyPool.Parse("user:pass@127.0.0.1:8080")!.Username);
        Assert.Equal(8080, ProxyPool.Parse("127.0.0.1:8080:user:pass")!.Port);
        Assert.Null(ProxyPool.Parse("not-a-proxy"));
        Assert.Null(ProxyPool.Parse("127.0.0.1:99999"));
        var proxies = new[] { ProxyPool.Parse("a:1")!, ProxyPool.Parse("b:2")! };
        var failures = new Dictionary<string, int> { ["a:1"] = 3 };
        var ptr = 0;
        Assert.Equal("b:2", ProxyPool.Next(proxies, ref ptr, failures)!.Key);
    }

    [Fact]
    public void ThrottlePlan_RestMath_IsExact()
    {
        var plan = new ThrottlePlan(2, 10, 60, 3, 120);
        Assert.True(plan.IsValid);
        Assert.Equal(TimeSpan.FromSeconds(120), plan.TotalRestFor(20));
        Assert.Equal(TimeSpan.Zero, plan.TotalRestFor(9));
        Assert.False(new ThrottlePlan(-1, 10, 60, 3, 120).IsValid);
    }

    [Fact]
    public void CodeExtractor_FindsOnly5DigitCodes()
    {
        var codes = CodeExtractor.ExtractCodes("Your code is 12345. Order 123456 is not a code. Again 12345.");
        Assert.Single(codes);
        Assert.Equal("12345", codes[0]);
        Assert.Empty(CodeExtractor.ExtractCodes("No code here."));
    }

    [Fact]
    public void DashboardSummary_Reconciles()
    {
        var d = new DashboardSummary(3, 5, 100, 7, 2);
        Assert.Equal(109, d.TotalOperations);
        Assert.Contains("accounts: 3", d.ToCleanText());
    }
}
