using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>A project's plugin (HOM-0001): a folder another repo owns, holding a <c>plugin.json</c> and a web page,
/// that Darg shows as a section on the project's rail. The page talks to the plugin's own command through
/// <c>darg.run(args)</c>, so the plugin can be any language and changes without Darg being rebuilt.</summary>
/// <param name="Id">Its name in addresses: the folder's name, made safe.</param>
/// <param name="Command">The program the page runs (relative to the folder, or on PATH), with any leading arguments; null for a page alone.</param>
public sealed record PluginInfo(string Folder, string Id, string Name, string Icon, string Tip, string Page, IReadOnlyList<string>? Command);

public static class PluginStore
{
    public const string FileName = "plugin.json";

    /// <summary>A plugin folder's settings, or why it can't be read.</summary>
    public static PluginInfo Load(string folder)
    {
        var full = Path.GetFullPath(folder);
        var file = Path.Combine(full, FileName);
        if (!File.Exists(file)) throw new InvalidDataException($"{full} has no {FileName}.");
        JsonObject o;
        try { o = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                  ?? throw new InvalidDataException($"{file} isn't a JSON object."); }
        catch (JsonException e) { throw new InvalidDataException($"{file} doesn't read: {e.Message}"); }
        string? Str(string key) => o.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>();
        var name = Str("name") is { Length: > 0 } n ? n : Path.GetFileName(full);
        var page = Str("page") is { Length: > 0 } p ? p : "index.html";
        if (!File.Exists(Path.Combine(full, page))) throw new InvalidDataException($"{name}'s page {page} isn't in {full}.");
        var command = Str("command") is { Length: > 0 } c ? Words(c) : null;
        return new PluginInfo(full, IdOf(full), name, Str("icon") is { Length: > 0 } i ? i : "◆", Str("tip") ?? name, page, command);
    }

    /// <summary>A project's plugins, in the order its <c>plugins</c> lists them, and the folders that couldn't be read with why.</summary>
    public static (List<PluginInfo> Plugins, List<string> Problems) For(ProjectConfig project)
    {
        var list = new List<PluginInfo>();
        var problems = new List<string>();
        foreach (var folder in project.Plugins ?? [])
        {
            try
            {
                var p = Load(folder);
                if (list.Any(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))) problems.Add($"{folder}: another plugin has the folder name {p.Id}.");
                else list.Add(p);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException) { problems.Add(e.Message); }
        }
        return (list, problems);
    }

    static string IdOf(string folder)
    {
        var sb = new StringBuilder();
        foreach (var ch in Path.GetFileName(folder.TrimEnd('\\', '/'))) sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? char.ToLowerInvariant(ch) : '-');
        return sb.Length > 0 ? sb.ToString() : "plugin";
    }

    /// <summary>A command line cut into words, "double quotes" keeping spaces.</summary>
    public static List<string> Words(string line)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false, any = false;
        foreach (var ch in line)
        {
            if (ch == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(ch) && !quoted) { if (any) words.Add(sb.ToString()); sb.Clear(); any = false; continue; }
            sb.Append(ch); any = true;
        }
        if (any) words.Add(sb.ToString());
        return words;
    }
}

/// <summary>One run of a plugin's command: its output lines as they come, and how it ended.</summary>
public sealed class PluginJob
{
    readonly List<string> _lines = [];
    readonly StringBuilder _errors = new();
    internal Process? Process;
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public string Plugin { get; init; } = "";
    public DateTime Ended { get; private set; } = DateTime.MaxValue;
    public bool Done { get; private set; }
    public int Exit { get; private set; }

    internal void Line(string? s) { if (s is null) return; lock (_lines) _lines.Add(s); }
    internal void Error(string? s) { if (s is null) return; lock (_errors) { if (_errors.Length < 64_000) _errors.AppendLine(s); } }
    internal void End(int exit) { Exit = exit; Ended = DateTime.UtcNow; Done = true; }

    /// <summary>The lines from <paramref name="from"/> on, whether it has ended (read before the lines, so none are missed), and its errors once it has.</summary>
    public (List<string> Lines, bool Done, int Exit, string Errors) Read(int from)
    {
        var done = Done;
        List<string> lines;
        lock (_lines) lines = from < _lines.Count ? _lines.GetRange(Math.Max(0, from), _lines.Count - Math.Max(0, from)) : [];
        string errors;
        lock (_errors) errors = done ? _errors.ToString() : "";
        return (lines, done, Exit, errors);
    }
}

/// <summary>Runs plugins' commands for their pages: each call starts the command with the page's arguments in the plugin's
/// folder (UTF-8 in and out, no window). Processes Darg starts end with it (its job), and Stop ends one sooner.</summary>
public sealed class PluginRunner
{
    readonly ConcurrentDictionary<string, PluginJob> _jobs = new();
    /// <summary>How long an ended job's output is kept for its page to read.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);

    public PluginJob Start(PluginInfo plugin, IReadOnlyList<string> args)
    {
        if (plugin.Command is not { Count: > 0 } command) throw new InvalidOperationException($"{plugin.Name} has no command to run.");
        Forget();
        var exe = command[0];
        var local = Path.Combine(plugin.Folder, exe);
        if (File.Exists(local)) exe = local;
        else if (OperatingSystem.IsWindows() && File.Exists(local + ".exe")) exe = local + ".exe";
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = plugin.Folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in command.Skip(1).Concat(args)) info.ArgumentList.Add(a);
        // as if typed in the plugin's folder: Claude Code's shells set this, and then cmd won't run a script sitting there
        info.Environment.Remove("NoDefaultCurrentDirectoryInExePath");
        var job = new PluginJob { Plugin = plugin.Id };
        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => job.Line(e.Data);
        p.ErrorDataReceived += (_, e) => job.Error(e.Data);
        try { p.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"{plugin.Name} couldn't start {command[0]}: {e.Message}");
        }
        job.Process = p;
        _jobs[job.Id] = job;
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _ = Task.Run(async () =>
        {
            await p.WaitForExitAsync(); // also waits for the output to be read to its end
            job.End(p.ExitCode);
            p.Dispose();
        });
        return job;
    }

    public PluginJob? Find(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    /// <summary>End a run and what it started.</summary>
    public bool Stop(string id)
    {
        if (Find(id) is not { Process: { } p } job || job.Done) return false;
        try { p.Kill(entireProcessTree: true); return true; } catch (Exception) { return false; }
    }

    void Forget()
    {
        foreach (var (id, job) in _jobs)
            if (job.Done && DateTime.UtcNow - job.Ended > Keep) _jobs.TryRemove(id, out _);
    }
}
