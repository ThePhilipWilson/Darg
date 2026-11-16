using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Workbench.Core;

/// <summary>Restart Workbench, or rebuild it from its repo and restart into the new build.
///
/// An update builds into a folder beside the install (<c>D:\Apps\Workbench.next</c>) while Workbench
/// keeps running, so a build that fails changes nothing and says why. Only once it has built does
/// Workbench start a small helper outside its own job (everything in the job dies with Workbench), close,
/// and leave the helper to wait for it to go, copy the new build over the old and start it again.
///
/// Where the source is: <c>source.txt</c> in the install folder, written by tools/publish.ps1. A copy
/// without one (a debug build in bin/) can restart but not update.</summary>
public static class Updater
{
    public const string SourceFile = "source.txt";

    /// <summary>The folder this copy of Workbench runs from.</summary>
    public static string InstallFolder => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>The repo this copy was published from, if it was published by tools/publish.ps1 and the
    /// repo is still there.</summary>
    public static string? SourceRepo(string? installFolder = null)
    {
        var file = Path.Combine(installFolder ?? InstallFolder, SourceFile);
        try
        {
            if (!File.Exists(file)) return null;
            var repo = File.ReadAllText(file).Trim();
            return repo.Length > 0 && File.Exists(Path.Combine(repo, "src", "Workbench.Desktop", "Workbench.Desktop.csproj")) ? repo : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>Where an update is built before it replaces the install.</summary>
    public static string StagingFolder(string installFolder) => Path.TrimEndingDirectorySeparator(installFolder) + ".next";

    public static string RuntimeId =>
        OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

    /// <summary>The same publish tools/publish.ps1 runs, into <paramref name="output"/>.</summary>
    public static ProcessStartInfo PublishCommand(string repo, string output)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "publish", Path.Combine(repo, "src", "Workbench.Desktop"), "-c", "Release", "-r", RuntimeId,
                     "--self-contained", "false", "-o", output, "-v", "q", "--nologo" })
            psi.ArgumentList.Add(a);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NUGET_PACKAGES")) && Directory.Exists(@"D:\nuget\packages"))
            psi.Environment["NUGET_PACKAGES"] = @"D:\nuget\packages"; // C: is small; the cache lives on D:
        return psi;
    }

    /// <summary>Build the update into the staging folder. Returns null when it built, else what went wrong
    /// (the error lines, or the tail of the output).</summary>
    public static async Task<string?> Build(string repo, string installFolder, Action<string>? progress = null, CancellationToken ct = default)
    {
        var staging = StagingFolder(installFolder);
        try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"Couldn't clear {staging}: {e.Message}"; }

        var output = new List<string>();
        using var p = new Process { StartInfo = PublishCommand(repo, staging) };
        void Line(string? l)
        {
            if (string.IsNullOrWhiteSpace(l)) return;
            lock (output) output.Add(l);
            progress?.Invoke(l.Trim());
        }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);
        try { p.Start(); }
        catch (Exception e) { return $"Couldn't run dotnet: {e.Message}"; }
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(entireProcessTree: true); } catch (Exception) { } throw; }
        if (p.ExitCode == 0 && File.Exists(Path.Combine(staging, ExeName))) return null;
        lock (output) return Failure(output, p.ExitCode);
    }

    /// <summary>What to show for a failed build: its error lines (each once), else its last lines.</summary>
    public static string Failure(IReadOnlyList<string> output, int exitCode)
    {
        var errors = output.Where(l => l.Contains(" error ", StringComparison.OrdinalIgnoreCase) || l.Contains(": error", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim()).Distinct().Take(8).ToList();
        var shown = errors.Count > 0 ? errors : output.TakeLast(8).Select(l => l.Trim()).ToList();
        return $"The build failed (exit {exitCode}). Nothing was changed.\n" + string.Join("\n", shown);
    }

    public static string ExeName => OperatingSystem.IsWindows() ? "Workbench.Desktop.exe" : "Workbench.Desktop";

    /// <summary>The helper that outlives Workbench: waits for process <paramref name="pid"/> to end, copies
    /// <paramref name="staging"/> over <paramref name="install"/> if given (a few tries, as files can stay
    /// locked a moment after exit), and starts <paramref name="exe"/> again, old build or new. If the copy
    /// fails it writes why to <paramref name="failedFile"/>, which the restarted Workbench shows.</summary>
    public static string HelperScript(int pid, string exe, IReadOnlyList<string> args, string? staging, string install, string failedFile)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"try {{ Wait-Process -Id {pid} -Timeout 120 }} catch {{ }}");
        // dev hooks would start their agent or open Setup a second time
        sb.AppendLine("foreach ($v in 'WORKBENCH_AGENT', 'WORKBENCH_AGENT_CWD', 'WORKBENCH_ADD_FOLDER') { Remove-Item -LiteralPath \"Env:$v\" -ErrorAction SilentlyContinue }");
        if (staging is not null)
        {
            sb.AppendLine($"$staging = {Q(staging)}; $install = {Q(install)}; $err = $null");
            sb.AppendLine("for ($i = 0; $i -lt 10; $i++) {");
            sb.AppendLine("  try { Copy-Item -Path (Join-Path ([WildcardPattern]::Escape($staging)) '*') -Destination $install -Recurse -Force; $err = $null; break }");
            sb.AppendLine("  catch { $err = $_; Start-Sleep -Seconds 1 }");
            sb.AppendLine("}");
            sb.AppendLine($"if ($err) {{ Set-Content -LiteralPath {Q(failedFile)} -Value \"The new build was made but couldn't be copied into $install, so this may be the old build, or a mix of the two: run tools/publish.ps1 from a terminal to be sure.`n$err\" }}");
            sb.AppendLine("else { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }");
        }
        var argList = args.Count == 0 ? "" : " -ArgumentList @(" + string.Join(", ", args.Select(a => Q(a.Contains(' ') ? $"\"{a}\"" : a))) + ")";
        var window = OperatingSystem.IsWindows() ? " -WindowStyle Normal" : "";
        sb.AppendLine($"Start-Process -FilePath {Q(exe)}{argList} -WorkingDirectory {Q(Path.GetDirectoryName(exe) ?? install)}{window}");
        return sb.ToString();
    }

    /// <summary>Write the helper to <paramref name="scriptFile"/> and start it, outside Workbench's job so
    /// it lives on once Workbench closes.</summary>
    public static void StartHelper(string script, string scriptFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scriptFile)!);
        File.WriteAllText(scriptFile, script);
        var pwsh = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptFile}\"";
        if (!OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(pwsh, args) { UseShellExecute = false });
            return;
        }
        try { ChildProcesses.StartOutsideJob(pwsh, args); }
        catch (System.ComponentModel.Win32Exception)
        {
            // Workbench is itself inside a job that won't let children leave (started from a shell that runs
            // in one): have Explorer, which is outside it, start the helper instead
            var cmd = Path.ChangeExtension(scriptFile, ".cmd");
            File.WriteAllText(cmd, $"@start \"\" /min {pwsh} {args}\r\n");
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { cmd }, UseShellExecute = false });
        }
    }
}
