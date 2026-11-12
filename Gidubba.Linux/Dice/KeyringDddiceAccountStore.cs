using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TableForge.Dice;

/// <summary>
/// The Linux home of a connected dddice account. Like the Windows store it is <c>&lt;data folder&gt;/dddice-account.json</c>, but
/// the token is not in the file at all: it is kept in the desktop keyring (the freedesktop Secret Service: GNOME Keyring, KWallet,
/// KeePassXC…) through <c>secret-tool</c>, which comes with libsecret. The file holds only the non-secret parts (name, room, theme).
/// The keyring entry is keyed by the file's path, so TABLEFORGE_DATA_DIR keeps separate accounts apart, as on Windows.
/// With no keyring available an account cannot be saved (Connect reports it); guest dice are unaffected.
/// A Windows account file (DPAPI-protected) cannot be read here and shows as expired: reconnect. Windows likewise reads this file
/// as unreadable, because it has no protectedToken.
/// </summary>
public sealed class KeyringDddiceAccountStore(string dataFolder) : IDddiceAccountStore
{
    public const string FileName = DddiceAccountStore.FileName;
    private const int Format = 1;
    private static readonly TimeSpan KeyringTimeout = TimeSpan.FromSeconds(60); // long enough to answer a keyring unlock prompt

    public string FilePath { get; } = Path.Combine(dataFolder, FileName);

    private string? _storedToken; // what the keyring holds, so a theme or room change does not rewrite it

    public DddiceAccountLoad Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new(DddiceAccountFileState.None);
            var json = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
            if (json is null || (int?)json["format"] != Format || Text(json["tokenStore"]) != "keyring")
                return new(DddiceAccountFileState.Unreadable); // includes a file copied from Windows

            var (code, token) = SecretTool(["lookup", .. Attributes()], stdin: null);
            if (code != 0 || token.Length == 0) return new(DddiceAccountFileState.Unreadable);
            _storedToken = token;

            return new(DddiceAccountFileState.Loaded, new DddiceAccount(token,
                Text(json["displayName"]), Text(json["roomSlug"]), Text(json["themeId"]), Text(json["themeName"])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                       or ArgumentException or Win32Exception or TimeoutException)
        {
            return new(DddiceAccountFileState.Unreadable); // corrupt, keyring locked or missing: reconnect, never crash
        }
    }

    public void Save(DddiceAccount account)
    {
        if (account.Token != _storedToken)
        {
            int code;
            try { (code, _) = SecretTool(["store", "--label=Gidubba dddice account", .. Attributes()], stdin: account.Token); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
            {
                throw new IOException("Gidubba keeps your dddice login in the desktop keyring, but could not reach it (secret-tool, from libsecret, " +
                                      "is needed). Built-in Dice and dddice guest dice still work.", ex);
            }
            if (code != 0)
                throw new IOException("Gidubba could not save your dddice login in the desktop keyring. Check that a keyring " +
                                      "(GNOME Keyring or KWallet) is running and unlocked, then connect again.");
            _storedToken = account.Token;
        }

        var json = new JsonObject
        {
            ["format"] = Format,
            ["tokenStore"] = "keyring",
            ["displayName"] = account.DisplayName,
            ["roomSlug"] = account.RoomSlug,
            ["themeId"] = account.ThemeId,
            ["themeName"] = account.ThemeName,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }

    public void Delete()
    {
        try { SecretTool(["clear", .. Attributes()], stdin: null); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException) { /* nothing reachable to clear */ }
        _storedToken = null;
        if (File.Exists(FilePath)) File.Delete(FilePath);
        if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp");
    }

    private string[] Attributes() => ["application", "gidubba", "purpose", "dddice-account-token", "file", FilePath];

    /// <summary>Runs secret-tool. The token only ever travels on stdin/stdout, never on a command line.</summary>
    private static (int Code, string Output) SecretTool(string[] args, string? stdin)
    {
        var start = new ProcessStartInfo("secret-tool")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) start.ArgumentList.Add(a);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("secret-tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (stdin is not null) process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        if (!process.WaitForExit(KeyringTimeout))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new TimeoutException("The keyring did not answer.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult());
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
