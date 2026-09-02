using System.Diagnostics;

namespace Comet.Core.Terminal;

/// <summary>
/// Defines when received serial chunks belong to different display groups.
/// </summary>
internal static class TerminalReceiveGrouping
{
    internal static readonly TimeSpan IdleThreshold = TimeSpan.FromMilliseconds(200);

    internal static bool StartsNewGroup(long? previousTimestamp, long receivedTimestamp) =>
        previousTimestamp is not long previous ||
        receivedTimestamp < previous ||
        Stopwatch.GetElapsedTime(previous, receivedTimestamp) >= IdleThreshold;
}
