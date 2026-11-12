# Gidubba for Linux (Avalonia port) — first cut

Native Linux port of Gidubba (formerly TableForge) using Avalonia UI. The
non-UI code (Domain, Import, Export, Data, Portable, Streaming, ViewModels,
Dice) is **linked from ../TableForge**, not copied, so fixes there apply to
both builds. Only the UI and a few platform shims live in `Gidubba.Linux/`.

## Build
Requires the .NET 10 SDK. From this folder:

    dotnet build                      # debug build
    dotnet run                        # run it
    ./packaging/build.sh              # self-contained publish -> packaging/publish
    ./packaging/install.sh            # install to ~/.local + app-menu entry
    ./packaging/uninstall.sh

## Status
Builds cleanly and starts on Arch (.NET 10 SDK, Avalonia 11.3). dddice guest rolls have been checked end to end
(prepare, 2d6+1, d20, d100) and the keyring store round-trips. dddice rolls appear in a floating window.

Highest-risk spots:
- `Platform/UiSync.cs` — nested `Dispatcher.UIThread.MainLoop` used so the
  shared view models' synchronous dialog callbacks work.
- Review screen: source-text highlighting may not show/scroll when the box is
  unfocused; scroll sync between aligned header/rows.
- Key handling (Ctrl+Enter, Ctrl+J/B/I/Space, Esc) uses tunnel KeyDown handlers.

## Differences from Windows
- **dddice 3D dice** roll in their own window ("Gidubba — dddice dice") instead of a panel inside the main window. It is
  WebKitGTK 6.0 (package `webkitgtk-6.0`; `libwebkitgtk-6.0-4` on Debian/Ubuntu), run as a separate process
  (`gidubba --dddice-host …`, see `Dice/DddiceHostProgram.cs`) because GTK 4 cannot share a process with the GTK 3 that
  Avalonia may load for dialogs. It shows the same `dddice-host.html` page as Windows. Closing the window just hides it;
  the next roll or **Show Dice** brings it back. On X11 it is marked as a utility window, so tiling window
  managers (i3 etc.) float it at 900x280; on Wayland compositors add a rule for the title "Gidubba — dddice dice". Without WebKitGTK, choosing dddice says what to install; Built-in dice
  always work.
- **dddice account login** is kept in the desktop keyring (Secret Service: GNOME Keyring, KWallet…) via `secret-tool`
  (libsecret), not DPAPI. `dddice-account.json` holds only the name/room/theme. An account file copied from Windows
  shows as "Connection expired": reconnect once.
- `--clipboard-diagnostic` mode omitted.
- Clipboard, file pickers and "open folder/URL" use Avalonia's StorageProvider
  and `xdg-open`.
- Data lives in `~/.local/share/TableForge` (override: `TABLEFORGE_DATA_DIR`);
  the database format is the same as Windows, so you can copy it across.
