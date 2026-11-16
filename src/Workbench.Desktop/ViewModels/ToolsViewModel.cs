using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>This machine's tools: whether each is running and what it has loaded, start buttons where
/// a tool has a program to start, and the native bits: Forge draws from a prompt, Ollama answers one,
/// ComfyUI's newest images.</summary>
public partial class ToolsViewModel : ObservableObject
{
    readonly MainViewModel _main;
    public ObservableCollection<ToolCard> Cards { get; } = [];

    public ToolsViewModel(MainViewModel main, IEnumerable<ToolConfig> tools)
    {
        _main = main;
        foreach (var t in tools) Cards.Add(new ToolCard(t));
        // claude-draw is a plugin every agent already has, not a tool someone lists: it is always here
        if (!Cards.Any(c => c.IsDraw))
            Cards.Add(new ToolCard(new ToolConfig { Id = "claude-draw", Name = "claude-draw", Kind = "claude-draw", Url = ClaudeDraw.Url }));
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    async Task Refresh() => await Task.WhenAll(Cards.Select(c => c.Check()));

    [RelayCommand] void Close() => _main.CloseTools();
}

public partial class ToolCard(ToolConfig tool) : ObservableObject
{
    public ToolConfig Tool { get; } = tool;
    public string Name => Tool.Name;
    public string Url => Tool.Url;
    public bool IsForge => Tool.Kind == "forge";
    public bool IsOllama => Tool.Kind == "ollama";
    public bool IsComfy => Tool.Kind == "comfyui";
    public bool IsDraw => Tool.Kind == "claude-draw";
    public bool CanStart => IsDraw ? ClaudeDraw.DaemonScript() is not null : Tool.Start is not null;
    public ObservableCollection<string> Models { get; } = [];
    public ObservableCollection<Bitmap> Recent { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowStart))] public partial bool Up { get; set; }
    [ObservableProperty] public partial string Detail { get; set; } = "checking…";
    [ObservableProperty] public partial bool Busy { get; set; }
    public bool ShowStart => CanStart && !Up;

    // Forge
    [ObservableProperty] public partial string Prompt { get; set; } = "";
    [ObservableProperty] public partial string Negative { get; set; } = "";
    [ObservableProperty] public partial int Width { get; set; } = 832;
    [ObservableProperty] public partial int Height { get; set; } = 1216;
    [ObservableProperty] public partial int Steps { get; set; } = 24;
    [ObservableProperty] public partial Bitmap? Picture { get; set; }
    [ObservableProperty] public partial string? SavedTo { get; set; }
    // Ollama
    [ObservableProperty] public partial string? Model { get; set; }
    [ObservableProperty] public partial string Question { get; set; } = "";
    [ObservableProperty] public partial string Answer { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";

    public async Task Check()
    {
        if (IsDraw)
        {
            var d = await ClaudeDraw.Read();
            Up = d is not null;
            Detail = ClaudeDraw.Describe(d);
            return;
        }
        var s = await Tools.Check(Tool);
        Up = s.Up;
        Detail = s.Detail;
        if (!Up) return;
        if (IsOllama && Models.Count == 0)
        {
            foreach (var m in await Tools.OllamaModels(Tool.Url)) Models.Add(m);
            Model ??= Models.FirstOrDefault();
        }
        if (IsComfy)
        {
            Recent.Clear();
            foreach (var url in await Tools.ComfyRecent(Tool.Url, 6))
                if (await Tools.Fetch(url) is { } bytes)
                    try { using var ms = new MemoryStream(bytes); Recent.Add(Bitmap.DecodeToHeight(ms, 160)); } catch (Exception) { }
        }
    }

    [RelayCommand]
    async Task Start()
    {
        if (IsDraw) Detail = ClaudeDraw.Start() ? "starting…" : "couldn't start it (is node on PATH?)";
        else if (Tool.Start is null) return;
        else Detail = Tools.Launch(Tool.Start) ? "starting…" : "couldn't start it";
        for (var i = 0; i < 20 && !Up; i++) { await Task.Delay(1500); await Check(); }
    }

    [RelayCommand]
    void Open()
    {
        if (Tool.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo(Tool.Url) { UseShellExecute = true });
    }

    /// <summary>claude-draw: a blank canvas to draw on unasked. The drawing waits until an agent collects it
    /// (tell one you've sent it something). Opens the canvas here if none is open anywhere.</summary>
    [RelayCommand]
    async Task NewCanvas()
    {
        Error = "";
        if (!Up) await Start();
        if (!Up) return;
        if (await ClaudeDraw.Blank() is { } error) { Error = $"Couldn't start a blank canvas: {error}"; return; }
        if ((await ClaudeDraw.Read())?.Canvases is 0) ClaudeDraw.Open();
        await Check();
    }

    [RelayCommand]
    async Task Generate()
    {
        if (Prompt.Trim().Length == 0 || Busy) return;
        Busy = true; Error = ""; SavedTo = null;
        var (png, error) = await Tools.ForgeTxt2Img(Tool.Url, Prompt, Negative, Width, Height, Steps);
        Busy = false;
        if (png is null) { Error = error; return; }
        // keep every image: they go in the instance's folder, by date
        var dir = Path.Combine(InstanceConfig.Folder(), "forge", DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{DateTime.Now:HHmmss}.png");
        await File.WriteAllBytesAsync(file, png);
        SavedTo = file;
        using var ms = new MemoryStream(png);
        Picture = new Bitmap(ms);
    }

    [RelayCommand]
    async Task Ask()
    {
        if (Question.Trim().Length == 0 || Model is null || Busy) return;
        Busy = true; Error = ""; Answer = "";
        var (text, error) = await Tools.OllamaAsk(Tool.Url, Model, Question);
        Busy = false;
        Answer = text;
        Error = error;
    }
}
