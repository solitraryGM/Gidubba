using Avalonia.Threading;

namespace TableForge;

/// <summary>
/// The shared view models ask for things synchronously: "confirm this" returns a bool, a Save dialog returns a path, About blocks
/// until it is closed (that is how the WPF build's ShowDialog behaves). Avalonia's dialogs are asynchronous, so this waits for one
/// WITHOUT freezing the window: it runs a nested UI loop until the task completes, which is exactly what a modal dialog does.
/// Must be called on the UI thread (every command handler already is).
/// </summary>
internal static class UiSync
{
    public static T Wait<T>(Task<T> task)
    {
        Wait((Task)task);
        return task.GetAwaiter().GetResult();
    }

    public static void Wait(Task task)
    {
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return; }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            task.GetAwaiter().GetResult(); // a background caller can simply block
            return;
        }

        using var stop = new CancellationTokenSource();
        _ = task.ContinueWith(_ => stop.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(stop.Token); // nested loop: the window stays alive while the dialog is open
        task.GetAwaiter().GetResult();
    }
}
