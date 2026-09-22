using System.Diagnostics;

namespace TerrariaSeedRoller.Core;

/// <summary>How much of its own budget the roll is currently allowed to use.</summary>
public enum ResourceGuardState
{
    /// <summary>Nothing is contended; the configured parallelism applies.</summary>
    Healthy,

    /// <summary>Resources are getting tight; concurrency is halved.</summary>
    Reduced,

    /// <summary>The reserve was breached; no new world may start.</summary>
    Reserved
}

/// <summary>One sampling of the machine's resources.</summary>
public sealed record ResourceSnapshot(
    long AvailableMemoryMb,
    int WorkerThreadsFree,
    long MemoryFloorMb,
    int ParallelismCap,
    ResourceGuardState State,
    string Reason)
{
    public bool IsHealthy => State == ResourceGuardState.Healthy;
}

/// <summary>
/// Protects the resources this tool needs to finish what it started.
/// </summary>
/// <remarks>
/// <para>
/// A roll runs for a long time and is worthless if it dies near the end, so the
/// guard draws a line under the resources the tool itself needs: below it no new
/// world is started, and below that line minus a hysteresis band the servers this
/// roll owns are parked so the analyser and the save path can still run.
/// </para>
/// <para>
/// Two rules keep this from doing harm. In-flight work is never killed; the guard
/// only gates <em>starting</em> new worlds. And the configured memory floor is
/// clamped to a quarter of physical memory, so a large floor on a small machine
/// cannot deadlock the roll.
/// </para>
/// </remarks>
public sealed class ResourceGovernor : IDisposable
{
    /// <summary>How far above the floor resources must recover before work resumes.</summary>
    private const int HysteresisMb = 256;

    /// <summary>Worker threads that must stay free before new work is allowed.</summary>
    private const int MinimumWorkerHeadroom = 4;

    /// <summary>Above this many pool threads the pool is considered saturated.</summary>
    private const int WorkerPoolSaturationThreshold = 32;

    private static readonly Lazy<ResourceGovernor> LazyShared = new(() => new ResourceGovernor());

    private readonly object _gate = new();
    private readonly HashSet<int> _ownedServerPids = [];
    private readonly Dictionary<int, List<IntPtr>> _suspendedThreads = [];
    private readonly OwnedProcessJob? _job;
    private readonly bool _ownsJob;

    private ResourceSnapshot _snapshot = new(0, 0, 0, 1, ResourceGuardState.Healthy, "尚未采样");
    private int _parallelismOverride = 1;
    private long _memoryFloorOverrideMb;
    private int _suspendRequests;
    private bool _forcedPause;
    private bool _disposed;
    private DateTime _lastPollUtc = DateTime.MinValue;
    private bool _loggedPressure;

    public ResourceGovernor()
        : this(requestedParallelism: 1, memoryFloorMb: 0, protectProcessPriority: false)
    {
    }

    public ResourceGovernor(int requestedParallelism, long memoryFloorMb, bool protectProcessPriority)
    {
        _job = OwnedProcessJob.Create();
        _ownsJob = _job is not null;
        _parallelismOverride = Math.Max(1, requestedParallelism);
        _memoryFloorOverrideMb = Math.Max(0, memoryFloorMb);
        if (protectProcessPriority) ProtectCurrentProcess();
        Poll(force: true);
    }

    /// <summary>The governor shared by the process. One roll runs at a time.</summary>
    public static ResourceGovernor Shared => LazyShared.Value;

    /// <summary>The id written next to each owned pid so one run can clean up after another.</summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Raised when the guard changes what the roll is allowed to do.</summary>
    public event Action<string>? ResourceLog;

    /// <summary>Times the roll had to wait or park servers before starting new work.</summary>
    public int ThrottleCount { get; private set; }

    /// <summary>Times servers were parked because the reserve was breached.</summary>
    public int PauseCount { get; private set; }

    /// <summary>Total time spent waiting for resources.</summary>
    public long ThrottledMilliseconds { get; private set; }

    public ResourceSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    /// <summary>
    /// Declares the parallelism, memory reserve and per-world memory ceiling for
    /// one roll. The memory floor is additionally bounded by a quarter of the
    /// machine's memory so a very large configured floor cannot deadlock the roll.
    /// </summary>
    public void Configure(int requestedParallelism, long memoryFloorMb, int reservedLogicalProcessors,
        int serverMemoryLimitMb = 0)
    {
        int processors = Math.Max(1, Environment.ProcessorCount - Math.Max(0, reservedLogicalProcessors));
        lock (_gate)
        {
            _parallelismOverride = Math.Max(1, Math.Min(Math.Max(1, requestedParallelism), processors));
            _memoryFloorOverrideMb = memoryFloorMb <= 0 ? 0 : Math.Min(memoryFloorMb, TotalMemoryMb() / 4);
        }
        if (serverMemoryLimitMb > 0)
        {
            // A ceiling below 256 MB would stop vanilla generating at all.
            long bytes = (long)Math.Max(256, serverMemoryLimitMb) * 1024 * 1024;
            _job?.ApplyLimits(bytes, activeProcessLimit: 0);
        }
        Poll(force: true);
    }

