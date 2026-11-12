using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Streaming;
using TableForge.ViewModels;

namespace TableForge;

public partial class App : Application
{
    private AppDatabase? _db;
    private WebKitDddiceRoller? _dddice;

    // The Streaming Overlay's current result: in memory only, so every run starts with an empty overlay.
    private readonly OverlayPublisher _overlay = new();
    private StreamingOverlayController? _overlayController;

    /// <summary>The main window, for dialogs that need an owner (file pickers, message boxes, the clipboard).</summary>
    public static MainWindow? MainWindow { get; private set; }

    /// <summary>The dddice dice window's owner, for Show Dice. Null until the app has started.</summary>
    public static WebKitDddiceRoller? DddiceRoller { get; private set; }

    private static WindowIcon? _icon;

    /// <summary>The window icon, or null if it cannot be loaded (never a reason to stop).</summary>
    public static WindowIcon? WindowIcon
    {
        get
        {
            if (_icon is not null) return _icon;
            try { return _icon = new WindowIcon(AssetLoader.Open(new Uri("avares://gidubba/Assets/gidubba-256.png"))); }
            catch (Exception) { return null; }
        }
    }

    // The dice choice is one word in a small file next to the database (no schema change for a single setting).
    private static string DataFolder => Path.GetDirectoryName(AppDatabase.DefaultPath)!;
    private static string PreferencePath => Path.Combine(DataFolder, "dice-provider.txt");
    private static string AutoFollowPath => Path.Combine(DataFolder, "auto-follow.txt");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartMainWindow(desktop);
            desktop.Exit += (_, _) =>
            {
                _overlayController?.Dispose();
                _dddice?.Dispose();
                _db?.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void StartMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var path = AppDatabase.DefaultPath;
        try
        {
            _db = new AppDatabase(path); // creates the folder and database on first run, and upgrades older ones
            var window = new MainWindow();
            MainWindow = window;

            // Built-in is the default and does no network work. dddice is only prepared once it has been chosen (or was chosen last time).
            // A connected dddice account is read from its local file (token from the keyring) here, but only checked with dddice
            // when dddice is prepared. dddice's dice tumble in their own WebKitGTK window, shown only while dddice is chosen.
            var rest = new DddiceRest();
            var connection = new DddiceConnection(new KeyringDddiceAccountStore(DataFolder), new DddiceRoomMemoryStore(DataFolder));
            _dddice = DddiceRoller = new WebKitDddiceRoller(Path.Combine(DataFolder, "WebKit"), rest, connection);
            var dice = new DiceProviderViewModel(new BuiltInDiceProvider(), new DddiceDiceProvider(_dddice), LoadDicePreference, SaveDicePreference,
                connection, openAccount: () => Dialogs.ShowModal(new DddiceAccountWindow
                {
                    DataContext = new DddiceAccountViewModel(connection, rest, CopyText, OpenInBrowser),
                }));
            dice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DiceProviderViewModel.ShowDicePanel)) _dddice.SetVisible(dice.ShowDicePanel);
            };

            var main = new MainViewModel(_db, dice,
                confirm: (message, title) => Dialogs.Confirm(message, title),
                confirmImport: message => Dialogs.Confirm(message, "Import Collection", "OK", "Cancel"),
                showMessage: message => Dialogs.Info(message),
                dataFolder: DataFolder, openFolder: OpenFolder,
                showAbout: () => Dialogs.ShowModal(new AboutWindow
                {
                    DataContext = new AboutViewModel(AppInfo.Version, AppContext.BaseDirectory, DataFolder, OpenWithDesktop),
                }),
                overlay: _overlay,
                reviewPumExport: review =>
                {
                    var dialog = new PumExportReviewWindow { DataContext = review };
                    Dialogs.ShowModal(dialog);
                    return dialog.Saved;
                },
                showStreamingOverlay: () => Dialogs.ShowModal(new StreamingOverlayWindow
                {
                    DataContext = new StreamingOverlayViewModel(_overlayController!, connection, CopyText, OpenInBrowser),
                }),
                autoFollow: new AutoFollowSetting(LoadAutoFollow(), SaveAutoFollow),
                openWebPage: OpenInBrowser);

            // The Streaming Overlay listens (on 127.0.0.1 only) just while it is enabled; a port problem is shown in its dialog.
            _overlayController = new StreamingOverlayController(_overlay, DataFolder);
            _overlayController.Start();

            window.DataContext = main;
            desktop.MainWindow = window;
            dice.RestorePreference();
        }
        catch (Exception ex)
        {
            _db?.Dispose(); // don't hold the file open after a failed start
            _db = null;
            var error = new MessageWindow(AppInfo.Title, DatabaseOpenError.Describe(path, ex), "OK", null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            error.Closed += (_, _) => desktop.Shutdown(1);
            desktop.MainWindow = error;
        }
    }

    private static void CopyText(string text) => ClipboardText.Set(text);

    /// <summary>Shows a folder in the file manager (Open Data Folder).</summary>
    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Shell.Open(folder);
    }

    /// <summary>Opens a file (the default text editor for .txt) or folder, for About. A failure is shown, never thrown.</summary>
    private static void OpenWithDesktop(string path)
    {
        try { Shell.Open(path); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Dialogs.Info($"Gidubba could not open {path}: {ex.Message}");
        }
    }

    /// <summary>Opens a page in the person's default browser.</summary>
    private static void OpenInBrowser(string url)
    {
        try { Shell.Open(url); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Dialogs.Info($"Gidubba could not open your web browser. Go to {url} yourself.");
        }
    }

    private static string? LoadDicePreference() =>
        File.Exists(PreferencePath) ? File.ReadAllText(PreferencePath).Trim() : null;

    private static void SaveDicePreference(string value)
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(PreferencePath, value);
    }

    /// <summary>Auto-follow linked tables is on only when auto-follow.txt says "on"; missing or unreadable is off.</summary>
    private static bool LoadAutoFollow()
    {
        try { return File.Exists(AutoFollowPath) && File.ReadAllText(AutoFollowPath).Trim() == "on"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void SaveAutoFollow(bool on)
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(AutoFollowPath, on ? "on" : "off");
    }
}
