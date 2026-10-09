using AccountSnapshot = CodexDiscordPresence.CodexAccountBillingTypeProvider.AccountSnapshot;

namespace CodexDiscordPresence.Tests;

public sealed class CodexAccountRefreshTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
    private static AccountSnapshot Success => new("subsc", new(25, 300, Now.AddHours(3)));

    [Fact]
    public void GetBillingType_PendingRefresh_ReturnsImmediatelyAndSharesSingleRequest()
    {
        var completion = new TaskCompletionSource<AccountSnapshot>();
        var calls = 0;
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
        {
            calls++;
            return completion.Task;
        }, () => Now);

        Assert.Null(provider.GetBillingType());
        Assert.Null(provider.GetRateLimit());
        Assert.Null(provider.GetBillingType());
        Assert.Equal(1, calls);
        completion.SetResult(Success);
        Assert.Equal("subsc", provider.GetBillingType());
        Assert.Equal(25, provider.GetRateLimit()?.UsedPercent);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(CodexActivityKind.ApplyingEdits, 60)]
    [InlineData(CodexActivityKind.WaitingForInput, 300)]
    [InlineData(CodexActivityKind.Ready, 300)]
    public void GetRateLimit_UsesActivityAppropriateRefreshInterval(CodexActivityKind kind, int seconds)
    {
        var now = Now;
        var calls = 0;
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
        {
            calls++;
            return Task.FromResult(Success);
        }, () => now);
        provider.SetActivity(kind);

        Assert.Equal("subsc", provider.GetBillingType());
        now = now.AddSeconds(seconds - 1);
        provider.GetRateLimit();
        Assert.Equal(1, calls);
        now = now.AddSeconds(1);
        provider.GetRateLimit();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void SetActivity_ResumingWork_DoesNotWaitForIdleDeadline()
    {
        var now = Now;
        var calls = 0;
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
        {
            calls++;
            return Task.FromResult(Success);
        }, () => now);
        provider.SetActivity(CodexActivityKind.WaitingForInput);
        provider.GetBillingType();
        now = now.AddMinutes(1);
        provider.GetRateLimit();
        Assert.Equal(1, calls);
        provider.SetActivity(CodexActivityKind.RunningCommand);
        provider.GetRateLimit();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void GetRateLimit_RepeatedFailures_BackOffAndSuccessfulRecoveryResetsDelay()
    {
        var now = Now;
        var calls = 0;
        var failed = true;
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
        {
            calls++;
            return Task.FromResult(failed ? new AccountSnapshot(null, null) : Success);
        }, () => now);
        provider.SetActivity(CodexActivityKind.ApplyingEdits);
        provider.GetBillingType();
        foreach (var delay in new[] { 1, 2, 4, 8, 15, 15 })
        {
            var previousCalls = calls;
            now = now.AddMinutes(delay).AddSeconds(-1);
            provider.GetBillingType();
            Assert.Equal(previousCalls, calls);
            now = now.AddSeconds(1);
            provider.GetBillingType();
            Assert.Equal(previousCalls + 1, calls);
        }
        failed = false;
        now = now.AddMinutes(15);
        Assert.Equal("subsc", provider.GetBillingType());
        var callsAfterRecovery = calls;
        now = now.AddMinutes(1);
        provider.GetRateLimit();
        Assert.Equal(callsAfterRecovery + 1, calls);
    }

    [Fact]
    public void GetRateLimit_FailedRefresh_OmitsStaleQuotaAndRetainsKnownBillingType()
    {
        var now = Now;
        var failed = false;
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
            Task.FromResult(failed ? new AccountSnapshot(null, null) : Success), () => now);
        provider.SetActivity(CodexActivityKind.ApplyingEdits);
        Assert.Equal(25, provider.GetRateLimit()?.UsedPercent);
        failed = true;
        now = now.AddMinutes(1);

        Assert.Null(provider.GetRateLimit());
        Assert.Equal("subsc", provider.GetBillingType());
    }

    [Fact]
    public void GetBillingType_ProviderSelectedAgain_RefreshesOldCompletedRequest()
    {
        var now = Now;
        var calls = 0;
        var completion = new TaskCompletionSource<AccountSnapshot>();
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
        {
            calls++;
            return calls == 1 ? completion.Task : Task.FromResult(Success);
        }, () => now);
        provider.GetBillingType();
        completion.SetResult(Success);
        now = now.AddMinutes(10);

        Assert.Equal("subsc", provider.GetBillingType());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void GetBillingType_CancelledCaller_DoesNotStartRefresh()
    {
        var calls = 0;
        var provider = new CodexAccountBillingTypeProvider("isolated-home", _ =>
        {
            calls++;
            return Task.FromResult(Success);
        }, () => Now);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => provider.GetBillingType(cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void TokenUsageProvider_InitialBackgroundLoadUnavailable_RecoversOnNextSnapshot()
    {
        var completion = new TaskCompletionSource<AccountSnapshot>();
        var account = new CodexAccountBillingTypeProvider("isolated-home", _ => completion.Task, () => Now);
        var usage = new TokenUsageProvider(new CodexDetectionOptions(),
            new TokenUsageOptions { Enabled = false }, account, account);

        Assert.Null(usage.GetSnapshot(includeSessionScan: false).BillingType);
        completion.SetResult(Success);
        var recovered = usage.GetSnapshot(includeSessionScan: false);
        Assert.Equal("subsc", recovered.BillingType);
        Assert.Equal(25, recovered.RateLimit?.UsedPercent);
    }
}
