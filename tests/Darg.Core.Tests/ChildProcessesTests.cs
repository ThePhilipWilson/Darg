using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0170: a group ends what its process left running, even once the shell that started it has gone, as an
/// agent's Start-Process'd emulator had. pwsh here is the Microsoft Store's, whose children leave the caller's job, so a
/// job alone missed them.</summary>
public class ChildProcessesTests
{
    /// <summary>pwsh starts each command in <paramref name="commands"/> with Start-Process (hidden) and writes their ids to
    /// a file, then is killed: what it started is nobody's child any more. Returns those processes.</summary>
    static List<Process> LeaveRunning(string dir, Func<Process, IDisposable> watch, out IDisposable group, params string[] commands)
    {
        var pidFile = Path.Combine(dir, "spawned.pid");
        var starts = string.Join("; ", commands.Select(c => $"(Start-Process {c} -WindowStyle Hidden -PassThru).Id"));
        var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", $"$ids = @({starts}); $ids -join ',' | Set-Content -LiteralPath '{pidFile}'; Start-Sleep 30" })
            psi.ArgumentList.Add(a);
        using var shell = Process.Start(psi)!;
        group = watch(shell);
        var until = DateTime.UtcNow.AddSeconds(20);
        string? ids = null;
        while (ids is null && DateTime.UtcNow < until)
        {
            // null until pwsh has written every id (and while it is still writing: the file is locked then)
            try { if (File.Exists(pidFile) && File.ReadAllText(pidFile).Trim() is var t && t.Split(',').Length == commands.Length && t.Split(',').All(i => int.TryParse(i, out _))) ids = t; }
            catch (IOException) { }
            if (ids is null) Thread.Sleep(100);
        }
        var left = ids!.Split(',').Select(id => Process.GetProcessById(int.Parse(id))).ToList();
        shell.Kill(); // the shell goes first, as an agent's does once its command returns
        shell.WaitForExit(5000);
        return left;
    }

    const string Sleeper = "-FilePath powershell -ArgumentList '-NoProfile','-Command','Start-Sleep 600'"; // not ping: DevServerTests counts those
    const string Waiter = "-FilePath cmd -ArgumentList '/k'"; // waits on its own console for ever

    [Fact]
    public void A_group_ends_what_its_process_started_and_let_go()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("darg-group-").FullName;
        List<Process> left = [];
        try
        {
            left = LeaveRunning(dir, p => ChildProcesses.Group(p, allowBreakaway: true), out var group, Sleeper);
            Assert.False(left[0].HasExited);

            group.Dispose();

            Assert.True(left[0].WaitForExit(5000), "the process the shell let go of is still running");
        }
        finally
        {
            foreach (var p in left) try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Leftovers_ends_only_what_it_is_told_to()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("darg-leftovers-").FullName;
        List<Process> left = [];
        try
        {
            left = LeaveRunning(dir, p => ChildProcesses.Leftovers(p, n => n.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)),
                out var group, Sleeper, Waiter);
            var (sleeper, waiter) = (left[0], left[1]);
            Assert.False(sleeper.HasExited);
            Assert.False(waiter.HasExited);

            group.Dispose();

            Assert.True(sleeper.WaitForExit(5000), "the leftover it was told to end is still running");
            Thread.Sleep(500);
            Assert.False(waiter.HasExited); // a daemon an agent's tools started for everyone stays
        }
        finally
        {
            foreach (var p in left) try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception) { }
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
