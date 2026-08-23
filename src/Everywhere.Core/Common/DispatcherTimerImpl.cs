using Avalonia.Threading;

namespace Everywhere.Common;

/// <summary>
/// Wraps an Avalonia DispatcherTimer to implement the ITimer interface.
/// </summary>
public sealed class DispatcherTimerImpl : ITimer
{
    public event Action? Callback;

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    private readonly DispatcherTimer _timer;

    public DispatcherTimerImpl()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Default);
        _timer.Tick += HandleTimerTick;
    }

    private void HandleTimerTick(object? sender, EventArgs e)
    {
        Callback?.Invoke();
        _timer.Stop();
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose() { }
}