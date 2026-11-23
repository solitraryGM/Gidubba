using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableForge.ViewModels;

namespace TableForge.Views;

public partial class RollView : UserControl
{
    public RollView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Modifier box: arriving at it selects its whole value, so typing replaces the "0" instead of joining it ("+20").
        ModifierBox.GotFocus += OnModifierGotFocus;
        ModifierBox.AddHandler(PointerPressedEvent, OnModifierPointerPressed, RoutingStrategies.Tunnel);

        // Complete Result (1.6): a bounded body, and keyboard scrolling from its toggle or Copy button.
        SizeChanged += (_, _) => UpdateCompleteResultHeight();
        RollControls.SizeChanged += (_, _) => UpdateCompleteResultHeight();
        CompleteResultPanel.AddHandler(KeyDownEvent, OnCompleteResultKeyDown, RoutingStrategies.Bubble);
    }

    // ---- Complete Result panel: bounded, so the trail and the Roll controls always keep their room ----------------------

    /// <summary>The panel's body takes at most this share of the Roll screen's height…</summary>
    public const double CompleteResultShare = 0.30;

    /// <summary>…and never more than this, however tall the window.</summary>
    public const double CompleteResultMaxBody = 240;

    /// <summary>The smallest body worth showing (about one caption and one line of result). Below that the panel still opens,
    /// but scrolls within this height.</summary>
    public const double CompleteResultMinBody = 44;

    /// <summary>Room the trail keeps beyond the Roll controls themselves, so they are never the only thing in view.</summary>
    public const double TrailReserve = 48;

    /// <summary>
    /// Keyboard scrolling for the Complete Result, without making its body another Tab stop: while its toggle or Copy Complete
    /// Result has focus, Page Up / Page Down scroll the body a page, Home / End go to its start / end. Nothing happens while the
    /// panel is collapsed.
    /// </summary>
    private void OnCompleteResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !CompleteResultScroller.IsEffectivelyVisible) return;
        switch (e.Key)
        {
            case Key.PageDown: CompleteResultScroller.PageDown(); break;
            case Key.PageUp: CompleteResultScroller.PageUp(); break;
            case Key.Home: CompleteResultScroller.ScrollToHome(); break;
            case Key.End: CompleteResultScroller.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Bounds the Complete Result's body: <see cref="CompleteResultShare"/> of the screen, at most <see cref="CompleteResultMaxBody"/>,
    /// and — in a small window, or with Recent Rolls open — only what is left once the trail has room for the whole Roll
    /// controls block plus <see cref="TrailReserve"/>. The body shrinks first; the trail is never squeezed for it.
    /// </summary>
    private void UpdateCompleteResultHeight()
    {
        var available = Bounds.Height;
        if (available <= 0) return;
        var header = CompleteResultPanel.Padding.Top + CompleteResultPanel.Padding.Bottom + CompleteResultPanel.BorderThickness.Bottom
            + Math.Max(CompleteResultExpander.Bounds.Height - CompleteResultScroller.Bounds.Height - CompleteResultScroller.Margin.Top, 30)
            + CompleteResultScroller.Margin.Top;
        var trailNeeds = RollControls.Bounds.Height + RollControls.Margin.Top + TrailContent.Margin.Top + TrailContent.Margin.Bottom + TrailReserve;
        var body = Math.Min(Math.Min(available * CompleteResultShare, CompleteResultMaxBody), available - header - trailNeeds);
        var max = Math.Max(CompleteResultMinBody, Math.Floor(body));
        if (Math.Abs(CompleteResultScroller.MaxHeight - max) > 0.5) CompleteResultScroller.MaxHeight = max;
    }

    private int _stepCount = 1;
    private RollViewModel? _subscribed;

    // ---- Modifier box ------------------------------------------------------------------------------------------------

    /// <summary>Tab, Shift+Tab or any other way in selects the whole value.</summary>
    private void OnModifierGotFocus(object? sender, GotFocusEventArgs e) => SelectModifierSoon();

    /// <summary>
    /// The first click into the box only focuses it (selecting the whole value), instead of also placing the caret beside the
    /// "0". Once it has focus, clicks place the caret as usual so the value can be edited.
    /// </summary>
    private void OnModifierPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ModifierBox.IsFocused) return;
        ModifierBox.Focus();
        e.Handled = true;
    }

    /// <summary>After the focus change has settled, so the click that gave focus cannot move the caret over the selection.</summary>
    private void SelectModifierSoon() =>
        Dispatcher.UIThread.Post(() => { if (ModifierBox.IsFocused) ModifierBox.SelectAll(); }, DispatcherPriority.Input);

    /// <summary>Export… opens its menu under the button, by mouse or keyboard alike.</summary>
    private void OnExportClick(object? sender, RoutedEventArgs e) => ExportMenu.Open(ExportButton);

    // ---- view model --------------------------------------------------------------------------------------------------

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnViewModelChanged;
        _subscribed = DataContext as RollViewModel;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnViewModelChanged;

        // This view is reused when another table is opened, so start that table at its top, ready to roll.
        TrailScroller.Offset = default;
        CompleteResultScroller.Offset = default;
        _stepCount = _subscribed?.Steps.Count ?? 1;
        Dispatcher.UIThread.Post(() => RollButton.Focus(), DispatcherPriority.Background);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The Complete Result changes before Results, so its newest part is brought into view first, then the trail scrolls.
        if (e.PropertyName == nameof(RollViewModel.CompleteResult))
            Dispatcher.UIThread.Post(() => CompleteResultScroller.ScrollToEnd(), DispatcherPriority.Background);

        // A new roll or a followed link changes Results; bring the new content into view once its visuals have been laid out.
        if (e.PropertyName == nameof(RollViewModel.Results))
            Dispatcher.UIThread.Post(ScrollToLatest, DispatcherPriority.Background);

        // A roll used the modifier up while its box still has focus: select the reset value, ready to be typed over.
        if (e.PropertyName == nameof(RollViewModel.ModifierText) && ModifierBox.IsFocused)
            SelectModifierSoon();
    }

    /// <summary>
    /// Scrolls so the newest roll (or, right after following a link, the new step) starts at the top of the view,
    /// unless that would push the Roll controls out of sight, in which case the controls stay visible.
    /// Older steps are left exactly as they are.
    /// </summary>
    private void ScrollToLatest()
    {
        if (DataContext is not RollViewModel vm || vm.Steps.Count == 0) return;
        UpdateLayout();

        // After following a link, focus goes to Roll for the new table. After an ordinary roll it stays where it was,
        // so someone typing several manual rolls in a row keeps their cursor in the box.
        if (vm.Steps.Count != _stepCount)
        {
            _stepCount = vm.Steps.Count;
            RollButton.Focus();
        }

        var step = vm.Current;
        var stepElement = FindContainer(StepsList, step);
        var anchor = step.Outcomes.Count > 0 && stepElement is not null
            ? FindContainer(stepElement, step.Outcomes[^1]) ?? stepElement
            : stepElement;
        if (anchor is null) return;

        var anchorTop = TopWithin(anchor, TrailContent);
        var controlsBottom = TopWithin(RollControls, TrailContent) + RollControls.Bounds.Height + TrailContent.Margin.Bottom;
        if (anchorTop is null || controlsBottom is null) return;

        var offset = Math.Max(anchorTop.Value, controlsBottom.Value - TrailScroller.Viewport.Height);
        TrailScroller.Offset = new Vector(TrailScroller.Offset.X, Math.Max(0, offset));
    }

    private static double? TopWithin(Visual element, Visual ancestor) => element.TranslatePoint(new Point(0, 0), ancestor)?.Y;

    /// <summary>Finds the generated container (the element wrapping one item) below <paramref name="root"/>.</summary>
    private static Control? FindContainer(Visual root, object item)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is ContentPresenter presenter && ReferenceEquals(presenter.Content, item)) return presenter;
            if (FindContainer(child, item) is { } found) return found;
        }
        return null;
    }
}
