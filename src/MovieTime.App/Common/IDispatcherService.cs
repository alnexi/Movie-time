using System;

namespace MovieTime.App.Common;

/// <summary>
/// Service contract for dispatching UI state changes and bindings to the active UI thread.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Executes the specified action on the UI thread.
    /// If called from the UI thread, executes immediately; otherwise enqueues to the dispatcher.
    /// </summary>
    void RunOnUi(Action action);
}
