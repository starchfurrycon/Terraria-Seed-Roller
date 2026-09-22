using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TerrariaSeedRoller.Core;

/// <summary>
/// A Windows job object holding every server this process starts.
/// </summary>
/// <remarks>
/// The job is created with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, so the
/// servers die with this process even when it is terminated by a crash or by the
/// task manager. That is what stops a killed roll from leaving servers behind
/// that keep eating the memory and CPU the next run needs.
/// </remarks>
internal sealed class OwnedProcessJob : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    private OwnedProcessJob(IntPtr handle) => _handle = handle;

    public static OwnedProcessJob? Create()
    {
        if (!OperatingSystem.IsWindows()) return null;
        IntPtr handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return null;
        OwnedProcessJob job = new(handle);
        if (!job.ApplyLimits(0, 0))
        {
            job.Dispose();
            return null;
        }
        return job;
    }

    /// <summary>Applies the process limits. A zero value leaves that limit unset.</summary>
    public bool ApplyLimits(long processMemoryLimitBytes, uint activeProcessLimit)
    {
        if (_handle == IntPtr.Zero || _disposed) return false;

        NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = default;
        limits.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (processMemoryLimitBytes > 0)
            limits.BasicLimitInformation.LimitFlags |= NativeMethods.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
        if (activeProcessLimit > 0)
            limits.BasicLimitInformation.LimitFlags |= NativeMethods.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
        limits.ProcessMemoryLimit = (UIntPtr)(ulong)Math.Max(0, processMemoryLimitBytes);
        limits.BasicLimitInformation.ActiveProcessLimit = activeProcessLimit;

        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);
            return NativeMethods.SetInformationJobObject(_handle,
                NativeMethods.JobObjectExtendedLimitInformation, buffer, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Puts a process under this job. Failure is not fatal.</summary>
    public bool Assign(Process process)
    {
        if (_handle == IntPtr.Zero || _disposed) return false;
        try
        {
            return NativeMethods.AssignProcessToJobObject(_handle, process.Handle);
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero) NativeMethods.CloseHandle(_handle);
    }

    internal static class NativeMethods
    {
        internal const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
        internal const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
        internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        internal const int JobObjectExtendedLimitInformation = 9;

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetInformationJobObject(IntPtr job, int infoClass,
            IntPtr info, uint infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    }
}