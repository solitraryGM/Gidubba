using System.Runtime.InteropServices;

namespace TableForge.Dice;

/// <summary>
/// The few GLib / GTK 4 / WebKitGTK 6.0 functions the dddice dice window uses. Only ever called inside the separate dice-host
/// process (see <see cref="DddiceHostProgram"/>): GTK 4 must never be loaded into the Avalonia process, which may load GTK 3
/// for its own dialogs, and the two cannot live in one process.
/// </summary>
internal static unsafe partial class NativeWebKit
{
    public const string GLib = "libglib-2.0.so.0";
    public const string GObject = "libgobject-2.0.so.0";
    public const string Gtk = "libgtk-4.so.1";
    public const string WebKit = "libwebkitgtk-6.0.so.4";
    public const string Jsc = "libjavascriptcoregtk-6.0.so.1";

    // ---- GLib / GObject ---------------------------------------------------------------------------------------------
    [LibraryImport(GLib)] public static partial IntPtr g_main_loop_new(IntPtr context, int isRunning);
    [LibraryImport(GLib)] public static partial void g_main_loop_run(IntPtr loop);
    [LibraryImport(GLib)] public static partial void g_main_loop_quit(IntPtr loop);
    [LibraryImport(GLib)] public static partial uint g_idle_add(delegate* unmanaged<IntPtr, int> function, IntPtr data);
    [LibraryImport(GLib)] public static partial void g_free(IntPtr mem);
    [LibraryImport(GLib, StringMarshalling = StringMarshalling.Utf8)] public static partial void g_set_prgname(string name);

    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    public static partial ulong g_signal_connect_data(IntPtr instance, string detailedSignal, IntPtr handler, IntPtr data, IntPtr destroyData, int connectFlags);

    [LibraryImport(GObject)] public static partial void g_value_init(GValue* value, IntPtr gType);
    [LibraryImport(GObject)] public static partial void g_value_set_object(GValue* value, IntPtr obj);
    [LibraryImport(GObject)] public static partial void g_value_unset(GValue* value);
    [LibraryImport(GObject)] public static partial IntPtr g_object_new_with_properties(IntPtr objectType, uint nProperties, IntPtr* names, GValue* values);
    [LibraryImport(GObject)] public static partial void g_object_unref(IntPtr obj);

    /// <summary>G_TYPE_OBJECT (fundamental type 20, shifted by G_TYPE_FUNDAMENTAL_SHIFT).</summary>
    public static readonly IntPtr GTypeObject = 20 << 2;

    /// <summary>GValue: a GType and a two-word data union (24 bytes on 64-bit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GValue { public IntPtr GType; public long Data0; public long Data1; }

