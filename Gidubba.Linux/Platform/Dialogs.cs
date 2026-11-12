using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableForge;

/// <summary>A small message / question window, built in code. Stands in for WPF's MessageBox.</summary>
internal sealed class MessageWindow : Window
{
    /// <summary>True when the first (default) button was chosen.</summary>
    public bool Result { get; private set; }

    public MessageWindow(string title, string message, string primary, string? secondary)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = App.WindowIcon;

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

        var first = new Button { Content = primary, IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        first.Click += (_, _) => { Result = true; Close(true); };
        buttons.Children.Add(first);

        if (secondary is not null)
        {
            var second = new Button { Content = secondary, IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            second.Click += (_, _) => { Result = false; Close(false); };
            buttons.Children.Add(second);
        }

        Content = new StackPanel { Margin = new Thickness(18), Children = { text, buttons } };
    }
}

/// <summary>Message boxes and modal windows, in the synchronous shape the shared view models expect.</summary>
internal static class Dialogs
{
    /// <summary>A Yes/No (or OK/Cancel) question. Closing the window counts as the second answer.</summary>
    public static bool Confirm(string message, string title, string yes = "Yes", string no = "No")
    {
        var window = new MessageWindow(title, message, yes, no);
        ShowModal(window);
        return window.Result;
    }

    /// <summary>A message with one OK button.</summary>
    public static void Info(string message, string? title = null) =>
        ShowModal(new MessageWindow(title ?? AppInfo.Title, message, "OK", null));

    /// <summary>Shows a window modally over the main window and returns once it is closed.</summary>
    public static void ShowModal(Window dialog)
    {
        var owner = App.MainWindow;
        if (owner is { IsVisible: true })
        {
            UiSync.Wait(dialog.ShowDialog(owner));
            return;
        }

        // No visible owner (nothing is open yet): show it on its own and wait for it to close.
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Show();
        UiSync.Wait(closed.Task);
    }
}