    /// <summary>Blocks until another world may be started, or the token fires.</summary>
    public async Task WaitForCapacityAsync(CancellationToken token)
    {
        bool reported = false;
        Stopwatch waited = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                ResourceSnapshot snapshot = Poll(force: false);
                if (snapshot.State != ResourceGuardState.Reserved && !IsForcedPause)
                {
                    if (reported)
                        Log($"资源保护：资源已恢复到 {snapshot.AvailableMemoryMb} MB 可用内存，继续启动新世界。");
                    return;
                }
                if (!reported)
                {
                    reported = true;
                    ThrottleCount++;
                    Log($"资源保护：{snapshot.Reason}；暂停启动新世界，已完成的结果不受影响。");
                    // Waiting alone is not enough when the machine is genuinely
                    // out of memory: park the servers this roll owns so the
                    // analyser and the save path can still get memory and CPU.
                    SuspendServers("内存低于保留线");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (reported)
            {
                ThrottledMilliseconds += waited.ElapsedMilliseconds;
                ResumeServers();
            }
        }
    }

    /// <summary>Stops the roll from starting new worlds until <see cref="ClearForcePause"/>.</summary>
    public void ForcePause(string reason)
    {
        lock (_gate)
        {
            if (_forcedPause) return;
            _forcedPause = true;
        }
        ThrottleCount++;
        Log($"资源保护：{reason}");
    }

    public void ClearForcePause()
    {
        lock (_gate) _forcedPause = false;
    }

    /// <summary>True while new worlds must not start.</summary>
    public bool IsThrottled => IsForcedPause || Snapshot.State == ResourceGuardState.Reserved;

    private bool IsForcedPause { get { lock (_gate) return _forcedPause; } }

    /// <summary>
    /// Parks the servers this roll owns so an analysis pass or a save can use the
    /// whole machine. Suspensions are reference counted, so several workers
    /// waiting on the reserve do not stack them.
    /// </summary>
    public void SuspendServers(string reason)
    {
        List<int> pids;
        lock (_gate)
        {
            _suspendRequests++;
            pids = [.. _ownedServerPids];
        }
        if (pids.Count == 0) return;
        PauseCount++;
        Log($"资源保护：{reason}；暂停 {pids.Count} 个服务端进程。");
        foreach (int pid in pids) SuspendServer(pid);
    }

    public void ResumeServers()
    {
        List<int> pids;
        lock (_gate)
        {
            if (_suspendRequests > 0) _suspendRequests--;
            if (_suspendRequests > 0) return;
            pids = [.. _suspendedThreads.Keys];
            _suspendedThreads.Clear();
        }
        foreach (int pid in pids) ResumeServer(pid);
    }

