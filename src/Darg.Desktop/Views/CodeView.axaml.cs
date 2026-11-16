using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

/// <summary>The editor is AvaloniaEdit (read-only for now) with TextMate colouring chosen by extension;
/// the tree opens a file on a click and a folder on a click or Enter.</summary>
public partial class CodeView : UserControl
{
    static readonly RegistryOptions Grammars = new(ThemeName.DarkPlus);
    readonly TextMate.Installation _textMate;
    CodeViewModel? _vm;

    public CodeView()
    {
        InitializeComponent();
        TabDrag.Attach(FileTabs, (from, to) => (DataContext as CodeViewModel)?.Tabs.Move(from, to));
        _textMate = Editor.InstallTextMate(Grammars);
        Tree.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && Tree.SelectedItem is TreeNode n && _vm is not null) n.Activate(_vm);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Tree.KeyDown += (_, e) => { if (e.Key == Key.Enter && Tree.SelectedItem is TreeNode n && _vm is not null) n.Activate(_vm); };
        // typing marks the open file edited; its text is kept with it when another tab is shown
        Editor.TextChanged += (_, _) =>
        {
            if (_loading || _shown is null) return;
            _shown.Edited = Editor.Text;
            _shown.Dirty = true;
        };
        Editor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.S || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || _shown is null || _vm is null) return;
            e.Handled = true;
            _vm.Status = _shown.Save() is { } why ? $"Couldn't save: {why}" : _shown.Saved ?? "";
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    bool _loading;
    OpenFile? _shown;

    // TextMate's colours follow the app's theme: Dark+ on a dark one, Light+ on a light one
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += Retheme;
        Retheme();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.Changed -= Retheme;
    }

    bool? _light;
    void Retheme()
    {
        var light = ThemeManager.Current.IsLight;
        if (_light == light) return;
        _light = light;
        _textMate.SetTheme(Grammars.LoadTheme(light ? ThemeName.LightPlus : ThemeName.DarkPlus));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= Changed;
        _vm = DataContext as CodeViewModel;
        if (_vm is not null) _vm.PropertyChanged += Changed;
        Show(_vm?.Current);
    }

    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeViewModel.Current)) Show(_vm?.Current);
    }

    void Show(OpenFile? f)
    {
        Editor.IsVisible = f?.IsText == true;
        ImageBox.IsVisible = f?.IsImage == true;
        Why.IsVisible = f is not null && !f.IsText && !f.IsImage;
        Why.Text = f?.Why ?? "";
        Picture.Source = f?.Image;
        _shown = f?.IsText == true ? f : null;
        if (f?.IsText != true) return;
        _loading = true;
        Editor.Text = f.Edited ?? f.Text;
        _loading = false;
        Editor.ScrollToHome();
        _textMate.SetGrammar(Grammars.GetScopeByExtension(f.Extension)); // null: plain text
    }
}
