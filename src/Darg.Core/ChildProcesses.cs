using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Darg.Core;

/// <summary>Makes every process Darg starts (agents, terminals and their children) end when
/// Darg ends, however it ends: closed, killed or crashed. Without it a killed Darg leaves
/// its agents running headless, still editing files.
///
/// Windows: Darg puts itself in a job object set to kill on close. Children join the job
/// automatically, and when Darg's process goes, the last handle to the job goes with it and
/// Windows ends everything in it. Elsewhere this does nothing yet; there AgentSession's disposal on a
/// normal exit is what stops agents.</summary>
public static class ChildProcesses
{
    static IntPtr _job;

    public static void TieToThisProcess()
    {
        if (!OperatingSystem.IsWindows() || _job != IntPtr.Zero) return;
        var job = KillOnCloseJob(breakawayOk: true); // only StartOutsideJob asks to break away
        if (!AssignProcessToJobObject(job, GetCurrentProcess())) throw new Win32Exception();
        _job = job; // held for the life of the process; never closed by hand
    }

    /// <summary>A process and everything it goes on to start, ended together by <see cref="ProcessGroup.Dispose"/>.
    /// Killing a process tree by parent ids misses what an intermediate (npm, cmd) has already let go of:
    /// a stopped dev server left its node running. A job doesn't. Elsewhere it is a plain tree kill.
    ///
    /// A job misses some too (WOR-0170): what the Microsoft Store's PowerShell (pwsh from WindowsApps, the one agents'
    /// PowerShell tool runs here) starts goes into its package's job, not the caller's. So the group also watches (see
    /// <see cref="Every"/>) and ends what it saw that is still running.</summary>
    /// <param name="allowBreakaway">Let a child that asks to (CREATE_BREAKAWAY_FROM_JOB) leave, as Darg's own job does:
    /// without it such a start fails outright.</param>
    public static ProcessGroup Group(System.Diagnostics.Process started, bool allowBreakaway = false)
    {
        var job = IntPtr.Zero;
        if (OperatingSystem.IsWindows())
            try
            {
                job = KillOnCloseJob(allowBreakaway);
                if (!AssignProcessToJobObject(job, started.Handle)) { CloseHandle(job); job = IntPtr.Zero; }
            }
            catch (Exception) { job = IntPtr.Zero; }
        return new ProcessGroup(started, job, null);
    }

    /// <summary>Watches what a process starts and, on Dispose, ends what is still running of it whose exe name
    /// <paramref name="ends"/> picks, with whatever those started; the process itself and the rest are left alone. An agent
    /// session's (WOR-0170): the copies of Darg and the emulators an agent left running go when it does, but not a daemon
    /// its tools started for every session to share (claude-draw, Forge), which a job would end too. Windows only.</summary>
    public static ProcessGroup Leftovers(System.Diagnostics.Process started, Func<string, bool> ends) => new(started, IntPtr.Zero, ends);

    /// <summary>How often groups look for new descendants: often enough to see a shell that lives a moment
    /// (Store pwsh takes longer than this to start) before it lets go of what it started. Each look notes every process
    /// whose parent was seen before (by id and start time, so a reused id isn't taken for it), so what a shell started
    /// is known once the shell has gone.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMilliseconds(300);

    public sealed class ProcessGroup : IDisposable
    {
        readonly System.Diagnostics.Process _process;
        readonly int _root; // its id, kept: a Process disposed meanwhile can't say
        IntPtr _job;
        readonly Func<string, bool>? _ends; // null: end everything (and the process)
        // the process and every descendant seen, by id: its start time (UTC file time), parent and exe name
        readonly Dictionary<int, (long Start, int Parent, string Name)> _seen = [];
        // how many looks running a seen process has been missing: one gone a while is forgotten, so _seen stays small
        // over an agent's thousands of git calls (a while: what it started is noted in the meantime, its id is known)
        readonly Dictionary<int, int> _missing = [];
        const int ForgetAfter = 20;
        bool _disposed;

        internal ProcessGroup(System.Diagnostics.Process process, IntPtr job, Func<string, bool>? ends)
        {
            _process = process;
            _job = job;
            _ends = ends;
            _root = process.Id;
            if (!OperatingSystem.IsWindows()) return;
            if (StartTime(_root) is { } t) _seen[_root] = (t, 0, "");
            Watch.Add(this);
        }

