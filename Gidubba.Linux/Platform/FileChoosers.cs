using Avalonia.Platform.Storage;
using TableForge.Portable;

namespace TableForge;

/// <summary>What a Save dialog asks: its title ("Save Foundry JSON") and the file name it starts from.</summary>
public sealed record SaveFileRequest(string Title, string SuggestedName);

/// <summary>
/// Asking where to save a file, using the desktop's own file dialog (through the XDG portal where there is one).
/// Same names and shapes as the Windows build, so the shared view models use them unchanged.
/// </summary>
public static class SaveFileChooser
{
    /// <summary>The Save dialog for a .json file, titled and starting from the request. Null when cancelled.</summary>
    public static string? ChooseJson(SaveFileRequest request) => FileDialogs.Save(request, "JSON files", ".json");

    /// <summary>The Save dialog for a PUM Companion bundle (.pumb). Null when cancelled.</summary>
    public static string? ChoosePumBundle(SaveFileRequest request) => FileDialogs.Save(request, "PUM Companion bundle", ".pumb");
}

/// <summary>Asking where to save a Collection file, or which one to import.</summary>
public static class CollectionFileChooser
{
    /// <summary>The Save dialog for a .tfcollection file. Null when cancelled.</summary>
    public static string? ChooseSave(SaveFileRequest request) => FileDialogs.Save(request, "Gidubba Collection", PortableFormat.Extension);

    /// <summary>The Open dialog for one .tfcollection file. Null when cancelled.</summary>
    public static string? ChooseOpen() => FileDialogs.Open("Import Collection", "Gidubba Collection", PortableFormat.Extension);
}

internal static class FileDialogs
{
    public static string? Save(SaveFileRequest request, string typeName, string extension)
    {
        var storage = App.MainWindow?.StorageProvider;
        if (storage is null) return null;

        var type = new FilePickerFileType(typeName) { Patterns = [$"*{extension}"] };
        var task = storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [type, FilePickerFileTypes.All],
            ShowOverwritePrompt = true,
        });

        var path = UiSync.Wait(task)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return null;

        // Not every desktop adds the extension for you.
        return Path.HasExtension(path) ? path : path + extension;
    }

    public static string? Open(string title, string typeName, string extension)
    {
        var storage = App.MainWindow?.StorageProvider;
        if (storage is null) return null;

        var type = new FilePickerFileType(typeName) { Patterns = [$"*{extension}"] };
        var task = storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type, FilePickerFileTypes.All],
        });

        var files = UiSync.Wait(task);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
