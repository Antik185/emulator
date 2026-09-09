using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public sealed record ReadinessState(bool IsReady, DateTime ReadyAtUtc, TimeSpan Remaining);

public static class ProfileReadiness
{
    public const int DefaultWaitingHours = 7 * 24;

    public static ReadinessState Calculate(BrowserProfile profile, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var created = profile.CreatedAtUtc.Kind == DateTimeKind.Utc
            ? profile.CreatedAtUtc
            : profile.CreatedAtUtc.ToUniversalTime();
        var waitingPeriod = TimeSpan.FromHours(Math.Max(0, profile.ReadyAfterHours));
        var readyAt = created.Add(waitingPeriod);
        var remaining = readyAt - now;
        return new ReadinessState(remaining <= TimeSpan.Zero, readyAt, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public static string CountdownLabel(ReadinessState state)
    {
        if (state.IsReady)
        {
            return "READY";
        }

        if (state.Remaining.TotalDays >= 1)
        {
            return $"Готов через {(int)state.Remaining.TotalDays}д {state.Remaining.Hours}ч";
        }

        if (state.Remaining.TotalHours >= 1)
        {
            return $"Готов через {(int)state.Remaining.TotalHours}ч {state.Remaining.Minutes}м";
        }

        return $"Готов через {Math.Max(1, (int)Math.Ceiling(state.Remaining.TotalMinutes))}м";
    }
}
