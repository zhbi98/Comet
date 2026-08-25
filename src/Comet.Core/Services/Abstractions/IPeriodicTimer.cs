namespace Comet.Services.Abstractions;

/// <summary>
/// Provides periodic callbacks without tying repeat-send coordination to a UI timer
/// or a specific Windows timer implementation.
/// </summary>
public interface IPeriodicTimer : IDisposable
{
    void Change(TimeSpan dueTime, TimeSpan period);

    void Stop();
}
