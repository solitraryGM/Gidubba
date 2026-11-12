using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static TableForge.Dice.NativeWebKit;

namespace TableForge.Dice;

/// <summary>
/// The dddice dice window: Gidubba started again as <c>gidubba --dddice-host &lt;page&gt; &lt;webkit-data-folder&gt;</c>. It shows
/// <c>dddice-host.html</c> (the same page the Windows build shows in WebView2) in a WebKitGTK view in its own GTK 4 window.
///
/// Talking to Gidubba is one JSON object per line. Gidubba writes commands to stdin:
/// <c>{"op":"load"}</c> (a fresh page), <c>{"op":"eval","js":"…"}</c>, <c>{"op":"show"}</c>, <c>{"op":"present"}</c>, <c>{"op":"hide"}</c>;
/// closing stdin ends the process. Every message the page posts comes back on stdout unchanged, plus the host's own:
/// <c>hostStarted</c>, <c>hostError</c>, <c>loadFailed</c>, <c>pageCrashed</c>, <c>windowClosed</c>.
///
/// The page calls <c>window.chrome.webview.postMessage</c> as on Windows; a small script injected before the page runs maps that onto
/// WebKit's script message handler, so the page itself is not changed. Like the Windows build, the page is given the origin
/// https://tableforge-app.local/ and may not navigate away from it or open windows. The dddice token reaches the page only inside an
/// <c>eval</c> command on stdin: never on the command line, in a file, or on stdout.
/// </summary>
internal static unsafe class DddiceHostProgram
{
    public const string Flag = "--dddice-host";
    public const string Origin = "https://tableforge-app.local/";
    private const string PageUri = Origin + "Assets/dddice-host.html";
    private const string HandlerName = "gidubba";

    private const string BridgeScript =
        "window.chrome = window.chrome || {};" +
        "window.chrome.webview = { postMessage: m => window.webkit.messageHandlers." + HandlerName + ".postMessage(m) };";

    private static readonly ConcurrentQueue<JsonObject?> Commands = new(); // null = stdin closed: quit
    private static readonly object OutLock = new();
    private static TextWriter _out = TextWriter.Null;
    private static IntPtr _loop, _window, _view;
    private static string _pagePath = "";
    private static int _idleQueued;

    public static int Run(string[] args)
    {
        _out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        if (args.Length < 3)
        {
            Send(new JsonObject { ["kind"] = "hostError", ["message"] = "usage: gidubba --dddice-host <page> <data-folder>" });
            return 2;
        }
        _pagePath = args[1];
        var dataFolder = args[2];

        if (!TryLoadAll(out var missing))
        {
            Send(new JsonObject { ["kind"] = "hostError", ["reason"] = "missingLibrary", ["message"] = missing });
            return 3;
        }

        g_set_prgname("gidubba");
        if (gtk_init_check() == 0)
        {
            Send(new JsonObject { ["kind"] = "hostError", ["reason"] = "noDisplay", ["message"] = "GTK could not open the display." });
            return 4;
        }

        CreateWindow(dataFolder);
        _loop = g_main_loop_new(IntPtr.Zero, 0);
        new Thread(ReadCommands) { IsBackground = true, Name = "dddice-host stdin" }.Start();
        Send(new JsonObject { ["kind"] = "hostStarted" });
        g_main_loop_run(_loop);
        return 0;
    }

