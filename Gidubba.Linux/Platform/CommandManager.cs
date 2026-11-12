using Avalonia.Threading;

// Deliberately in System.Windows.Input: the shared view models say "using System.Windows.Input" and call
// CommandManager.InvalidateRequerySuggested(), which on Windows is WPF's. ICommand itself is part of plain .NET.
namespace System.Windows.Input;

/// <summary>
/// The small part of WPF's CommandManager the shared code relies on: every command re-asks "can I run?" after view-model
/// state changes. Requests are coalesced into one pass on the UI thread, because the view models raise this on every
/// property change (and sometimes from background work).
/// </summary>
public static class CommandManager
{
    private static readonly object Gate = new();
    private static readonly List<EventHandler> Handlers = [];
    private static int _pending;

    public static event EventHandler? RequerySuggested
    {
        add { if (value is not null) lock (Gate) Handlers.Add(value); }
        remove { if (value is not null) lock (Gate) Handlers.Remove(value); }
    }

    public static void InvalidateRequerySuggested()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        Dispatcher.UIThread.Post(Raise, DispatcherPriority.Background);
    }

    private static void Raise()
    {
        Interlocked.Exchange(ref _pending, 0);
        EventHandler[] copy;
        lock (Gate) copy = Handlers.ToArray();
        foreach (var handler in copy) handler(null, EventArgs.Empty);
    }
}
