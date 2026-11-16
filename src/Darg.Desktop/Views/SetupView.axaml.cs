using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        AddFolder.Click += async (_, _) =>
        {
            if (DataContext is not SetupViewModel setup || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick the project's root folder", AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) setup.AddFolder(path);
        };
    }
}