    private static void CreateWindow(string dataFolder)
    {
        // The browser profile (cache, cookies, dddice-js's own storage) lives in Gidubba's data folder, like the WebView2 profile on
        // Windows. It holds no Gidubba data.
        var session = webkit_network_session_new(Path.Combine(dataFolder, "data"), Path.Combine(dataFolder, "cache"));

        // webkit_web_view_new() would use the default session (stored under the program name); this one gives it ours.
        GValue value = default;
        g_value_init(&value, GTypeObject);
        g_value_set_object(&value, session);
        var name = Marshal.StringToCoTaskMemUTF8("network-session");
        try { _view = g_object_new_with_properties(webkit_web_view_get_type(), 1, &name, &value); }
        finally { Marshal.FreeCoTaskMem(name); g_value_unset(&value); g_object_unref(session); }

        var background = new GdkRGBA { Red = 0x24 / 255f, Green = 0x3B / 255f, Blue = 0x2C / 255f, Alpha = 1 }; // the Windows dice panel's green
        webkit_web_view_set_background_color(_view, &background);

        var content = webkit_web_view_get_user_content_manager(_view);
        var bridge = webkit_user_script_new(BridgeScript, InjectTopFrame, InjectAtDocumentStart, IntPtr.Zero, IntPtr.Zero);
        webkit_user_content_manager_add_script(content, bridge);
        webkit_user_script_unref(bridge);
        webkit_user_content_manager_register_script_message_handler(content, HandlerName, IntPtr.Zero);

        Connect(content, "script-message-received::" + HandlerName, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScriptMessage);
        Connect(_view, "decide-policy", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int>)&OnDecidePolicy);
        Connect(_view, "context-menu", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, int>)&OnContextMenu);
        Connect(_view, "load-failed", (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, IntPtr, int>)&OnLoadFailed);
        Connect(_view, "web-process-terminated", (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr, void>)&OnWebProcessTerminated);

        _window = gtk_window_new();
        gtk_window_set_title(_window, "Gidubba — dddice dice");
        gtk_window_set_icon_name(_window, "gidubba");
        gtk_window_set_default_size(_window, 900, 280);
        gtk_window_set_child(_window, _view);
        Connect(_window, "close-request", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&OnCloseRequest);
        MarkAsUtilityWindow();
    }

    /// <summary>
    /// On X11, marks the dice window as a utility window (_NET_WM_WINDOW_TYPE_UTILITY) before it is first shown, so tiling window
    /// managers such as i3 float it at its own size instead of tiling it full height. GTK 4 no longer offers this, so it is set
    /// with Xlib. On Wayland (or if anything is missing) the window is simply a normal one.
    /// </summary>
    private static void MarkAsUtilityWindow()
    {
        try
        {
            gtk_widget_realize(_window);
            var surface = gtk_native_get_surface(_window);
            var display = surface == IntPtr.Zero ? IntPtr.Zero : gdk_surface_get_display(surface);
            if (display == IntPtr.Zero || g_type_check_instance_is_a(display, gdk_x11_display_get_type()) == 0) return;

            var xdisplay = gdk_x11_display_get_xdisplay(display);
            var xid = gdk_x11_surface_get_xid(surface);
            var utility = XInternAtom(xdisplay, "_NET_WM_WINDOW_TYPE_UTILITY", 0);
            XChangeProperty(xdisplay, xid, XInternAtom(xdisplay, "_NET_WM_WINDOW_TYPE", 0), XaAtom, 32, PropModeReplace, &utility, 1);
            XFlush(xdisplay);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"dddice host: could not mark the window as a utility window: {ex.Message}");
        }
    }

    private static void Connect(IntPtr instance, string signal, IntPtr handler) =>
        g_signal_connect_data(instance, signal, handler, IntPtr.Zero, IntPtr.Zero, 0);

    // ---- stdin -> GTK thread ----------------------------------------------------------------------------------------

    private static void ReadCommands()
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        try
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                try { Commands.Enqueue(JsonNode.Parse(line) as JsonObject); }
                catch (JsonException) { continue; }
                Wake();
            }
        }
        catch (IOException) { /* Gidubba went away */ }
        Commands.Enqueue(null);
        Wake();
    }

    /// <summary>g_idle_add is safe from any thread: the GTK thread drains the queue on its next turn.</summary>
    private static void Wake()
    {
        if (Interlocked.Exchange(ref _idleQueued, 1) == 0) g_idle_add(&OnIdle, IntPtr.Zero);
    }

    [UnmanagedCallersOnly]
    private static int OnIdle(IntPtr data)
    {
        Volatile.Write(ref _idleQueued, 0);
        while (Commands.TryDequeue(out var command))
        {
            if (command is null) { g_main_loop_quit(_loop); return 0; }
            try { Execute(command); }
            catch (Exception ex) { Console.Error.WriteLine($"dddice host: {ex.Message}"); }
        }
        return 0; // G_SOURCE_REMOVE
    }

    private static void Execute(JsonObject command)
    {
        switch ((string?)command["op"])
        {
            case "load":
                // A fresh page (and so a fresh dddice-js) each time, given the same https origin the Windows build uses.
                string html;
                try { html = File.ReadAllText(_pagePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Send(new JsonObject { ["kind"] = "loadFailed", ["message"] = ex.Message });
                    return;
                }
                webkit_web_view_load_html(_view, html, PageUri);
                break;
            case "eval" when command["js"] is JsonValue js && js.TryGetValue<string>(out var script):
                webkit_web_view_evaluate_javascript(_view, script, -1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                break;
            case "show":
                // A roll is about to be thrown: the dice only animate in a visible window. Shown without taking focus from Gidubba
                // when it is already open.
                if (gtk_widget_get_visible(_window) == 0) gtk_window_present(_window);
                break;
            case "present":
                gtk_window_present(_window);
                break;
            case "hide":
                gtk_widget_set_visible(_window, 0);
                break;
        }
    }

    // ---- WebKit / GTK signals ---------------------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(IntPtr manager, IntPtr jsValue, IntPtr data)
    {
        // The page posts plain objects; their JSON form is what WebView2's WebMessageAsJson gives the Windows build.
        var json = TakeUtf8(jsc_value_to_json(jsValue, 0));
        if (json is null) return;
        lock (OutLock) _out.WriteLine(json);
    }

    [UnmanagedCallersOnly]
    private static int OnDecidePolicy(IntPtr view, IntPtr decision, int type, IntPtr data)
    {
        if (type == PolicyNewWindowAction) { webkit_policy_decision_ignore(decision); return 1; } // the page never opens windows
        if (type != PolicyNavigationAction) return 0;

        var action = webkit_navigation_policy_decision_get_navigation_action(decision);
        var uri = Utf8(webkit_uri_request_get_uri(webkit_navigation_action_get_request(action))) ?? "";
        if (uri.StartsWith(Origin, StringComparison.Ordinal) || uri.StartsWith("about:", StringComparison.Ordinal)) return 0;

        Console.Error.WriteLine($"dddice host: blocked navigation to {uri}");
        webkit_policy_decision_ignore(decision); // and never leaves its own page
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnContextMenu(IntPtr view, IntPtr menu, IntPtr hitTest, IntPtr data) => 1; // no right-click menu

    [UnmanagedCallersOnly]
    private static int OnLoadFailed(IntPtr view, int loadEvent, IntPtr failingUri, IntPtr error, IntPtr data)
    {
        // GError: { GQuark domain; gint code; gchar *message; }. A load replaced by a newer one is not a failure.
        var code = error == IntPtr.Zero ? 0 : Marshal.ReadInt32(error, 4);
        if (code == NetworkErrorCancelled) return 0;
        var message = error == IntPtr.Zero ? "" : Utf8(Marshal.ReadIntPtr(error, 8)) ?? "";
        Send(new JsonObject { ["kind"] = "loadFailed", ["message"] = message });
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(IntPtr view, int reason, IntPtr data) =>
        Send(new JsonObject { ["kind"] = "pageCrashed", ["reason"] = reason });

    [UnmanagedCallersOnly]
    private static int OnCloseRequest(IntPtr window, IntPtr data)
    {
        // Closing the window only hides it: the page and its dddice connection stay ready. Gidubba shows it again for the next roll.
        gtk_widget_set_visible(_window, 0);
        Send(new JsonObject { ["kind"] = "windowClosed" });
        return 1;
    }

    private static void Send(JsonObject message)
    {
        var line = message.ToJsonString();
        lock (OutLock) _out.WriteLine(line);
    }
}
