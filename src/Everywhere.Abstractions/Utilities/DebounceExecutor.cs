using ITimer = Everywhere.Common.ITimer;

namespace Everywhere.Utilities;

/// <summary>
/// A high-performance, low-allocation debounced executor.
/// It debounces calls to a parameterless method and, when the delay has passed,
/// it invokes a value provider (Func{T}) and passes the result to an action (Action{T}).
/// </summary>
/// <typeparam name="TSender">The type of the value to be processed.</typeparam>
/// <typeparam name="TTimer"></typeparam>
public class DebounceExecutor<TSender, TTimer> : IDisposable where TTimer : class, ITimer, new()
{
    /// <summary>
    /// Gets or sets the debounce delay time.
    /// </summary>
    public TimeSpan Delay { get; set; }

    private readonly TTimer _timer;
    private readonly Func<TSender> _valueProvider;
    private readonly Action<TSender> _action;

    private volatile bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebounceExecutor{TSender, TTimer}"/> class.
    /// </summary>
    /// <param name="valueProvider">The function to call to get the value when the action is to be executed.</param>
    /// <param name="action">The action to execute with the value from the provider.</param>
    /// <param name="delay">The debounce delay time.</param>
    public DebounceExecutor(Func<TSender> valueProvider, Action<TSender> action, TimeSpan delay)
    {
        _valueProvider = valueProvider;
        _action = action;
        Delay = delay;
        _timer = new TTimer();
        _timer.Callback += TimerCallback;
    }

    /// <summary>
    /// Triggers the execution of the action after the debounce delay.
    /// If called again before the delay has passed, the timer is reset.
    /// I've renamed Execute to Trigger, as it's a more fitting name for a parameterless method that starts a process.
    /// </summary>
    public void Trigger()
    {
        if (_isDisposed)
        {
            return;
        }

        // This is thread-safe. It will reset the timer to the specified delay.
        _timer.Interval = Delay;
        _timer.Start();
    }

    public void Cancel()
    {
        if (_isDisposed)
        {
            return;
        }

        // Cancel the timer by setting the due time to infinite.
        _timer.Stop();
    }

    private void TimerCallback()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            // Get the value and execute the action.
            var value = _valueProvider();
            _action(value);
        }
        catch
        {
            // Depending on requirements, you might want to log exceptions here.
            // By default, we suppress exceptions from the provider or action to prevent the timer from crashing.
        }
    }

    /// <summary>
    /// Disposes the executor, stopping any pending operations.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Dispose();

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}