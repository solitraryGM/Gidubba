using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableForge.ViewModels;

namespace TableForge.Views;

public partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Focus moving into any control of a row shows where that row came from in the original text.
        AddHandler(GotFocusEvent, OnFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        AlignedRowsScroller.ScrollChanged += OnAlignedRowsScrollChanged;
        AlignedHeaderScroller.ScrollChanged += OnAlignedHeaderScrollChanged;

        // The table name is the first thing worth checking.
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => NameBox.Focus(), DispatcherPriority.Background);
    }

    private ReviewViewModel? _subscribed;

    private static IInputElement? FocusedElement(Visual from) => TopLevel.GetTopLevel(from)?.FocusManager?.GetFocusedElement();

    // ---- keys -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Handled on the way down, before the text boxes see the keys. Ctrl+B, Ctrl+I and Ctrl+Space in a row's result text need the real
    /// selection of the text box being typed in, which the view model does not have (Ctrl+J on the Paste screen works the same way);
    /// anywhere else on the screen those keys are left alone. The paste-rows area's Escape (close) and Ctrl+Enter (add) are here too,
    /// because a multi-line text box would otherwise swallow them.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = FocusedElement(this);

        if (DataContext is ReviewViewModel vm && vm.IsPasteRowsOpen && focused is Visual inside && PasteRowsArea.IsVisualAncestorOf(inside))
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                if (vm.TogglePasteRowsCommand.CanExecute(null)) vm.TogglePasteRowsCommand.Execute(null);
                return;
            }
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
                if (vm.AppendPastedRowsCommand.CanExecute(null)) vm.AppendPastedRowsCommand.Execute(null);
                return;
            }
        }

        if (e.KeyModifiers != KeyModifiers.Control) return;
        if (FormattingKey(e.Key) is not { } action || !IsResultTextBox(focused)) return;
        e.Handled = true;
        ApplyFormatting(action);
    }

    /// <summary>What a Ctrl+key does to the selected result text; null for every other key.</summary>
    public static FormattingAction? FormattingKey(Key key) => key switch
    {
        Key.B => FormattingAction.Bold,
        Key.I => FormattingAction.Italic,
        Key.Space => FormattingAction.Clear,
        _ => null,
    };

    // ---- bold / italic ----------------------------------------------------------------------------------------------

    private void OnBoldClick(object? sender, RoutedEventArgs e) => ApplyFormatting(FormattingAction.Bold);
    private void OnItalicClick(object? sender, RoutedEventArgs e) => ApplyFormatting(FormattingAction.Italic);
    private void OnClearFormattingClick(object? sender, RoutedEventArgs e) => ApplyFormatting(FormattingAction.Clear);

    /// <summary>
    /// Formats the selection in the result text box that has keyboard focus. The buttons cannot take focus, so after a click
    /// that is still the box the person was selecting in, with its selection untouched.
    /// </summary>
    public void ApplyFormatting(FormattingAction action)
    {
        if (DataContext is not ReviewViewModel vm) return;
        var box = FocusedElement(this) as TextBox;
        if (!IsResultTextBox(box)) box = null;

        var start = box is null ? 0 : Math.Min(box.SelectionStart, box.SelectionEnd);
        var length = box is null ? 0 : Math.Abs(box.SelectionEnd - box.SelectionStart);
        vm.ApplyFormatting(box?.DataContext as EntryRowViewModel, start, length, action);
    }

    private static bool IsResultTextBox(object? element) => element is TextBox { Name: "ResultTextBox", DataContext: EntryRowViewModel };

    // ---- edit tracking ----------------------------------------------------------------------------------------------

    private readonly ConditionalWeakTable<TextBox, string> _lastText = new();

    /// <summary>
    /// Tells the row exactly where the person's edit happened, so formatting next to it lands on the right side of a boundary
    /// (see <see cref="EntryRowViewModel.RefineLastEdit"/>). Avalonia does not report the change itself, so it is worked out by
    /// comparing the text before and after (the common start and end are kept). Only the focused box reports: the same row can also
    /// be shown, hidden, in the other layout, and that copy just receives the new text as a whole.
    /// </summary>
    private void OnResultTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        var now = box.Text ?? "";
        _lastText.TryGetValue(box, out var before);
        _lastText.AddOrUpdate(box, now);

        if (before is null || before == now || !box.IsFocused || box.DataContext is not EntryRowViewModel row) return;

        var min = Math.Min(before.Length, now.Length);
        var prefix = 0;
        while (prefix < min && before[prefix] == now[prefix]) prefix++;
        var suffix = 0;
        while (suffix < min - prefix && before[before.Length - 1 - suffix] == now[now.Length - 1 - suffix]) suffix++;
        row.RefineLastEdit(prefix, before.Length - prefix - suffix, now.Length - prefix - suffix);
    }

    // ---- aligned table scrolling -----------------------------------------------------------------------------------

    /// <summary>
    /// The aligned table's headings move with its rows: the heading strip is kept exactly as wide as the rows' viewport (so its
    /// columns get the same width; the rows' vertical scrollbar takes some of theirs) and at the same sideways offset. Only the
    /// rows' own ScrollViewer counts: a result text box's inner one reports here too.
    /// </summary>
    private void OnAlignedRowsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, AlignedRowsScroller)) return;
        var viewport = AlignedRowsScroller.Viewport.Width;
        if (viewport > 0 && Math.Abs(AlignedHeaderScroller.Width - viewport) > 0.5) AlignedHeaderScroller.Width = viewport;
        if (Math.Abs(AlignedHeaderScroller.Offset.X - AlignedRowsScroller.Offset.X) > 0.5)
            AlignedHeaderScroller.Offset = new Vector(AlignedRowsScroller.Offset.X, 0);
    }

    /// <summary>The other way round: Tab into a heading that is scrolled out of view brings the rows along with it.</summary>
    private void OnAlignedHeaderScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, AlignedHeaderScroller) || e.OffsetDelta.X == 0) return;
        if (Math.Abs(AlignedRowsScroller.Offset.X - AlignedHeaderScroller.Offset.X) > 0.5)
            AlignedRowsScroller.Offset = new Vector(AlignedHeaderScroller.Offset.X, AlignedRowsScroller.Offset.Y);
    }

    // ---- view model --------------------------------------------------------------------------------------------------

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnViewModelChanged;
        _subscribed = DataContext as ReviewViewModel;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnViewModelChanged;
    }

    private void OnFocused(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReviewViewModel vm && (e.Source as Control)?.DataContext is EntryRowViewModel row)
            vm.SelectedRow = row;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ReviewViewModel vm) return;

        if (e.PropertyName == nameof(ReviewViewModel.SelectedRow) && vm.SelectedRow is { } row)
            HighlightSourceLines(row.SourceLineStart, row.SourceLineEnd);

        // Opening the paste area puts the cursor in it; closing it (button or Escape) puts focus back where it came from.
        if (e.PropertyName == nameof(ReviewViewModel.IsPasteRowsOpen))
            Dispatcher.UIThread.Post(() => (vm.IsPasteRowsOpen ? (Control)PasteRowsBox : PasteRowsToggle).Focus(), DispatcherPriority.Background);
    }

    /// <summary>Selects whole lines (one-based, inclusive) in the read-only original text.</summary>
    private void HighlightSourceLines(int firstLine, int lastLine)
    {
        if (firstLine < 1 || lastLine < firstLine) return;

        var text = SourceBox.Text ?? "";
        int start = -1, end = -1, position = 0, line = 1;
        while (true)
        {
            var newline = text.IndexOf('\n', position);
            var lineEnd = newline < 0 ? text.Length : newline;
            if (lineEnd > position && text[lineEnd - 1] == '\r') lineEnd--; // not part of the line

            if (line == firstLine) start = position;
            if (line == lastLine) { end = lineEnd; break; }
            if (newline < 0) break;
            position = newline + 1;
            line++;
        }
        if (start < 0 || end < start) return;

        SourceBox.Select(start, end);
        SourceBox.CaretIndex = end;
    }
}
