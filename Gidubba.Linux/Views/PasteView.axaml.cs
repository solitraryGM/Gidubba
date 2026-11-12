using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TableForge.Import;
using TableForge.ViewModels;

namespace TableForge.Views;

public partial class PasteView : UserControl
{
    public PasteView()
    {
        InitializeComponent();
        // The only thing to do here is paste, so the box takes focus as soon as the screen is shown.
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => PasteSourceBox.Focus(), DispatcherPriority.Background);
        PasteSourceBox.AddHandler(KeyDownEvent, OnPasteSourceKeyDown, RoutingStrategies.Tunnel);
    }

    private PasteViewModel? ViewModel => DataContext as PasteViewModel;

    // Ctrl+J needs the real caret position, which is a TextBox concern, not something the view model has. Ctrl+Enter is done here
    // too, on the way down, because a multi-line TextBox would otherwise take it as a new line.
    private void OnPasteSourceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;

        if (e.Key == Key.J)
        {
            e.Handled = true;
            JoinLines();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (ViewModel?.InterpretCommand is { } interpret && interpret.CanExecute(null)) interpret.Execute(null);
        }
    }

    private void OnJoinLinesClick(object? sender, RoutedEventArgs e) => JoinLines();

    /// <summary>The selection as (start, length), whichever way it was dragged.</summary>
    private static (int Start, int Length) Selection(TextBox box) =>
        (Math.Min(box.SelectionStart, box.SelectionEnd), Math.Abs(box.SelectionEnd - box.SelectionStart));

    /// <summary>
    /// Ctrl+J and Join Lines: a selection touching two or more lines joins those lines into one; anything else (no
    /// selection, or one within a single line) is the original join of the caret's line into the line above it.
    /// </summary>
    private void JoinLines()
    {
        var box = PasteSourceBox;
        var (selStart, selLength) = Selection(box);
        if (!TextCleanup.TryJoinSelectedLines(box.Text ?? "", selStart, selLength, out var start, out var length, out var replacement, out var message))
        {
            JoinCurrentLineWithPrevious();
            return;
        }
        ReplaceSpan(box, start, length, replacement);
        SetMessage(message);
    }

    private void JoinCurrentLineWithPrevious()
    {
        var box = PasteSourceBox;
        var joined = TextCleanup.TryJoinLineWithPrevious(box.Text ?? "", box.CaretIndex, out var result, out var caret, out var message);
        if (joined) ReplaceAll(box, result, caret);
        SetMessage(message);
    }

    private void OnNormalizeTextClick(object? sender, RoutedEventArgs e)
    {
        var box = PasteSourceBox;
        var before = box.Text ?? "";
        var after = TextCleanup.NormalizePastedText(before);
        if (after == before) { SetMessage("Nothing needed normalizing."); return; }
        ReplaceAll(box, after, box.CaretIndex);
        SetMessage("Normalized the pasted text.");
    }

    private void OnDehyphenateClick(object? sender, RoutedEventArgs e)
    {
        var box = PasteSourceBox;
        if (!TextCleanup.TryDehyphenate(box.Text ?? "", out var after))
        {
            SetMessage("No line-wrap hyphenation was found in the pasted text.");
            return;
        }
        ReplaceAll(box, after, box.CaretIndex);
        SetMessage("Removed line-wrap hyphenation from the pasted text.");
    }

    private void OnFindNextDamagedClick(object? sender, RoutedEventArgs e)
    {
        var box = PasteSourceBox;
        var text = box.Text ?? "";
        var (selStart, selLength) = Selection(box);
        var from = Math.Clamp(selStart + selLength, 0, text.Length);
        var index = text.IndexOf('�', from);
        if (index < 0) index = text.IndexOf('�'); // wrap around to the start
        if (index < 0) { SetMessage("No damaged characters were found."); return; }

        box.Focus();
        box.Select(index, index + 1);
        SetMessage("Found a damaged character.");
    }

    /// <summary>
    /// Replaces the whole editor's text by selecting everything and assigning <see cref="TextBox.SelectedText"/>, the same path a
    /// paste or a typed replacement goes through, so the change is one ordinary step on the TextBox's own undo stack.
    /// </summary>
    private static void ReplaceAll(TextBox box, string newText, int caretIndex)
    {
        box.Focus();
        box.SelectAll();
        box.SelectedText = newText;
        var caret = Math.Clamp(caretIndex, 0, (box.Text ?? "").Length);
        box.Select(caret, caret);
        box.CaretIndex = caret;
    }

    /// <summary>Replaces just one span of the editor's text through <see cref="TextBox.SelectedText"/>; the caret lands at the end of the new text.</summary>
    private static void ReplaceSpan(TextBox box, int start, int length, string newText)
    {
        box.Focus();
        box.Select(start, start + length);
        box.SelectedText = newText;
        var end = start + newText.Length;
        box.Select(end, end);
        box.CaretIndex = end;
    }

    private void SetMessage(string message)
    {
        if (ViewModel is { } vm) vm.CleanupMessage = message;
    }
}
