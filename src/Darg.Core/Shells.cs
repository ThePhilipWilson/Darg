using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>A shell a console can run: its name in the menu, the program and the arguments that start it
/// interactively.</summary>
public sealed record ShellInfo(string Name, string Path, IReadOnlyList<string> Args)
{
    /// <summary>Arguments that run a command and leave the shell open afterwards, so its output (or error) can be read.</summary>
    public IReadOnlyList<string> RunArgs(string command)
    {
        var exe = System.IO.Path.GetFileNameWithoutExtension(Path).ToLowerInvariant();
        return exe switch
        {
            "pwsh" or "powershell" => ["-NoLogo", "-NoExit", "-Command", command],
            "cmd" => ["/K", command],
            "wsl" => [.. Args, "--", "bash", "-c", command + "; exec bash -l"],
            // exec by name: a Windows path (Git Bash's) means nothing to bash itself
            _ => [.. Args.Where(a => a != "-i"), "-c", command + "; exec " + exe + (Args.Contains("--login") ? " --login" : "") + " -i"],
        };
    }
}

/// <summary>The shells installed on this machine, the platform's usual one first.</summary>
public static class Shells
{
    static readonly Lazy<IReadOnlyList<ShellInfo>> Found = new(Detect);

    /// <summary>Found once per run (WSL's distro list takes a moment to ask for).</summary>
    public static IReadOnlyList<ShellInfo> Available() => Found.Value;

    /// <summary>The shell called <paramref name="name"/>, else the first one found.</summary>
    public static ShellInfo Pick(string? name) =>
        Available().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Available()[0];

    public static IReadOnlyList<ShellInfo> Detect() => OperatingSystem.IsWindows() ? Windows() : Unix();

    static List<ShellInfo> Windows()
    {
        var list = new List<ShellInfo>();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var sys = Environment.SystemDirectory;
        if ((Existing(System.IO.Path.Combine(pf, "PowerShell", "7", "pwsh.exe")) ?? OnPath("pwsh.exe")) is { } pwsh)
            list.Add(new("PowerShell", pwsh, ["-NoLogo"]));
        if (Existing(System.IO.Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe")) is { } ps)
            list.Add(new("Windows PowerShell", ps, ["-NoLogo"]));
        if (GitBash(pf) is { } bash) list.Add(new("Git Bash", bash, ["--login", "-i"]));
        list.Add(new("Command Prompt", Existing(System.IO.Path.Combine(sys, "cmd.exe")) ?? "cmd.exe", []));
        if (Existing(System.IO.Path.Combine(sys, "wsl.exe")) is { } wsl)
            foreach (var d in WslDistros(wsl)) list.Add(new($"{d} (WSL)", wsl, ["-d", d]));
        return list;
    }

    /// <summary>Git for Windows' bash.exe in bin\ (the one that sets up Git's environment), found beside git on
    /// the PATH or in the usual install folders.</summary>
    static string? GitBash(string programFiles)
    {
        var roots = new List<string>();
        if (OnPath("git.exe") is { } git && System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(git)) is { } root) roots.Add(root);
        roots.Add(System.IO.Path.Combine(programFiles, "Git"));
        roots.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git"));
        return roots.Select(r => Existing(System.IO.Path.Combine(r, "bin", "bash.exe"))).FirstOrDefault(p => p is not null);
    }

    /// <summary>WSL distros a person would open a shell in (Docker Desktop's own aren't).</summary>
    static IEnumerable<string> WslDistros(string wsl)
    {
        try
        {
            var psi = new ProcessStartInfo(wsl, "--list --quiet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode, // wsl.exe writes UTF-16
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch (InvalidOperationException) { } return []; }
            return ParseWslList(output.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return []; }
    }

    public static List<string> ParseWslList(string output) =>
        output.Replace("\0", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(d => !d.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            .ToList();

    static List<ShellInfo> Unix()
    {
        var paths = new List<string>();
        if (Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } login) paths.Add(login);
        try
        {
            if (File.Exists("/etc/shells"))
                paths.AddRange(File.ReadAllLines("/etc/shells").Select(l => l.Trim()).Where(l => l.StartsWith('/')));
        }
        catch (IOException) { }
        if (paths.Count == 0) paths.Add("/bin/zsh");
        return ParseUnixShells(paths).Select(p => new ShellInfo(System.IO.Path.GetFileName(p), p, ["-l"])).ToList();
    }

    /// <summary>One entry per shell name, the first path seen winning ($SHELL comes first), missing files and
    /// the non-interactive ones (sh, nologin) left out unless nothing else is there.</summary>
    public static List<string> ParseUnixShells(IEnumerable<string> paths, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        string[] never = ["nologin", "false"], plain = ["sh", "dash"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = paths.Where(p => !never.Contains(System.IO.Path.GetFileName(p)) && exists(p) && seen.Add(System.IO.Path.GetFileName(p))).ToList();
        var interactive = all.Where(p => !plain.Contains(System.IO.Path.GetFileName(p))).ToList();
        return interactive.Count > 0 ? interactive : all;
    }

    static string? Existing(string path) => File.Exists(path) ? path : null;

    static string? OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => { try { return System.IO.Path.Combine(d.Trim('"'), exe); } catch (ArgumentException) { return null; } })
            .FirstOrDefault(p => p is not null && File.Exists(p));
}
