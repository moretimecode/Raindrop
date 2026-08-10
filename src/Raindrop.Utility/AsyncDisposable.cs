namespace Raindrop.Utility;

public class AsyncDisposable : IAsyncDisposable
{
    Func<Task>? _disposeAction;
    private AsyncDisposable(Func<Task> disposeAction) { _disposeAction = disposeAction; }

    public static AsyncDisposable Create(Func<Task> disposeAction)
    {
        return disposeAction == null ? throw new ArgumentNullException(nameof(disposeAction)) : new AsyncDisposable(disposeAction);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeAction == null)
        {
            return;
        }
        await _disposeAction.Invoke();
        _disposeAction = default;
    }
}
