namespace UrHmi.Tools.Screwdriver;

internal static class SpinBridgeReconnectBackoff
{
    public static readonly TimeSpan Initial = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Max = TimeSpan.FromSeconds(30);

    public static TimeSpan Delay(int failedAttempt)
    {
        var attempt = Math.Max(0, failedAttempt);
        var milliseconds = Initial.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 8));
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, Max.TotalMilliseconds));
    }
}
