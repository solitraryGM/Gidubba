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
        _stepCount = _subscribed?.Steps.Count ?? 1;
        Dispatcher.UIThread.Post(() => RollButton.Focus(), DispatcherPriority.Background);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
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
