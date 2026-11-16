using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Workbench.Core;

namespace Workbench.Desktop.Views;

/// <summary>The file dialog for a note's or card's picture.</summary>
public static class PicturePicker
{
    public static async Task<string?> Pick(Control from)
    {
        if (TopLevel.GetTopLevel(from)?.StorageProvider is not { } storage) return null;
        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick a picture", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = Pictures.Extensions.Select(e => "*" + e).ToList() }],
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }
}