    private void SuspendServer(int pid)
    {
        if (!OperatingSystem.IsWindows()) return;
        List<IntPtr> handles = [];
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited) return;
            foreach (ProcessThread thread in process.Threads)
            {
                IntPtr handle = NativeMethods.OpenThread(
                    NativeMethods.ThreadSuspendResume, false, (uint)thread.Id);
                if (handle == IntPtr.Zero) continue;
                // SuspendThread is counted: resume exactly as many times as this
                // succeeded, so a thread is never left parked forever.
                if (NativeMethods.SuspendThread(handle) != unchecked((uint)-1)) handles.Add(handle);
                else NativeMethods.CloseHandle(handle);
            }
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }

        lock (_gate)
        {
            // Register even an empty list so the resume path still runs and the
            // reference count stays balanced.
            if (_suspendedThreads.TryGetValue(pid, out List<IntPtr>? existing))
            {
                existing.AddRange(handles);
                handles = existing;
            }
            else _suspendedThreads[pid] = handles;
        }
    }

    private void ResumeServer(int pid)
    {
        List<IntPtr>? handles;
        lock (_gate) handles = _suspendedThreads.TryGetValue(pid, out List<IntPtr>? found) ? found : null;
        if (handles is null) return;
        foreach (IntPtr handle in handles)
        {
            NativeMethods.ResumeThread(handle);
            NativeMethods.CloseHandle(handle);
        }
    }

    private void TryResume(int pid)
    {
        bool had;
        lock (_gate)
        {
            had = _suspendedThreads.Remove(pid);
            if (had) _suspendRequests = 0;
        }
        if (had) ResumeServer(pid);
    }

    // ---- owned server tracking ----------------------------------------------

    /// <summary>
    /// Registers a server this process started, both in the kill-on-close job and
    /// in an on-disk pid file so a later run can clean up after a hard crash.
    /// </summary>
    public void RegisterServer(Process process, string? pidFileDirectory)
    {
        bool suspend;
        try
        {
            _job?.Assign(process);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (PlatformNotSupportedException) { }

        if (!string.IsNullOrWhiteSpace(pidFileDirectory))
        {
            try
            {
                Directory.CreateDirectory(pidFileDirectory);
                File.AppendAllText(Path.Combine(pidFileDirectory, ServerPidFileName),
                    $"{process.Id} {InstanceId}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        lock (_gate)
        {
            _ownedServerPids.Add(process.Id);
            // A server that starts while the reserve is breached must not undo
            // the protection the waiting workers asked for.
            suspend = _suspendRequests > 0;
        }
        if (suspend) SuspendServer(process.Id);
    }

    public void UnregisterServer(int pid)
    {
        bool resume;
        lock (_gate)
        {
            _ownedServerPids.Remove(pid);
            resume = _suspendedThreads.ContainsKey(pid);
        }
        // A process that is parked when it exits would otherwise never be reaped.
        if (resume) TryResume(pid);
    }

    /// <summary>
    /// Removes this instance's lines from the pid file once its servers have all
    /// exited, so the file only ever describes servers that are still alive.
    /// </summary>
    public void ReleaseServerPidFile(string? pidFileDirectory)
    {
        if (string.IsNullOrWhiteSpace(pidFileDirectory)) return;
        string path = Path.Combine(Path.GetFullPath(pidFileDirectory), ServerPidFileName);
        lock (_gate)
        {
            if (_ownedServerPids.Count > 0) return;
            try
            {
                if (!File.Exists(path)) return;
                string[] remaining = [.. File.ReadAllLines(path)
                    .Where(line => !line.TrimEnd().EndsWith(InstanceId, StringComparison.Ordinal))
                    .Where(line => !string.IsNullOrWhiteSpace(line))];
                if (remaining.Length == 0) File.Delete(path);
                else File.WriteAllLines(path, remaining);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public const string ServerPidFileName = "server-pids.txt";

    /// <summary>
    /// Kills servers a previous run left behind. An interrupted roll can leave
    /// orphaned TerrariaServer processes that keep consuming memory and CPU, which
    /// is exactly the pressure that made the roll stall in the first place.
    /// </summary>
    public static int KillOrphanedServers(string directory, int timeoutMilliseconds = 4000)
    {
        string path = Path.Combine(Path.GetFullPath(directory), ServerPidFileName);
        if (!File.Exists(path)) return 0;
        List<int> pids = [];
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                // Lines are "pid instance"; older files may hold a bare pid.
                string first = line.Trim().Split(' ', 2)[0];
                if (int.TryParse(first, out int pid) && pid > 0 && !pids.Contains(pid)) pids.Add(pid);
            }
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }

        List<Process> owned = [];
        int current = Environment.ProcessId;
        foreach (int pid in pids)
        {
            if (pid == current) continue;
            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.HasExited) continue;
                // A pid can be reused after the original process died. Only a
                // Terraria server may ever be terminated here.
                if (!IsTerrariaServer(process)) continue;
                owned.Add(process);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        int killed = 0;
        try
        {
            foreach (Process process in owned)
            {
                try
                {
                    if (process.HasExited) continue;
                    process.Kill(entireProcessTree: true);
                    killed++;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (killed > 0)
            {
                Stopwatch watch = Stopwatch.StartNew();
                foreach (Process process in owned)
                {
                    int remaining = timeoutMilliseconds - (int)watch.ElapsedMilliseconds;
                    if (remaining <= 0) break;
                    try { process.WaitForExit(remaining); }
                    catch (InvalidOperationException) { }
                }
            }
        }
        finally
        {
            foreach (Process process in owned) process.Dispose();
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return killed;
    }

    // ---- sampling -----------------------------------------------------------

    /// <summary>
    /// True when a process looks like the vanilla server this tool starts. Checked
    /// before terminating anything recovered from a pid file, so a recycled pid
    /// can never take down an unrelated program.
    /// </summary>
    private static bool IsTerrariaServer(Process process)
    {
        try
        {
            string? name = process.ProcessName;
            return name is not null &&
                name.Contains("TerrariaServer", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    public ResourceSnapshot Poll(bool force)
    {
        lock (_gate)
        {
            if (_disposed) return _snapshot;
            DateTime now = DateTime.UtcNow;
            if (!force && (now - _lastPollUtc).TotalMilliseconds < 1000) return _snapshot;
            _lastPollUtc = now;

            long availableMb = AvailableMemoryMb();
            ThreadPool.GetAvailableThreads(out int freeWorkers, out _);
            ThreadPool.GetMaxThreads(out int maxWorkers, out _);
            long floor = _memoryFloorOverrideMb;

            bool memoryLow = floor > 0 && availableMb < floor;
            bool memoryCritical = floor > 0 && availableMb < Math.Max(1, floor - HysteresisMb);
            bool workersStarved = maxWorkers >= WorkerPoolSaturationThreshold &&
                freeWorkers < MinimumWorkerHeadroom;

            ResourceGuardState state;
            string reason;
            if (memoryCritical)
            {
                state = ResourceGuardState.Reserved;
                reason = $"可用内存 {availableMb} MB 已低于保留线 {floor} MB";
            }
            else if (memoryLow || workersStarved)
            {
                state = ResourceGuardState.Reduced;
                reason = memoryLow
                    ? $"可用内存 {availableMb} MB 接近保留线 {floor} MB"
                    : $"线程池空闲工作线程仅剩 {freeWorkers} 个";
            }
            else
            {
                state = ResourceGuardState.Healthy;
                reason = $"可用内存 {availableMb} MB / 保留 {floor} MB，空闲工作线程 {freeWorkers}";
            }

            int cap = state switch
            {
                ResourceGuardState.Reserved => 1,
                ResourceGuardState.Reduced => Math.Max(1, _parallelismOverride / 2),
                _ => _parallelismOverride
            };
            cap = Math.Max(1, Math.Min(cap, _parallelismOverride));

            _snapshot = new ResourceSnapshot(availableMb, freeWorkers, floor, cap, state, reason);

            // Report each transition once, not once per sample.
            bool pressured = state != ResourceGuardState.Healthy;
            if (pressured != _loggedPressure)
            {
                _loggedPressure = pressured;
                Log($"资源状态 {(state == ResourceGuardState.Healthy ? "恢复" : "变化")}：{reason}，并发上限 {cap}");
            }
            return _snapshot;
        }
    }

    public string Describe() => $"资源状态 {StateText(Snapshot.State)}：{Snapshot.Reason}，并发上限 {Snapshot.ParallelismCap}";

    public string BuildSummary()
    {
        ResourceSnapshot snapshot = Snapshot;
        return $"资源保护统计：节流 {ThrottleCount} 次，暂停服务端 {PauseCount} 次，" +
            $"累计等待 {ThrottledMilliseconds / 1000.0:0.0} 秒；{Describe()}";
    }

    /// <summary>Raises this process above normal so it is not starved by its own servers.</summary>
    public static void ProtectCurrentProcess()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            if (self.PriorityClass is ProcessPriorityClass.High or ProcessPriorityClass.RealTime) return;
            self.PriorityClass = ProcessPriorityClass.AboveNormal;
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (PlatformNotSupportedException) { }
    }

    /// <summary>Total physical memory of the machine, in megabytes.</summary>
    public static long TotalMemoryMb()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        OwnedProcessJob.NativeMethods.MEMORYSTATUSEX status = default;
        status.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<
            OwnedProcessJob.NativeMethods.MEMORYSTATUSEX>();
        if (!OwnedProcessJob.NativeMethods.GlobalMemoryStatusEx(ref status)) return 0;
        return (long)(status.ullTotalPhys / (1024 * 1024));
    }

    /// <summary>
    /// Machine-wide available physical memory, in megabytes. This deliberately
    /// measures the whole machine rather than this process: the pressure that
    /// stalls a roll comes from everywhere, not only from the roll itself.
    /// </summary>
    public static long AvailableMemoryMb()
    {
        if (!OperatingSystem.IsWindows())
        {
            long fallback = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return fallback <= 0 ? 0 : fallback / (1024 * 1024);
        }
        OwnedProcessJob.NativeMethods.MEMORYSTATUSEX status = default;
        status.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<
            OwnedProcessJob.NativeMethods.MEMORYSTATUSEX>();
        if (!OwnedProcessJob.NativeMethods.GlobalMemoryStatusEx(ref status)) return 0;
        return (long)(status.ullAvailPhys / (1024 * 1024));
    }

    private static string StateText(ResourceGuardState state) => state switch
    {
        ResourceGuardState.Reserved => "保留",
        ResourceGuardState.Reduced => "降速",
        _ => "正常"
    };

    private void Log(string message) => ResourceLog?.Invoke(message);

    public void Dispose()
    {
        List<int> pids;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            pids = [.. _ownedServerPids];
            _suspendedThreads.Clear();
            _suspendRequests = 0;
        }

        // A roll that is being torn down must not leave servers holding memory.
        foreach (int pid in pids)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.HasExited || !IsTerrariaServer(process)) continue;
                process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        if (_ownsJob) _job?.Dispose();
    }

    private static class NativeMethods
    {
        internal const uint ThreadSuspendResume = 0x0002;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SuspendThread(IntPtr thread);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint ResumeThread(IntPtr thread);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}