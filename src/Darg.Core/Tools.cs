using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>A local tool Darg talks to: Forge, ComfyUI, Ollama, Pinokio, or any web UI.</summary>
public sealed class ToolConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>forge, comfyui, ollama, pinokio or web: decides what Darg can do with it.</summary>
    public string Kind { get; set; } = "web";
    public string Url { get; set; } = "";
    /// <summary>A program that starts it (launched through the desktop shell, so it outlives Darg).</summary>
    public string? Start { get; set; }

    public static List<ToolConfig> Defaults()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ollamaApp = Path.Combine(local, "Programs", "Ollama", "ollama app.exe");
        return
        [
            new() { Id = "forge", Name = "Forge", Kind = "forge", Url = "http://127.0.0.1:7860" },
            new() { Id = "comfyui", Name = "ComfyUI", Kind = "comfyui", Url = "http://127.0.0.1:8188" },
            new() { Id = "ollama", Name = "Ollama", Kind = "ollama", Url = "http://127.0.0.1:11434", Start = File.Exists(ollamaApp) ? ollamaApp : null },
            new() { Id = "pinokio", Name = "Pinokio", Kind = "pinokio", Url = "http://127.0.0.1:42000" },
        ];
    }
}

public sealed record ToolStatus(bool Up, string Detail);

