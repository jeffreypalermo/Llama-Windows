using LlamaApp.Llama;
using Xunit;

namespace LlamaApp.LlamaCpp.Tests;

/// <summary>
/// Unit tests for <see cref="ServerRestartPolicy"/> — which exits are
/// restarted, how the delay grows, and when a burst of failures is left
/// <see cref="LlamaManager.ServerState.Failed"/> instead of relaunched.
/// </summary>
public class ServerRestartPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void UnexpectedExit_UnderTheLimit_RestartsWithBackoff(int earlierInWindow, int delaySeconds)
    {
        var decision = ServerRestartPolicy.Decide(
            enabled: true,
            ServerExitKind.Unexpected,
            Earlier(earlierInWindow, Now.AddMinutes(-1)),
            Now);

        Assert.True(decision.ShouldRestart);
        Assert.False(decision.Exhausted);
        Assert.Equal(earlierInWindow + 1, decision.FailuresInWindow);
        Assert.Equal(TimeSpan.FromSeconds(delaySeconds), decision.Delay);
    }

    [Fact]
    public void FifthUnexpectedExit_WithinTheWindow_StaysFailed()
    {
        // Four restarts are attempted (exits 1–4). The exit that reaches
        // FailureLimit is left Failed, which is what the tray shows today
        // when nobody is retrying.
        var decision = ServerRestartPolicy.Decide(
            enabled: true,
            ServerExitKind.Unexpected,
            Earlier(ServerRestartPolicy.MaxRestarts, Now.AddSeconds(-30)),
            Now);

        Assert.False(decision.ShouldRestart);
        Assert.True(decision.Exhausted);
        Assert.Equal(ServerRestartPolicy.FailureLimit, decision.FailuresInWindow);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
    }

    [Fact]
    public void Exits_PastTheLimit_StayFailed()
    {
        var decision = ServerRestartPolicy.Decide(
            enabled: true,
            ServerExitKind.Unexpected,
            Earlier(ServerRestartPolicy.FailureLimit + 2, Now.AddSeconds(-10)),
            Now);

        Assert.False(decision.ShouldRestart);
        Assert.True(decision.Exhausted);
    }

    [Theory]
    [InlineData(ServerExitKind.UserStop)]
    [InlineData(ServerExitKind.SettingsChange)]
    [InlineData(ServerExitKind.AppExit)]
    public void IntentionalExit_NeverRestarts(ServerExitKind kind)
    {
        var quiet = ServerRestartPolicy.Decide(true, kind, [], Now);
        Assert.False(quiet.ShouldRestart);
        Assert.False(quiet.Exhausted);
        Assert.Equal(0, quiet.FailuresInWindow);

        // A full failure history must not turn a deliberate stop into a restart
        // or into the "gave up" outcome. The stop is not a failure.
        var afterFailures = ServerRestartPolicy.Decide(
            true, kind, Earlier(ServerRestartPolicy.FailureLimit, Now.AddSeconds(-5)), Now);
        Assert.False(afterFailures.ShouldRestart);
        Assert.False(afterFailures.Exhausted);
        Assert.Equal(TimeSpan.Zero, afterFailures.Delay);
    }

    [Fact]
    public void Disabled_NeverRestarts_AnUnexpectedExit()
    {
        var decision = ServerRestartPolicy.Decide(
            enabled: false,
            ServerExitKind.Unexpected,
            [],
            Now);

        Assert.False(decision.ShouldRestart);
        Assert.False(decision.Exhausted);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
    }

    [Fact]
    public void Failures_OutsideTheWindow_DoNotCount()
    {
        var window = ServerRestartPolicy.Window;
        var earlier = new List<DateTime>
        {
            Now - window,                 // exactly on the edge — out
            Now - window - TimeSpan.FromSeconds(1),
            Now.AddMinutes(-30),
            Now.AddTicks(1),              // future — out
            Now - TimeSpan.FromSeconds(5) // still inside
        };

        var decision = ServerRestartPolicy.Decide(
            true, ServerExitKind.Unexpected, earlier, Now);

        // Only the one recent exit plus this one.
        Assert.True(decision.ShouldRestart);
        Assert.Equal(2, decision.FailuresInWindow);
        Assert.Equal(TimeSpan.FromSeconds(2), decision.Delay);
    }

    [Fact]
    public void Failure_OneTickInsideTheWindow_Counts()
    {
        var inside = Now - ServerRestartPolicy.Window + TimeSpan.FromTicks(1);

        var decision = ServerRestartPolicy.Decide(
            true, ServerExitKind.Unexpected, [inside], Now);

        Assert.Equal(2, decision.FailuresInWindow);
        Assert.True(decision.ShouldRestart);
    }

    [Fact]
    public void Backoff_CapsAtThirtySeconds()
    {
        // The default limit gives up before the cap matters. A higher limit
        // (tests, or a future change) still must not wait for minutes.
        var earlier = Enumerable.Range(1, 9)
            .Select(i => Now.AddSeconds(-i))
            .ToArray();

        var decision = ServerRestartPolicy.Decide(
            true, ServerExitKind.Unexpected, earlier, Now, failureLimit: 30);

        Assert.True(decision.ShouldRestart);
        Assert.Equal(10, decision.FailuresInWindow);
        Assert.Equal(ServerRestartPolicy.MaxDelay, decision.Delay);
        Assert.Equal(TimeSpan.FromSeconds(16), ServerRestartPolicy.DelayForAttempt(5));
        Assert.Equal(ServerRestartPolicy.MaxDelay, ServerRestartPolicy.DelayForAttempt(6));
    }

    [Fact]
    public void LimitOfOne_LeavesTheFirstExitFailed()
    {
        var decision = ServerRestartPolicy.Decide(
            true, ServerExitKind.Unexpected, [], Now, failureLimit: 1);

        Assert.False(decision.ShouldRestart);
        Assert.True(decision.Exhausted);
        Assert.Equal(1, decision.FailuresInWindow);
    }

    private static DateTime[] Earlier(int count, DateTime at)
        => Enumerable.Range(0, count).Select(i => at.AddSeconds(-i)).ToArray();
}