        internal void Learn(IReadOnlyList<(int Id, int Parent, string Name)> snapshot)
        {
            lock (_seen)
            {
                if (_disposed || _seen.Count == 0) return;
                for (var grew = true; grew;)
                {
                    grew = false;
                    foreach (var (id, parent, name) in snapshot)
                    {
                        if (_seen.ContainsKey(id) || !_seen.TryGetValue(parent, out var p)) continue;
                        // a child starts after its parent; one that didn't has a parent id that was reused
                        if (StartTime(id) is not { } start || start < p.Start) continue;
                        _seen[id] = (start, parent, name);
                        grew = true;
                    }
                }
                var running = snapshot.Select(s => s.Id).ToHashSet();
                foreach (var id in _seen.Keys.ToList())
                {
                    if (id == _root) continue;
                    if (running.Contains(id)) { _missing.Remove(id); continue; }
                    var n = _missing.GetValueOrDefault(id) + 1;
                    if (n < ForgetAfter) _missing[id] = n;
                    else { _missing.Remove(id); _seen.Remove(id); }
                }
            }
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                Watch.Remove(this);
                Learn(Snapshot()); // a last look, for what started since the last
            }
            if (_job != IntPtr.Zero) { TerminateJobObject(_job, 1); CloseHandle(_job); _job = IntPtr.Zero; }
            if (_ends is null) try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (Exception) { }
            Dictionary<int, (long Start, int Parent, string Name)> seen;
            lock (_seen) { _disposed = true; seen = new(_seen); _seen.Clear(); }
            HashSet<int> chosen = _ends is null ? [.. seen.Keys] : [.. seen.Where(s => s.Key != _root && _ends(s.Value.Name)).Select(s => s.Key)];
            for (var grew = _ends is not null; grew;) // and what those started
            {
                grew = false;
                foreach (var (id, s) in seen)
                    if (!chosen.Contains(id) && chosen.Contains(s.Parent)) { chosen.Add(id); grew = true; }
            }
            foreach (var id in chosen) End(id, seen[id].Start);
        }
    }

    /// <summary>The one timer every live group shares: a snapshot of the processes each <see cref="Every"/>.</summary>
    static class Watch
    {
        static readonly List<ProcessGroup> Groups = [];
        static Timer? _timer;

        public static void Add(ProcessGroup g)
        {
            lock (Groups)
            {
                Groups.Add(g);
                _timer ??= new Timer(_ => Tick());
                _timer.Change(Every, Every);
            }
        }

        public static void Remove(ProcessGroup g)
        {
            lock (Groups)
            {
                Groups.Remove(g);
                if (Groups.Count == 0) _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        static void Tick()
        {
            try
            {
                ProcessGroup[] groups;
                lock (Groups) groups = [.. Groups];
                if (groups.Length == 0) return;
                var snapshot = Snapshot();
                foreach (var g in groups) g.Learn(snapshot);
            }
            catch (Exception) { } // a timer thread throwing would end the app; the next tick tries again
        }
    }

    /// <summary>Every process running now: its id, its parent's id and its exe's name. Windows only (empty elsewhere).</summary>
    internal static IReadOnlyList<(int Id, int Parent, string Name)> Snapshot()
    {
        var list = new List<(int, int, string)>();
        if (!OperatingSystem.IsWindows()) return list;
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (var ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                list.Add(((int)e.th32ProcessID, (int)e.th32ParentProcessID, e.szExeFile ?? ""));
        }
        finally { CloseHandle(snap); }
        return list;
    }

    /// <summary>When a process started (UTC file time), or null if it isn't running or can't be looked at.</summary>
    static long? StartTime(int id)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!GetProcessTimes(h, out var created, out _, out _, out _)) return null;
            // a process that has ended but whose handle someone still holds can still be opened: it's not running
            return GetExitCodeProcess(h, out var code) && code == STILL_ACTIVE ? created : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>End a process if it is still the one that started then (not another that got its id).</summary>
    static void End(int id, long start)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, false, id);
        if (h == IntPtr.Zero) return;
        try
        {
            if (GetProcessTimes(h, out var created, out _, out _, out _) && created == start) TerminateProcess(h, 1);
        }
        finally { CloseHandle(h); }
    }

    const uint TH32CS_SNAPPROCESS = 0x2, PROCESS_TERMINATE = 0x1, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, STILL_ACTIVE = 259;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);

    /// <summary>Start a process that is not in Darg's job, so it outlives Darg (the restart and
    /// update helper). Windows only; throws <see cref="Win32Exception"/> if Windows refuses, as it does
    /// when Darg itself runs inside another job that doesn't allow breaking away.</summary>
    public static void StartOutsideJob(string exe, string arguments)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var cmd = new System.Text.StringBuilder($"\"{exe}\" {arguments}");
        var flags = _job != IntPtr.Zero ? CREATE_BREAKAWAY_FROM_JOB | CREATE_NO_WINDOW : CREATE_NO_WINDOW;
        if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, null, ref si, out var pi))
            throw new Win32Exception();
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    }

    const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000, CREATE_NO_WINDOW = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string? applicationName, System.Text.StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    static IntPtr KillOnCloseJob(bool breakawayOk = false)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | (breakawayOk ? JOB_OBJECT_LIMIT_BREAKAWAY_OK : 0) },
        };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size)) throw new Win32Exception();
        }
        finally { Marshal.FreeHGlobal(ptr); }
        return job;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    // lets a child started with CREATE_BREAKAWAY_FROM_JOB leave; children that don't ask still join
    const uint JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x800;

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS { public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes; }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
}
