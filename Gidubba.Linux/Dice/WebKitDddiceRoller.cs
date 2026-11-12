using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TableForge.Dice;

/// <summary>
/// The Linux dddice boundary, the counterpart of the Windows build's WebViewDddiceRoller. The session (guest or connected account,
/// room, theme) comes from the shared <see cref="DddiceSessionSource"/>; the dice are drawn by the same <c>dddice-host.html</c> page,
/// in a WebKitGTK window owned by a separate process (<see cref="DddiceHostProgram"/>). The page's messages come back over that
/// process's stdout and are handled exactly as on Windows.
/// Nothing is started until <see cref="PrepareAsync"/>, so a person who stays on Built-in Dice costs nothing here.
/// </summary>
public sealed class WebKitDddiceRoller : IDddiceRoomRoller, IDisposable
{
    private readonly string _webKitFolder;
    private readonly DddiceRest _rest;
    private readonly DddiceSessionSource _sessions;
    private readonly Func<ProcessStartInfo> _hostStart;
    private readonly DddiceRollTracker _tracker = new();
    private readonly object _lock = new();
    private Process? _host;
    private StreamWriter? _commands;
    private TaskCompletionSource? _hostStarted;
    private TaskCompletionSource? _pageReady;
    private TaskCompletionSource? _pageLoaded;
    private DddiceSession? _session;
    private volatile bool _isReady;
    private bool _wantVisible;
    private string _lastHostError = "";

    public TimeSpan PrepareTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RollTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <param name="webKitFolder">Where WebKitGTK keeps the dice window's browser profile. It holds no Gidubba data.</param>
    /// <param name="hostStart">How to start the dice window process (tests). Default: this program with <c>--dddice-host</c>.</param>
    public WebKitDddiceRoller(string webKitFolder, DddiceRest? rest = null, DddiceConnection? connection = null, Func<ProcessStartInfo>? hostStart = null)
    {
        _webKitFolder = webKitFolder;
        _rest = rest ?? new DddiceRest();
        _sessions = new DddiceSessionSource(_rest, connection);
        _hostStart = hostStart ?? DefaultHostStart;
    }

    /// <summary>The dice page shipped beside the program (Assets/dddice-host.html).</summary>
    public static string PagePath => Path.Combine(AppContext.BaseDirectory, "Assets", "dddice-host.html");