/// <summary>Talking to the tools over their local HTTP APIs. Every call is short and never throws:
/// a tool that isn't running is a status, not an error.</summary>
public static class Tools
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    static readonly HttpClient Slow = new() { Timeout = TimeSpan.FromMinutes(5) };

    static async Task<JsonNode?> Get(string url, HttpClient? client = null)
    {
        try { return JsonNode.Parse(await (client ?? Http).GetStringAsync(url)); }
        catch (Exception) { return null; }
    }

    public static async Task<ToolStatus> Check(ToolConfig t)
    {
        var u = t.Url.TrimEnd('/');
        switch (t.Kind)
        {
            case "forge":
                var opts = await Get($"{u}/sdapi/v1/options");
                return opts is null ? new(false, "not running") : new(true, $"model: {Short((string?)opts["sd_model_checkpoint"])}");
            case "comfyui":
                var stats = await Get($"{u}/system_stats");
                var queue = stats is null ? null : await Get($"{u}/queue");
                return stats is null ? new(false, "not running")
                    : new(true, $"{(string?)stats["devices"]?[0]?["name"] ?? "running"} · queue {(queue?["queue_running"] as JsonArray)?.Count ?? 0} running, {(queue?["queue_pending"] as JsonArray)?.Count ?? 0} waiting");
            case "ollama":
                var tags = await Get($"{u}/api/tags");
                var chat = (tags?["models"] as JsonArray ?? []).Count(m => !IsEmbedding((string?)m?["name"] ?? "", (string?)m?["details"]?["family"] ?? ""));
                return tags is null ? new(false, "not running") : new(true, $"{chat} models to talk to");
            default:
                try { using var r = await Http.GetAsync(u); return new(true, $"answering ({(int)r.StatusCode})"); }
                catch (Exception) { return new(false, "not running"); }
        }
    }

    static string Short(string? model) => model is null ? "?" : Path.GetFileNameWithoutExtension(model.Split(' ')[0]);

    // ------------------------------------------------------------ Forge
    public static async Task<List<string>> ForgeModels(string url) =>
        (await Get($"{url.TrimEnd('/')}/sdapi/v1/sd-models") as JsonArray ?? []).Select(m => (string?)m?["title"]).OfType<string>().ToList();

    /// <summary>txt2img: the first image's PNG bytes, or an error.</summary>
    public static async Task<(byte[]? Png, string Error)> ForgeTxt2Img(string url, string prompt, string negative, int width, int height, int steps, long seed = -1)
    {
        try
        {
            var body = new JsonObject
            {
                ["prompt"] = prompt, ["negative_prompt"] = negative, ["width"] = width, ["height"] = height,
                ["steps"] = steps, ["seed"] = seed, ["cfg_scale"] = 6, ["sampler_name"] = "Euler a",
            };
            using var r = await Slow.PostAsJsonAsync($"{url.TrimEnd('/')}/sdapi/v1/txt2img", body);
            var json = JsonNode.Parse(await r.Content.ReadAsStringAsync());
            if (!r.IsSuccessStatusCode) return (null, (string?)json?["detail"] ?? (string?)json?["error"] ?? $"HTTP {(int)r.StatusCode}");
            var b64 = (string?)json?["images"]?[0];
            return b64 is null ? (null, "Forge sent no image.") : (Convert.FromBase64String(b64), "");
        }
        catch (Exception e) { return (null, e.Message); }
    }

    // ------------------------------------------------------------ Ollama
    /// <summary>Models that can answer: embedding models (which only turn text into vectors) are left out.</summary>
    public static async Task<List<string>> OllamaModels(string url) =>
        ((await Get($"{url.TrimEnd('/')}/api/tags"))?["models"] as JsonArray ?? [])
            .Where(m => !IsEmbedding((string?)m?["name"] ?? "", (string?)m?["details"]?["family"] ?? ""))
            .Select(m => (string?)m?["name"]).OfType<string>().ToList();

    public static bool IsEmbedding(string name, string family) =>
        name.Contains("embed", StringComparison.OrdinalIgnoreCase) || family.Contains("bert", StringComparison.OrdinalIgnoreCase);

    public static async Task<(string Text, string Error)> OllamaAsk(string url, string model, string prompt)
    {
        try
        {
            using var r = await Slow.PostAsJsonAsync($"{url.TrimEnd('/')}/api/generate", new JsonObject { ["model"] = model, ["prompt"] = prompt, ["stream"] = false });
            var json = JsonNode.Parse(await r.Content.ReadAsStringAsync());
            if (r.IsSuccessStatusCode) return ((string?)json?["response"] ?? "", "");
            var said = (string?)json?["error"] ?? $"HTTP {(int)r.StatusCode}";
            // out of video memory: usually Forge or ComfyUI holding a model on the same GPU
            return ("", said.Contains("memory", StringComparison.OrdinalIgnoreCase) || said.Contains("alloc", StringComparison.OrdinalIgnoreCase)
                ? $"{said}. The GPU is probably full (Forge or ComfyUI holds a model): try a smaller model, or free the GPU first." : said);
        }
        catch (Exception e) { return ("", e.Message); }
    }

    // ------------------------------------------------------------ ComfyUI
    /// <summary>The newest output images' URLs, from ComfyUI's history.</summary>
    public static async Task<List<string>> ComfyRecent(string url, int count = 8)
    {
        var u = url.TrimEnd('/');
        var history = await Get($"{u}/history?max_items={count}") as JsonObject;
        var urls = new List<string>();
        foreach (var (_, run) in history?.Reverse() ?? [])
            foreach (var (_, output) in run?["outputs"] as JsonObject ?? [])
                foreach (var img in output?["images"] as JsonArray ?? [])
                    urls.Add($"{u}/view?filename={Uri.EscapeDataString((string?)img?["filename"] ?? "")}&subfolder={Uri.EscapeDataString((string?)img?["subfolder"] ?? "")}&type={(string?)img?["type"] ?? "output"}");
        return urls.Take(count).ToList();
    }

    public static async Task<byte[]?> Fetch(string url)
    {
        try { return await Slow.GetByteArrayAsync(url); } catch (Exception) { return null; }
    }

    /// <summary>Start a tool's program through the desktop shell (explorer on Windows, open on macOS),
    /// so it is not a child of Darg and keeps running when Darg closes.</summary>
    public static bool Launch(string program)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { program }, UseShellExecute = false });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", [program]);
            else Process.Start(new ProcessStartInfo(program) { UseShellExecute = true });
            return true;
        }
        catch (Exception) { return false; }
    }
}
