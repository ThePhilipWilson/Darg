using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The app's own settings (the header's ⚙ Settings): the theme (a pick shows at once and is saved to
/// settings.json in the instance folder), phone notifications through ntfy (AND-0006, kept in instance.json), and phone access (<see cref="PhoneViewModel"/>, AND-0002).</summary>
public partial class SettingsViewModel : ObservableObject
{
    readonly MainViewModel _main;
    public IReadOnlyList<ThemeChoice> Themes { get; }
    /// <summary>Pairing a phone and the paired ones (AND-0002).</summary>
    public PhoneViewModel Phone { get; } = new();
    [ObservableProperty] public partial string? Status { get; set; }

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        Themes = [.. Core.Themes.All.Select(t => new ThemeChoice(t, new RelayCommand(() => Pick(t))))];
        Mark();
        PushTopic = main.PushConfig?.Topic ?? "";
        PushServer = main.PushConfig?.Server ?? PushConfig.DefaultServer;
        main.PushChanged += ShowPush;
        ShowPush();
    }

    /// <summary>The ntfy topic phone notifications go to (blank: off).</summary>
    [ObservableProperty] public partial string PushTopic { get; set; } = "";
    [ObservableProperty] public partial string PushServer { get; set; } = PushConfig.DefaultServer;
    /// <summary>How phone notifications stand: on or off, where to subscribe, the last answer and any problem.</summary>
    [ObservableProperty] public partial string PushState { get; set; } = "";

    void ShowPush() => Avalonia.Threading.Dispatcher.UIThread.Post(() => PushState = _main.PushStatus());

    /// <summary>A fresh secret topic, saved and on at once.</summary>
    [RelayCommand] void NewPushTopic()
    {
        PushTopic = PushConfig.NewTopic();
        SavePush();
    }

    [RelayCommand] void SavePush()
    {
        var on = PushTopic.Trim().Length > 0;
        Status = _main.SavePush(on ? new PushConfig { Topic = PushTopic.Trim(), Server = PushServer.Trim(), Token = _main.PushConfig?.Token } : null);
    }

    [RelayCommand] void PushOff()
    {
        PushTopic = "";
        SavePush();
    }

    [RelayCommand] async Task PushTest() => PushState = await _main.PushTest() + Environment.NewLine + _main.PushStatus();

    void Pick(Theme theme)
    {
        Status = _main.SetTheme(theme);
        Mark();
    }

    /// <summary>The theme changed elsewhere (the API): tick the one now on.</summary>
    public void Refresh() => Mark();

    void Mark()
    {
        foreach (var c in Themes) c.IsCurrent = c.Theme == ThemeManager.Current;
    }

    [RelayCommand] void Close()
    {
        _main.PushChanged -= ShowPush;
        _main.CloseSettings();
    }
}

/// <summary>A theme as a card to click: its name, a line on it, and a little picture of a window in its colours.</summary>
public partial class ThemeChoice(Theme theme, IRelayCommand pick) : ObservableObject
{
    public Theme Theme { get; } = theme;
    public IRelayCommand PickCommand { get; } = pick;
    public string Name => $"{Theme.Mark} {Theme.Name}";
    public string Blurb => Theme.Blurb;
    public string AutomationName => $"{Theme.Name} theme";
    [ObservableProperty] public partial bool IsCurrent { get; set; }

    IBrush B(string key) => new SolidColorBrush(Color.Parse(Theme.Colours[key]));
    public IBrush Bg => B("Bg");
    public IBrush Chrome => B("Chrome");
    public IBrush Panel => B("Panel");
    public IBrush Panel2 => B("Panel2");
    public IBrush Line => B("Line");
    public IBrush Ink => B("Ink");
    public IBrush Dim => B("Dim");
    public IBrush Accent => B("Accent");
    public IBrush OnAccent => B("OnAccent");
    public IBrush Ok => B("Ok");
    public IBrush Info => B("Info");
}