    /// <summary>Starts this same program as the dice window. Under <c>dotnet gidubba.dll</c> that means dotnet plus the dll.</summary>
    private ProcessStartInfo DefaultHostStart()
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("The program's own path is not known.");
        var start = new ProcessStartInfo(self);
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add(DddiceHostProgram.Flag);
        start.ArgumentList.Add(PagePath);
        start.ArgumentList.Add(_webKitFolder);
        return start;
    }

    /// <summary>The account or theme changed (or was disconnected): the next preparation starts a fresh page with the new session.</summary>
    public void Reset()
    {
        _isReady = false;
        _session = null;
    }

    /// <summary>Shows or hides the dice window (it is shown only while dddice is the chosen provider).</summary>
    public void SetVisible(bool visible)
    {
        _wantVisible = visible;
        if (_host is not null) Command(new JsonObject { ["op"] = visible ? "show" : "hide" });
    }

    /// <summary>Brings the dice window to the front (Show Dice).</summary>
    public void Present()
    {
        _wantVisible = true;
        if (_host is not null) Command(new JsonObject { ["op"] = "present" });
    }

    public bool IsWindowAvailable => _host is not null;

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (_isReady) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PrepareTimeout);
            try
            {
                _session = null;
                _session = await _sessions.GetAsync(timeout.Token);
                await EnsureHostAsync(timeout.Token);
                await LoadPageAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DddiceException("dddice did not become ready in time. Check the internet connection.");
            }
            _isReady = true;
        }
        catch (DddiceException ex) when (ex.IsAuthProblem)
        {
            _sessions.AuthFailed(_session); // a refused guest or room is replaced by a new one on the next try; an account is marked expired
            _session = null;
            throw;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or DddiceException))
        {
            throw new DddiceException("The dddice dice could not be started: " + ex.Message, inner: ex);
        }
    }

    private async Task EnsureHostAsync(CancellationToken ct)
    {
        if (_host is { HasExited: false }) return;
        StopHost();

        if (!File.Exists(PagePath)) throw new DddiceException("The dddice dice page (Assets/dddice-host.html) is missing from the Gidubba folder.");

        var start = _hostStart();
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        Directory.CreateDirectory(_webKitFolder);

        _lastHostError = "";
        _hostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new Process { StartInfo = start, EnableRaisingEvents = true };
        host.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) OnHostLine(line); };
        host.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) _lastHostError = line; }; // GTK/WebKit chatter; kept for an error message
        host.Exited += (_, _) => OnHostExited(host);
        if (!host.Start()) throw new DddiceException("The dddice dice window could not be started.");
        _host = host;
        _commands = host.StandardInput;
        _commands.AutoFlush = true;
        _commands.NewLine = "\n";
        host.BeginOutputReadLine();
        host.BeginErrorReadLine();

        await _hostStarted.Task.WaitAsync(ct);
    }

    private async Task LoadPageAsync(CancellationToken ct)
    {
        _pageLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pageReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Visible before the page starts: the dice canvas takes its size from the window, and a hidden page does not animate.
        Command(new JsonObject { ["op"] = _wantVisible ? "present" : "hide" });
        Command(new JsonObject { ["op"] = "load" }); // a fresh page (and so a fresh dddice-js) for every preparation
        await _pageLoaded.Task.WaitAsync(ct);
        var session = _session!;
        Eval($"window.tf.init({JsonSerializer.Serialize(session.Token)}, {JsonSerializer.Serialize(session.Room)}, {JsonSerializer.Serialize(session.Theme)})");
        await _pageReady.Task.WaitAsync(ct);
    }

    public async Task<IReadOnlyList<DddiceFace>> RollAsync(IReadOnlyList<string> diceTypes, CancellationToken cancellationToken)
    {
        if (!_isReady || _host is null || _session is not { } session) throw new DddiceException("dddice is not ready.");

        var externalId = Guid.NewGuid().ToString("N");
        Task<IReadOnlyList<DddiceFace>> settled;
        lock (_lock) settled = _tracker.Begin(externalId);
        try
        {
            var dice = new JsonArray(diceTypes.Select(d => (JsonNode)new JsonObject { ["type"] = d, ["theme"] = session.Theme }).ToArray());
            Command(new JsonObject { ["op"] = "show" }); // the dice only tumble (and finish) in a visible window
            Eval("window.tf.clear()"); // sweep the previous roll's dice off the tray
            Eval($"window.tf.roll({dice.ToJsonString()}, '{externalId}')");
            return await settled.WaitAsync(RollTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _isReady = false;
            throw new DddiceException($"The dddice roll did not finish within {RollTimeout.TotalSeconds:0} seconds.");
        }
        catch (DddiceException)
        {
            _isReady = false; // whatever went wrong, the next attempt starts from a fresh page
            throw;
        }
        finally { lock (_lock) _tracker.Abandon(); }
    }

    private void Eval(string script) => Command(new JsonObject { ["op"] = "eval", ["js"] = script });

    private void Command(JsonObject command)
    {
        try { _commands?.WriteLine(command.ToJsonString()); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _isReady = false; // the dice window is gone; OnHostExited reports it
        }
    }

    private void OnHostLine(string json)
    {
        string? kind;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            kind = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("kind", out var k) ? k.GetString() : null;
        }
        catch (JsonException) { return; }
        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

        switch (kind)
        {
            case "hostStarted": _hostStarted?.TrySetResult(); break;
            case "hostError":
                var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
                _hostStarted?.TrySetException(new DddiceException(reason switch
                {
                    "missingLibrary" => "dddice's 3D dice need WebKitGTK 6.0, which is not installed (" + message + " is missing). " +
                                        "Install the webkitgtk-6.0 package (libwebkitgtk-6.0-4 on Debian and Ubuntu), or keep using Built-in Dice.",
                    "noDisplay" => "The dddice dice window could not open on this display.",
                    _ => "The dddice dice window could not start: " + message,
                }));
                break;
            case "pageLoaded": _pageLoaded?.TrySetResult(); break;
            case "loadFailed": _pageLoaded?.TrySetException(new DddiceException("The dddice dice page could not be loaded.")); break;
            case "ready": _pageReady?.TrySetResult(); break;
            case "sdkLoadFailed":
                _pageReady?.TrySetException(new DddiceException("The dddice dice could not be downloaded from dddice.com. Check the internet connection."));
                break;
            case "initFailed":
                var auth = message.Contains("401") || message.Contains("403");
                var account = _session?.IsAccount ?? false;
                _pageReady?.TrySetException(new DddiceException(auth
                    ? account ? DddiceMessages.Expired : "dddice refused the guest login for its room."
                    : "Gidubba could not connect to dddice's room. Check the internet connection.", auth) { IsAccountProblem = auth && account });
                break;
            case "connectionState" when root.TryGetProperty("state", out var s) && s.GetString() is "unavailable" or "failed":
                lock (_lock) if (_tracker.IsRolling) _tracker.Fail("The connection to dddice was lost during the roll.");
                _isReady = false;
                break;
            case "pageCrashed":
                PageGone("The dddice dice page stopped unexpectedly.");
                break;
            case "windowClosed":
                break; // only hidden; the next roll shows it again
            default:
                lock (_lock) _tracker.OnPageMessage(json);
                break;
        }
    }

    private void OnHostExited(Process host)
    {
        if (!ReferenceEquals(host, _host)) return;
        var detail = _lastHostError.Length > 0 ? $" ({_lastHostError})" : "";
        _hostStarted?.TrySetException(new DddiceException("The dddice dice window could not start" + detail + "."));
        PageGone("The dddice dice window closed unexpectedly.");
        _host = null;
        _commands = null;
    }

    private void PageGone(string message)
    {
        _isReady = false;
        _pageLoaded?.TrySetException(new DddiceException(message));
        _pageReady?.TrySetException(new DddiceException(message));
        lock (_lock) if (_tracker.IsRolling) _tracker.Fail(message);
    }

    private void StopHost()
    {
        var host = _host;
        _host = null;
        if (host is null) return;
        try { _commands?.Close(); } catch (IOException) { /* already gone */ }
        _commands = null;
        try
        {
            if (!host.WaitForExit(2000)) host.Kill();
        }
        catch (InvalidOperationException) { /* never started or already reaped */ }
        host.Dispose();
    }

    public void Dispose()
    {
        StopHost();
        _rest.Dispose();
    }
}
