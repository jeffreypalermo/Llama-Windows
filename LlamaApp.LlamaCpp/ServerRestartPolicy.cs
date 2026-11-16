namespace LlamaApp.Llama;

/// <summary>
/// Why a managed llama server left <see cref="LlamaManager.ServerState.Running"/>.
/// Only <see cref="Unexpected"/> is eligible for an automatic restart. The other
/// kinds are app intent — user Stop, a settings-change restart, and app exit —
/// and must never schedule one.
/// </summary>
public enum ServerExitKind
{
    /// <summary>The process crashed, was killed from outside the app, or stopped answering.</summary>
    Unexpected,

    /// <summary>The user chose Stop.</summary>
    UserStop,

    /// <summary>Saving settings deliberately restarted the server.</summary>
    SettingsChange,

    /// <summary>The app is shutting down.</summary>
    AppExit,
}

/// <summary>
/// Pure decision for bringing a managed llama server back after it dies.
/// The tray app records unexpected exits and asks this whether to relaunch,
/// with exponential backoff, and when to give up and stay
/// <see cref="LlamaManager.ServerState.Failed"/>. No clocks, processes, or
/// settings IO — the caller passes the history — so the rules stay unit-testable.
/// </summary>
public static class ServerRestartPolicy
{
    /// <summary>
    /// Unexpected exits inside <see cref="Window"/> before the server is left
    /// Failed. The exit that reaches this count is not restarted; the ones
    /// before it are, so the number of automatic restarts is
    /// <see cref="MaxRestarts"/>.
    /// </summary>
    public const int FailureLimit = 5;

    /// <summary>Automatic restarts attempted per <see cref="Window"/> before giving up.</summary>
    public const int MaxRestarts = FailureLimit - 1;

    /// <summary>Sliding window the failure count is measured over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Delay before the first automatic restart. Each further exit in the window doubles it.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>Cap on the exponential backoff.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// What to do about one exit. <see cref="ShouldRestart"/> and
    /// <see cref="Exhausted"/> are never both true. <see cref="FailuresInWindow"/>
    /// counts this exit plus earlier unexpected exits still inside
    /// <see cref="Window"/> (zero when the exit is not eligible).
    /// </summary>
    public readonly record struct Decision(
        bool ShouldRestart,
        TimeSpan Delay,
        bool Exhausted,
        int FailuresInWindow);

    /// <summary>
    /// Decides whether <paramref name="kind"/> should be restarted.
    /// <paramref name="earlierExits"/> are unexpected exits already recorded,
    /// not including the one being decided. Times in the future and times
    /// at or beyond <paramref name="window"/> do not count.
    /// </summary>
    public static Decision Decide(
        bool enabled,
        ServerExitKind kind,
        IReadOnlyCollection<DateTime> earlierExits,
        DateTime now,
        int failureLimit = FailureLimit,
        TimeSpan? window = null)
    {
        if (failureLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(failureLimit), "Failure limit must be at least 1.");

        if (!enabled || !IsUnexpected(kind))
            return new Decision(false, TimeSpan.Zero, false, 0);

        var span = window ?? Window;
        var count = 1;
        foreach (var at in earlierExits)
        {
            if (at <= now && now - at < span)
                count++;
        }

        if (count >= failureLimit)
            return new Decision(false, TimeSpan.Zero, true, count);

        return new Decision(true, DelayForAttempt(count), false, count);
    }

    /// <summary>
    /// Backoff for the <paramref name="attempt"/>-th unexpected exit in the
    /// window (1-based). Attempt 1 waits <see cref="InitialDelay"/>; each
    /// next attempt doubles that, up to <see cref="MaxDelay"/>.
    /// </summary>
    public static TimeSpan DelayForAttempt(int attempt)
    {
        if (attempt < 1) attempt = 1;

        var delay = InitialDelay;
        for (var i = 1; i < attempt; i++)
        {
            if (delay >= MaxDelay) return MaxDelay;
            var doubled = delay.Ticks * 2;
            delay = doubled >= MaxDelay.Ticks || doubled < 0
                ? MaxDelay
                : TimeSpan.FromTicks(doubled);
        }

        return delay > MaxDelay ? MaxDelay : delay;
    }

    /// <summary>
    /// True only for <see cref="ServerExitKind.Unexpected"/>. Every other kind
    /// is intentional and is not restarted. The discard throws so a new kind
    /// fails here until it is classified.
    /// </summary>
    private static bool IsUnexpected(ServerExitKind kind) => kind switch
    {
        ServerExitKind.Unexpected => true,
        ServerExitKind.UserStop => false,
        ServerExitKind.SettingsChange => false,
        ServerExitKind.AppExit => false,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled server exit kind."),
    };
}
