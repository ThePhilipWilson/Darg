using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Workbench.Core;

/// <summary>Makes every process Workbench starts (agents, terminals and their children) end when
/// Workbench ends, however it ends: closed, killed or crashed. Without it a killed Workbench leaves
/// its agents running headless, still editing files.
///
/// Windows: Workbench puts itself in a job object set to kill on close. Children join the job
/// automatically, and when Workbench's process goes, the last handle to the job goes with it and
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
    /// a stopped dev server left its node running. A job doesn't. Elsewhere it is a plain tree kill.</summary>
    public static ProcessGroup Group(System.Diagnostics.Process started)
    {
        var job = IntPtr.Zero;
        if (OperatingSystem.IsWindows())
            try
            {
                job = KillOnCloseJob();
                if (!AssignProcessToJobObject(job, started.Handle)) { CloseHandle(job); job = IntPtr.Zero; }
            }
            catch (Exception) { job = IntPtr.Zero; }
        return new ProcessGroup(started, job);
    }

    public sealed class ProcessGroup(System.Diagnostics.Process process, IntPtr job) : IDisposable
    {
        IntPtr _job = job;
        public void Dispose()
        {
            if (_job != IntPtr.Zero) { TerminateJobObject(_job, 1); CloseHandle(_job); _job = IntPtr.Zero; }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        }
    }

    /// <summary>Start a process that is not in Workbench's job, so it outlives Workbench (the restart and
    /// update helper). Windows only; throws <see cref="Win32Exception"/> if Windows refuses, as it does
    /// when Workbench itself runs inside another job that doesn't allow breaking away.</summary>
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
