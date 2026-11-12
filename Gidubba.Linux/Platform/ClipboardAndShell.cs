using System.Diagnostics;
using Avalonia.Input.Platform;

namespace TableForge;

/// <summary>Writing text to the system clipboard. Screens take this as a plain delegate, so tests hand them a list instead.</summary>
public static class ClipboardText
{
    /// <summary>Copies the text. A clipboard that is not available is ignored, like another program holding it on Windows.</summary>
    public static void Set(string text)
    {
        var clipboard = App.MainWindow?.Clipboard;
        if (clipboard is null) return;
        try { UiSync.Wait(clipboard.SetTextAsync(text)); }
        catch (Exception) { /* the text is still on screen to select and copy by hand */ }
    }
}

/// <summary>Opening folders, files and web pages with whatever the desktop uses for them.</summary>
internal static class Shell
{
    /// <summary>Opens a folder, a file or a URL with xdg-open. Throws <see cref="System.ComponentModel.Win32Exception"/> if that is missing.</summary>
    public static void Open(string target)
    {
        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(target);
        using var _ = Process.Start(start);
    }
}
