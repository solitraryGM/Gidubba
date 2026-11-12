using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableForge.ViewModels;

namespace TableForge;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            FitToScreen();
            FocusStartingPoint();
        };
        LayoutUpdated += OnLayoutUpdated;
        DataContextChanged += OnDataContextChanged;

        // A click on a table opens it, even if it is already highlighted (selection itself only highlights).
        TablesList.AddHandler(PointerReleasedEvent, OnTablePointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private MainViewModel? _subscribed;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null) _subscribed.CollectionCreated -= OnCollectionCreated;
        _subscribed = DataContext as MainViewModel;
        if (_subscribed is not null) _subscribed.CollectionCreated += OnCollectionCreated;
    }

    /// <summary>Never open larger than the screen's usable area (the default suits a 1080p display, not a 768-pixel laptop).</summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var area = screen.WorkingArea;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width / scale - 20));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height / scale - 20));
    }

    /// <summary>A brand-new user starts in the collection name box; otherwise the search box, ready to find a table.</summary>
    private void FocusStartingPoint()
    {
        if (DataContext is MainViewModel { Collections.Count: 0 }) NewCollectionBox.Focus();
        else TableFilterBox.Focus();
    }

    private void OnTablePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (DataContext is not MainViewModel vm) return;
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item is not null && ReferenceEquals(item.DataContext, vm.HighlightedTable) && vm.OpenTableCommand.CanExecute(null))
            vm.OpenTableCommand.Execute(null);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        FitTableList();
        FitRecentRolls();
    }

    /// <summary>
    /// The table list fills the left pane down to its Edit/Delete buttons, but never shrinks below 160 px: on a short window the pane
    /// scrolls a little instead. Its place in the pane does not depend on its own height, so this settles at once.
    /// </summary>
    private void FitTableList()
    {
        if (!TablesList.IsVisible) return;
        var top = TablesList.TranslatePoint(new Point(0, 0), LeftContent);
        if (top is null) return;
        var room = LeftScroll.Viewport.Height - top.Value.Y - TableActions.Bounds.Height - TableActions.Margin.Top;
        var max = Math.Max(160, Math.Floor(room));
        if (double.IsInfinity(TablesList.MaxHeight) || Math.Abs(TablesList.MaxHeight - max) > 0.5) TablesList.MaxHeight = max;
    }

    /// <summary>The Recent Rolls list takes at most 240 px, and never more than 40% of the main area, so the screen above keeps its room.</summary>
    private void FitRecentRolls()
    {
        var max = Math.Clamp(MainArea.Bounds.Height * 0.4, 80, 240);
        if (Math.Abs(RecentRollsScroller.MaxHeight - max) > 0.5) RecentRollsScroller.MaxHeight = max;
    }

    /// <summary>Show Dice: brings the dddice dice window back (it may have been closed or hidden behind Gidubba).</summary>
    private void OnShowDice(object? sender, RoutedEventArgs e) => App.DddiceRoller?.Present();

    /// <summary>
    /// After naming a first collection the natural next step is pasting a table. Paste Table only becomes enabled once the commands
    /// are asked again (a moment after the collection exists), and a disabled button cannot take focus, so wait for that.
    /// </summary>
    private void OnCollectionCreated(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => Dispatcher.UIThread.Post(() => PasteTableButton.Focus(), DispatcherPriority.Background), DispatcherPriority.Background);
}
