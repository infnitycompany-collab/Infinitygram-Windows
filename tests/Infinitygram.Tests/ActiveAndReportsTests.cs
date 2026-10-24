using Infinitygram.Application;
using Infinitygram.Domain;

namespace Infinitygram.Tests;

public class ActiveAndReportsTests
{
    [Fact]
    public void Activation_Cold_To_Super_OnlyWhenChecklistComplete()
    {
        var cold = new AccountActivation("a", false, false, false, false, 0, 0, 0);
        Assert.Equal("Cold", cold.Status);
        Assert.Equal(0, cold.CompletionPercent);

        var warming = cold with { NameSet = true, PhotoSet = true };
        Assert.Equal("Warming", warming.Status);

        var active = new AccountActivation("a", true, true, true, true, 2, 2, 0);
        Assert.Equal("Active", active.Status); // spaces done, no messages yet: NOT Super
        Assert.NotEqual(100, active.CompletionPercent);

        var superAcc = active with { MessagesPosted = 4 };
        Assert.Equal("Super", superAcc.Status);
        Assert.Equal(100, superAcc.CompletionPercent);
        Assert.Empty(superAcc.MissingSteps);
    }

    [Fact]
    public void Activation_OneGroupOrOneChannel_IsNotSuper()
    {
        Assert.NotEqual("Super", new AccountActivation("a", true, true, true, true, 1, 2, 4).Status);
        Assert.NotEqual("Super", new AccountActivation("a", true, true, true, true, 2, 1, 4).Status);
        Assert.NotEqual("Super", new AccountActivation("a", true, true, true, false, 2, 2, 4).Status);
    }

    [Fact]
    public void Activation_MissingSteps_ListExactlyWhatRemains()
    {
        var a = new AccountActivation("a", true, false, true, true, 1, 2, 2);
        Assert.Contains("photo", a.MissingSteps);
        Assert.Contains("groups:1-left", a.MissingSteps);
        Assert.Contains("messages:2-left", a.MissingSteps);
    }

    [Fact]
    public void Report_TotalsReconcile_AndPerAccountSumsToGrandTotal()
    {
        var results = new[]
        {
            new AddResult("m1", "a", AddOutcomeKind.Added, "ok"),
            new AddResult("m2", "a", AddOutcomeKind.SkippedPrivacy, "privacy"),
            new AddResult("m3", "b", AddOutcomeKind.Added, "ok"),
            new AddResult("m4", "b", AddOutcomeKind.Failed, "failed"),
            new AddResult("m5", "b", AddOutcomeKind.Uncertain, "unknown"),
        };
        var report = OperationReport.FromAddResults("add", "MyGroup", results);
        Assert.True(report.Reconciles);
        Assert.Equal(5, report.Total);
        Assert.Equal(2, report.Added);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(1, report.Failed);
        Assert.Equal(1, report.Uncertain);
        Assert.Equal(report.Total, report.ByAccount.Sum(a => a.Total));
        var text = report.ToCleanText();
        Assert.Contains("total: 5 | added: 2", text);
        Assert.Contains("reconciles: yes", text);
        Assert.Contains("account: a | total: 2", text);
        Assert.Contains("account: b | total: 3", text);
    }

    [Fact]
    public void Report_EmptyInput_IsZero_AndReconciles()
    {
        var report = OperationReport.FromAddResults("add", "G", Array.Empty<AddResult>());
        Assert.Equal(0, report.Total);
        Assert.True(report.Reconciles);
    }
}