    // ---- GTK 4 ------------------------------------------------------------------------------------------------------
    [LibraryImport(Gtk)] public static partial int gtk_init_check();
    [LibraryImport(Gtk)] public static partial IntPtr gtk_window_new();
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)] public static partial void gtk_window_set_title(IntPtr window, string title);
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)] public static partial void gtk_window_set_icon_name(IntPtr window, string name);
    [LibraryImport(Gtk)] public static partial void gtk_window_set_default_size(IntPtr window, int width, int height);
    [LibraryImport(Gtk)] public static partial void gtk_window_set_child(IntPtr window, IntPtr child);
    [LibraryImport(Gtk)] public static partial void gtk_window_present(IntPtr window);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_visible(IntPtr widget, int visible);
    [LibraryImport(Gtk)] public static partial int gtk_widget_get_visible(IntPtr widget);

    [LibraryImport(Gtk)] public static partial void gtk_widget_realize(IntPtr widget);
    [LibraryImport(Gtk)] public static partial IntPtr gtk_native_get_surface(IntPtr native);
    [LibraryImport(Gtk)] public static partial IntPtr gdk_surface_get_display(IntPtr surface);
    [LibraryImport(Gtk)] public static partial IntPtr gdk_x11_display_get_type();
    [LibraryImport(Gtk)] public static partial IntPtr gdk_x11_display_get_xdisplay(IntPtr display);
    [LibraryImport(Gtk)] public static partial nuint gdk_x11_surface_get_xid(IntPtr surface);
    [LibraryImport(GObject)] public static partial int g_type_check_instance_is_a(IntPtr instance, IntPtr type);

    // ---- Xlib (only on X11, only to mark the dice window as a utility window) ---------------------------------------
    public const string X11 = "libX11.so.6";
    [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)] public static partial nuint XInternAtom(IntPtr display, string name, int onlyIfExists);
    [LibraryImport(X11)] public static partial int XChangeProperty(IntPtr display, nuint window, nuint property, nuint type, int format, int mode, nuint* data, int count);
    [LibraryImport(X11)] public static partial int XFlush(IntPtr display);
    public const int XaAtom = 4;            // XA_ATOM
    public const int PropModeReplace = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct GdkRGBA { public float Red, Green, Blue, Alpha; }

    // ---- WebKitGTK 6.0 ----------------------------------------------------------------------------------------------
    [LibraryImport(WebKit)] public static partial IntPtr webkit_web_view_get_type();
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr webkit_network_session_new(string dataDirectory, string cacheDirectory);
    [LibraryImport(WebKit)] public static partial IntPtr webkit_web_view_get_user_content_manager(IntPtr webView);
    [LibraryImport(WebKit)] public static partial void webkit_web_view_set_background_color(IntPtr webView, GdkRGBA* rgba);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] public static partial void webkit_web_view_load_html(IntPtr webView, string content, string baseUri);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_evaluate_javascript(IntPtr webView, string script, nint length, IntPtr worldName, IntPtr sourceUri,
        IntPtr cancellable, IntPtr callback, IntPtr userData);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(IntPtr manager, string name, IntPtr worldName);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr webkit_user_script_new(string source, int injectedFrames, int injectionTime, IntPtr allowList, IntPtr blockList);
    [LibraryImport(WebKit)] public static partial void webkit_user_content_manager_add_script(IntPtr manager, IntPtr script);
    [LibraryImport(WebKit)] public static partial void webkit_user_script_unref(IntPtr script);

    [LibraryImport(WebKit)] public static partial IntPtr webkit_navigation_policy_decision_get_navigation_action(IntPtr decision);
    [LibraryImport(WebKit)] public static partial IntPtr webkit_navigation_action_get_request(IntPtr action);
    [LibraryImport(WebKit)] public static partial IntPtr webkit_uri_request_get_uri(IntPtr request);
    [LibraryImport(WebKit)] public static partial void webkit_policy_decision_ignore(IntPtr decision);

    public const int InjectTopFrame = 1;            // WEBKIT_USER_CONTENT_INJECT_TOP_FRAME
    public const int InjectAtDocumentStart = 0;     // WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START
    public const int PolicyNavigationAction = 0;    // WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION
    public const int PolicyNewWindowAction = 1;     // WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION
    public const int NetworkErrorCancelled = 302;   // WEBKIT_NETWORK_ERROR_CANCELLED

    // ---- JavaScriptCore ---------------------------------------------------------------------------------------------
    [LibraryImport(Jsc)] public static partial IntPtr jsc_value_to_json(IntPtr value, uint indent);

    /// <summary>A UTF-8 C string owned by someone else (not freed).</summary>
    public static string? Utf8(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

    /// <summary>A UTF-8 C string the caller owns: read, then g_free.</summary>
    public static string? TakeUtf8(IntPtr p)
    {
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { g_free(p); }
    }

    /// <summary>True when every library the dice window needs can be loaded; otherwise the first one that cannot.</summary>
    public static bool TryLoadAll(out string missing)
    {
        foreach (var lib in new[] { GLib, GObject, Gtk, WebKit, Jsc })
        {
            if (!NativeLibrary.TryLoad(lib, out _)) { missing = lib; return false; }
        }
        missing = "";
        return true;
    }
}
