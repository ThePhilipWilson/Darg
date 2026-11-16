using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>The project's site, live: the dev server started (or found already running) and its pages
/// shown in a browser inside Workbench. Edits the server hot-reloads show as they land.</summary>
public partial class PreviewViewModel(PreviewConfig config) : ObservableObject
{
    public PreviewConfig Config { get; } = config;
    DevServer? _server;
    bool _opened;

    /// <summary>The page shown; the view navigates when it changes.</summary>
    [ObservableProperty] public partial Uri? Page { get; set; }
    /// <summary>The address box: follows the page, and Enter goes where it says.</summary>
    [ObservableProperty] public partial string Address { get; set; } = config.Url ?? "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ServerLabel))] public partial bool ServerRunning { get; set; }
    [ObservableProperty] public partial bool ShowLog { get; set; }
    [ObservableProperty] public partial string Log { get; set; } = "";
    public bool HasCommand => Config.Command is { Length: > 0 };
    public string ServerLabel => ServerRunning ? "■ Stop server" : "▶ Start server";
    public string ServerTip => $"{Config.Command}  (in {Config.Cwd})";

    /// <summary>First time the section shows: use a server that's already up, else start one.</summary>
    public async Task Open()
    {
        if (_opened) return;
        _opened = true;
        if (Config.Url is { Length: > 0 } url && await Preview.Answers(url)) { Status = $"Already running at {url}"; Go(url); return; }
        if (HasCommand) StartServer();
        else Status = $"Nothing answers at {Config.Url}";
    }

    void StartServer()
    {
        _server?.Dispose();
        Log = "";
        Status = $"Starting: {Config.Command}";
        try { _server = new DevServer(Config.Command!, Config.Cwd ?? Environment.CurrentDirectory); }
        catch (Exception e) { Status = $"Couldn't start {Config.Command}: {e.Message}"; ShowLog = true; return; }
        var server = _server;
        ServerRunning = true;
        server.Line += _ => Dispatcher.UIThread.Post(() => { if (_server == server) Log = server.Log; });
        server.UrlFound += u => Dispatcher.UIThread.Post(async () => await ShowWhenUp(server, u));
        server.Exited += code => Dispatcher.UIThread.Post(() =>
        {
            if (_server != server) return;
            ServerRunning = false;
            Status = $"The server stopped (exit {code}).";
            ShowLog = code != 0 && code != -1;
        });
        // a server that never prints its address: try the configured one
        if (Config.Url is { Length: > 0 } url)
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < 60 && _server == server && server.Url is null; i++)
                {
                    await Task.Delay(1000);
                    if (server.Url is null && await Preview.Answers(url)) { Dispatcher.UIThread.Post(() => { if (server.Url is null) Go(url); }); return; }
                }
            });
    }

    async Task ShowWhenUp(DevServer server, string url)
    {
        for (var i = 0; i < 30 && _server == server && !await Preview.Answers(url); i++) await Task.Delay(500);
        if (_server != server) return;
        Status = $"Serving at {url}";
        Go(url);
    }

    void Go(string url)
    {
        if (!url.Contains("://")) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { Status = $"Not an address: {url}"; return; }
        Address = uri.ToString();
        Page = uri;
    }

    /// <summary>The browser went somewhere (a link clicked): the address box follows.</summary>
    public void Navigated(Uri? uri) { if (uri is not null) Address = uri.ToString(); }

    [RelayCommand] void GoToAddress() => Go(Address.Trim());

    [RelayCommand]
    void ToggleServer()
    {
        if (ServerRunning) { var s = _server; _server = null; s?.Dispose(); ServerRunning = false; Status = "Server stopped."; }
        else if (HasCommand) StartServer();
    }

    [RelayCommand] void ToggleLog() => ShowLog = !ShowLog;

    [RelayCommand]
    void OpenInBrowser()
    {
        var url = Page?.ToString() ?? Address;
        if (url.Length > 0) try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
    }

    public void Close() { _server?.Dispose(); _server = null; }
}
