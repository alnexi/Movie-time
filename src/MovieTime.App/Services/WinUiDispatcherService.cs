using System;
using Microsoft.UI.Dispatching;
using MovieTime.App.Common;

namespace MovieTime.App.Services;

/// <summary>
/// Thread dispatcher implementation for WinUI 3 ensuring that background/unmanaged
/// events (e.g. LibVLC player state, position, and tracks) are safely marshaled to the UI thread.
/// </summary>
public sealed class WinUiDispatcherService : IDispatcherService
{
    private DispatcherQueue? _dispatcherQueue;

    public WinUiDispatcherService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Explicitly binds or updates the active DispatcherQueue from a Window or UI element.
    /// </summary>
    public void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    public void RunOnUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var queue = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (queue != null && !queue.HasThreadAccess)
        {
            queue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }
}
