using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TableForge;

/// <summary>The review before a PUM Companion bundle is saved (see <see cref="ViewModels.PumExportReviewViewModel"/>): <see cref="Saved"/> means Save Bundle…</summary>
public partial class PumExportReviewWindow : Window
{
    public PumExportReviewWindow() => InitializeComponent();

    /// <summary>True only when Save Bundle… was chosen; Cancel, Close and the window's own close button leave it false.</summary>
    public bool Saved { get; private set; }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close();
    }
}
