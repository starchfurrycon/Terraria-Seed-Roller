using System.Collections.Concurrent;
using System.Diagnostics;

namespace TerrariaSeedRoller.Core;

/// <summary>Optional wiring for one roll run.</summary>
public sealed record RollRunOptions
{
    /// <summary>Receives resource-protection notices. They are not per-seed progress.</summary>
    public Action<string>? ResourceLog { get; init; }

    /// <summary>
    /// Continue the most recent interrupted session in the output directory
    /// instead of starting a new one.
    /// </summary>
    public bool Resume { get; init; }

    /// <summary>
    /// Start a new session even when an interrupted one is waiting. Without this
    /// the engine only reports that one is available.
    /// </summary>
    public bool Fresh { get; init; }

    /// <summary>Skip killing servers left behind by an earlier run.</summary>
    public bool SkipOrphanCleanup { get; init; }

    /// <summary>Overrides the resource governor. Used by tests.</summary>
    public ResourceGovernor? Governor { get; init; }
}

/// <summary>
/// Runs a roll: generate candidate worlds, analyse them, keep the best.
/// </summary>
/// <remarks>
/// Two properties matter as much as throughput here. The roll must not take the
/// machine down with it, so every world is gated by <see cref="ResourceGovernor"/>.
/// And an interrupted roll must not be a total loss, so every accepted world is
/// copied out of the volatile work directory and journalled the moment it is
/// analysed; the remaining time is spent only on worlds that are still missing.
/// </remarks>
public sealed class SeedRollerEngine
{
    private readonly TerrariaServerGenerator _generator;
    private readonly WorldAnalyzer _analyzer;

    public SeedRollerEngine(TerrariaServerGenerator? generator = null, WorldAnalyzer? analyzer = null)
    {
        _generator = generator ?? new TerrariaServerGenerator();
        _analyzer = analyzer ?? new WorldAnalyzer();
    }

    public Task<RollSessionResult> RunAsync(GenerationSettings settings, RollProfile profile,
        PauseController? pause = null, IProgress<RollProgress>? progress = null,
        Action<string>? log = null, CancellationToken cancellationToken = default)
        => RunAsync(settings, profile, pause, progress, log, cancellationToken, new RollRunOptions());

    public async Task<RollSessionResult> RunAsync(GenerationSettings settings, RollProfile profile,
        PauseController? pause, IProgress<RollProgress>? progress, Action<string>? log,
        CancellationToken cancellationToken, RollRunOptions options)
    {
        settings.Validate();
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        pause ??= new PauseController();

        string root = Path.GetFullPath(settings.CandidateDirectory);
        Directory.CreateDirectory(root);

        ResourceGovernor governor = options.Governor ?? ResourceGovernor.Shared;
        governor.Configure(settings.Parallelism, settings.MinimumFreeMemoryMb,
            settings.ReservedLogicalProcessors, settings.ServerMemoryLimitMb);
        void OnResource(string message) => (options.ResourceLog ?? log)?.Invoke(message);
        governor.ResourceLog += OnResource;
        try
        {
            return await RunCoreAsync(settings, profile, pause, progress, log, cancellationToken,
                options, root, governor).ConfigureAwait(false);
        }
        finally
        {
            governor.ResourceLog -= OnResource;
            // Only ever clears this instance's own lines, and only when it has no
            // servers left, so a concurrent run is never affected.
            governor.ReleaseServerPidFile(root);
        }
    }

    private async Task<RollSessionResult> RunCoreAsync(GenerationSettings settings, RollProfile profile,
        PauseController pause, IProgress<RollProgress>? progress, Action<string>? log,
        CancellationToken cancellationToken, RollRunOptions options, string root,
        ResourceGovernor governor)
    {
        DateTimeOffset started = DateTimeOffset.Now;
        Stopwatch elapsed = Stopwatch.StartNew();

        if (!options.SkipOrphanCleanup)
        {
            int orphans = ResourceGovernor.KillOrphanedServers(root);
            if (orphans > 0) log?.Invoke($"已清理上次中断遗留的 {orphans} 个后台 TerrariaServer 进程。");
            int stale = TerrariaServerGenerator.CleanOrphanedWorkDirectories(root);
            if (stale > 0) log?.Invoke($"已清理上次中断遗留的 {stale} 个临时生成目录。");
        }

        // ---- pick the session -------------------------------------------------
        InterruptedRoll? resumable = RollRecovery.FindLatest(root);
        InterruptedRoll? resuming = null;
        if (options.Resume && resumable is not null)
        {
            RollRecovery.EnsureCompatible(resumable, settings);
            resuming = resumable;
        }
        else if (resumable is not null && !options.Fresh)
        {
            log?.Invoke($"检测到被中断的会话：{resumable.Describe()}。" +
                "使用 roll --resume 继续，或 --fresh 忽略它开始新会话。");
        }

        string sessionDirectory = resuming?.SessionDirectory ?? UniqueSessionDirectory(root, started, profile);
        Directory.CreateDirectory(sessionDirectory);
        RollJournal journal = new(sessionDirectory);

        // A resumed session must re-enumerate the same seed order, so the
        // enumeration seed is persisted with the session state and reused.
        int? enumerationSeed = resuming is null
            ? settings.EnumerationSeed ?? Random.Shared.Next()
            : RollJournal.ReadState(sessionDirectory)?.EnumerationSeed ?? settings.EnumerationSeed ?? Random.Shared.Next();
        GenerationSettings effective = settings with { EnumerationSeed = enumerationSeed };

        RollJournalContents contents = resuming is null
            ? new RollJournalContents()
            : RollJournal.Read(sessionDirectory);

        object winnersGate = new();
        List<Candidate> top = [];
        ConcurrentBag<string> failures = [];
        int attempted = contents.Finished.Count;
        int completed = contents.Finished.Count;
        int accepted = contents.Finished.Count(f => f.Outcome == RollAttemptOutcome.Accepted);
        int failed = contents.Finished.Count(f => f.Outcome == RollAttemptOutcome.Failed);
        bool cancelled = false;
        bool crashed = false;

        // Restore what the interrupted run had already accepted, so its staged
        // worlds stay in the ranking without being generated a second time.
        if (resuming is not null)
        {
            foreach (RollAttemptRecord record in contents.Survivors)
            {
                top.Add(new Candidate(record.Seed, record.CopiedSeed, record.Score,
                    record.WorldPath!, record.AttemptDirectory,
                    TimeSpan.FromSeconds(record.GenerationSeconds),
                    TimeSpan.FromSeconds(record.AnalysisSeconds), Restored: true));
            }
            top.Sort(CompareCandidates);
            TrimTop(top, effective.WinnersToKeep);
            log?.Invoke($"继续会话 {resuming.Name}：已有 {attempted} 次尝试，" +
                $"保留 {top.Count} 个入围世界。");
        }
        else
        {
            log?.Invoke($"资源保护：并发上限 {effective.EffectiveParallelism}" +
                $"（预留 {settings.ReservedLogicalProcessors} 个逻辑处理器），" +
                $"内存保留线 {settings.MinimumMemoryFloorDescription()}");
        }

        HashSet<int> finishedSeeds = [.. contents.FinishedSeeds];
        RollSessionState state = BuildState(sessionDirectory, profile, SessionOutcome.Running,
            attempted, completed, failed, accepted, started, effective.Generation(), enumerationSeed);
        journal.WriteState(state);

        string stagingDirectory = Path.Combine(sessionDirectory, "winners", "staging");
        string rejectedDirectory = Path.Combine(sessionDirectory, "rejected");

        // A crash must still leave a truthful state file behind.
        using CrashFlushScope crashScope = new(() =>
        {
            crashed = true;
            try
            {
                journal.WriteState(BuildState(sessionDirectory, profile, SessionOutcome.Failed,
                    attempted, completed, failed, accepted, started, effective.Generation(), enumerationSeed));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });

        // Drop the exact attempts the interrupted run left half-done. The journal
        // names them, so this does not have to guess by age.
        if (resuming is not null)
        {
            int dropped = 0;
            // The journal can name the same directory more than once (a pending
            // record is written before generation and again once it starts), so
            // duplicates are collapsed before counting.
            foreach (string orphan in contents.OrphanedDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (TerrariaServerGenerator.DeleteAttemptDirectory(orphan, root)) dropped++;
                }
                catch (InvalidOperationException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (dropped > 0) log?.Invoke($"已清理上次中断遗留的 {dropped} 个未完成生成目录。");
        }

        IEnumerable<int> seeds = effective.Seeds.Enumerate(enumerationSeed)
            .Where(seed => !finishedSeeds.Contains(seed))
            .Take(Math.Max(0, effective.MaximumAttempts - attempted));

        using SemaphoreSlim slots = new(effective.EffectiveParallelism, effective.EffectiveParallelism);

        try
        {
            await Parallel.ForEachAsync(seeds, new ParallelOptions
            {
                MaxDegreeOfParallelism = effective.EffectiveParallelism,
                CancellationToken = cancellationToken
            }, async (seed, token) =>
            {
                await pause.WaitIfPausedAsync(token).ConfigureAwait(false);
                // Gate starting new work on the resource reserve. In-flight work
                // is never interrupted by this.
                await governor.WaitForCapacityAsync(token).ConfigureAwait(false);
                await slots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await RunAttemptAsync(seed, token).ConfigureAwait(false);
                }
                finally
                {
                    slots.Release();
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            log?.Invoke("任务已取消，正在保存已经完成的入围结果。");
        }

        elapsed.Stop();
        List<RollResult> winners = FinalizeTop(top, profile, effective, sessionDirectory, log);

        if (failures.Count > 0)
            await File.WriteAllLinesAsync(Path.Combine(sessionDirectory, "failures.log"), failures,
                CancellationToken.None).ConfigureAwait(false);

        SessionOutcome outcome = cancelled ? SessionOutcome.Cancelled
            : crashed ? SessionOutcome.Failed
            : SessionOutcome.Completed;
        journal.WriteState(BuildState(sessionDirectory, profile, outcome,
            attempted, completed, failed, accepted, started, effective.Generation(), enumerationSeed));

        RollSessionResult session = new(started, DateTimeOffset.Now, attempted, completed, failed,
            cancelled, winners.AsReadOnly(), sessionDirectory,
            Resumed: resuming is not null, Outcome: outcome, ResourceSummary: governor.BuildSummary());
        ReportWriter.WriteSession(session, profile, Path.Combine(sessionDirectory, "session.json"));
        TryDeleteEmptyWorkRoot(root);
        return session;

        async Task RunAttemptAsync(int seed, CancellationToken token)
        {
            int currentAttempt = Interlocked.Increment(ref attempted);
            Report(seed, "生成", null, $"开始第 {currentAttempt} 个种子");
            GeneratedWorld? generated = null;
            journal.AppendPending(new RollPendingRecord(seed, effective.BuildCopiedSeed(seed),
                null, DateTimeOffset.Now, governor.Snapshot.State.ToString()));
            try
            {
                generated = await _generator.GenerateAsync(effective, seed, log, token, governor)
                    .ConfigureAwait(false);
                // Record where the work is so a kill during analysis can still be
                // cleaned up on the next run.
                journal.AppendPending(new RollPendingRecord(seed, generated.CopiedSeed,
                    generated.AttemptDirectory, DateTimeOffset.Now, governor.Snapshot.State.ToString()));

                await pause.WaitIfPausedAsync(token).ConfigureAwait(false);
                Report(seed, "分析", null, "只读解析世界并计算指标");
                Stopwatch analysisWatch = Stopwatch.StartNew();
                WorldAnalysis analysis = await Task.Run(
                    () => _analyzer.Analyze(generated.WorldPath, profile, token), token).ConfigureAwait(false);
                analysisWatch.Stop();
                WorldEvaluation evaluation = analysis.Evaluation ??
                    throw new InvalidOperationException("Profile evaluation was not produced.");

                if (!evaluation.PassedHardCriteria)
                {
                    string? durable = null;
                    if (settings.KeepRejectedWorlds)
                    {
                        durable = MoveRejected(generated, rejectedDirectory);
                    }
                    else
                    {
                        TerrariaServerGenerator.DeleteAttemptDirectory(generated.AttemptDirectory, root);
                    }
                    journal.Append(new RollAttemptRecord(seed, generated.CopiedSeed,
                        RollAttemptOutcome.Rejected, evaluation.Score, durable, null,
                        generated.Duration.TotalSeconds, analysisWatch.Elapsed.TotalSeconds,
                        null, governor.Snapshot.State.ToString()));
                    Interlocked.Increment(ref completed);
                    Report(seed, "淘汰", evaluation.Score, "未通过硬条件");
                    return;
                }

                // Copy the world out of _work before anything else can delete it.
                // This is what makes an interruption lose at most the worlds that
                // were still in flight.
                string staged = StageWorld(generated.WorldPath, stagingDirectory, seed);
                journal.Append(new RollAttemptRecord(seed, generated.CopiedSeed,
                    RollAttemptOutcome.Accepted, evaluation.Score, staged, null,
                    generated.Duration.TotalSeconds, analysisWatch.Elapsed.TotalSeconds,
                    null, governor.Snapshot.State.ToString()));

                Candidate? dropped = null;
                lock (winnersGate)
                {
                    top.Add(new Candidate(seed, generated.CopiedSeed, evaluation.Score, staged,
                        null, generated.Duration, analysisWatch.Elapsed, Restored: false));
                    top.Sort(CompareCandidates);
                    if (top.Count > effective.WinnersToKeep)
                    {
                        dropped = top[^1];
                        top.RemoveAt(top.Count - 1);
                    }
                }
                if (dropped.HasValue) DeleteStaged(dropped.Value.WorldPath);
                TerrariaServerGenerator.DeleteAttemptDirectory(generated.AttemptDirectory, root);
                Interlocked.Increment(ref accepted);
                Interlocked.Increment(ref completed);
                Report(seed, "入围", evaluation.Score, "通过硬条件并进入当前排名");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                if (generated is not null)
                    TryDeleteAttempt(generated.AttemptDirectory, root);
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                Interlocked.Increment(ref completed);
                failures.Add($"Seed {seed}: {ex.Message}");
                log?.Invoke($"种子 {seed} 失败: {ex}");
                journal.Append(new RollAttemptRecord(seed, effective.BuildCopiedSeed(seed),
                    RollAttemptOutcome.Failed, 0, null, null, 0, 0, ex.Message,
                    governor.Snapshot.State.ToString()));
                if (generated is not null)
                    TryDeleteAttempt(generated.AttemptDirectory, root);
                Report(seed, "失败", null, ex.Message);
            }
        }

        void Report(int seed, string stage, double? score, string message)
        {
            progress?.Report(new RollProgress(attempted, completed, accepted, failed,
                effective.MaximumAttempts, seed, stage, elapsed.Elapsed, score, message));
        }
    }

    private static RollSessionState BuildState(string sessionDirectory, RollProfile profile,
        SessionOutcome outcome, int attempted, int completed, int failed, int accepted,
        DateTimeOffset started, RollGenerationFingerprint generation, int? enumerationSeed) =>
        new(sessionDirectory, profile.Name, outcome, attempted, completed, failed, accepted,
            started, DateTimeOffset.Now, generation, enumerationSeed);

    private static int CompareCandidates(Candidate left, Candidate right) =>
        right.Score.CompareTo(left.Score);

    private static void TrimTop(List<Candidate> top, int winnersToKeep)
    {
        while (top.Count > winnersToKeep)
        {
            Candidate dropped = top[^1];
            top.RemoveAt(top.Count - 1);
            DeleteStaged(dropped.WorldPath);
        }
    }

    /// <summary>
    /// Moves the staged worlds into their final place and writes the reports.
    /// Every winner is analysed again here, which is also what lets a resumed
    /// session produce full reports for worlds it only had a score for.
    /// </summary>
    private List<RollResult> FinalizeTop(List<Candidate> top, RollProfile profile,
        GenerationSettings settings, string sessionDirectory, Action<string>? log)
    {
        List<Candidate> finalTop;
        lock (top) finalTop = [.. top];
        finalTop.Sort(CompareCandidates);

        List<RollResult> winners = [];
        string winnersDirectory = Path.Combine(sessionDirectory, "winners");
        Directory.CreateDirectory(winnersDirectory);
        int rank = 0;
        foreach (Candidate candidate in finalTop)
        {
            rank++;
            string baseName = $"{rank:00}_seed_{candidate.Seed}_score_{candidate.Score:0.##}";
            string finalWorld = UniquePath(Path.Combine(winnersDirectory, baseName + ".wld"));
            try
            {
                if (candidate.Restored)
                {
                    // Staged worlds are the durable copy, so they are moved rather
                    // than re-analysed from a temporary path that no longer exists.
                    if (!File.Exists(candidate.WorldPath))
                    {
                        log?.Invoke($"种子 {candidate.Seed}: 暂存世界已丢失，跳过。");
                        continue;
                    }
                    File.Move(candidate.WorldPath, finalWorld, overwrite: false);
                }
                else
                {
                    File.Move(candidate.WorldPath, finalWorld, overwrite: false);
                }
            }
            catch (IOException ex)
            {
                log?.Invoke($"种子 {candidate.Seed}: 无法保存世界（{ex.Message}）。");
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                log?.Invoke($"种子 {candidate.Seed}: 无法保存世界（{ex.Message}）。");
                continue;
            }

            WorldAnalysis analysis = _analyzer.Analyze(finalWorld, profile);
            WorldAnalysis finalAnalysis = analysis with { WorldPath = finalWorld };
            string reportDirectory = Path.Combine(winnersDirectory,
                Path.GetFileNameWithoutExtension(finalWorld));
            ReportWriter.Write(finalAnalysis, reportDirectory, candidate.CopiedSeed);
            winners.Add(new RollResult(candidate.Seed, candidate.CopiedSeed, finalWorld,
                candidate.GenerationDuration, candidate.AnalysisDuration, finalAnalysis));
        }
        CleanStaging(Path.Combine(winnersDirectory, "staging"));
        return winners;
    }

    /// <summary>Copies a world out of the volatile work directory.</summary>
    private static string StageWorld(string worldPath, string stagingDirectory, int seed)
    {
        Directory.CreateDirectory(stagingDirectory);
        string destination = UniquePath(Path.Combine(stagingDirectory,
            $"seed_{seed}_{Guid.NewGuid():N}.wld"));
        File.Copy(worldPath, destination, overwrite: false);
        return destination;
    }

    private static void DeleteStaged(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void CleanStaging(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory) &&
                !Directory.EnumerateFileSystemEntries(stagingDirectory).Any())
                Directory.Delete(stagingDirectory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteAttempt(string attemptDirectory, string candidateDirectory)
    {
        try { TerrariaServerGenerator.DeleteAttemptDirectory(attemptDirectory, candidateDirectory); }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Moves a rejected world into the session's rejected directory.</summary>
    private static string MoveRejected(GeneratedWorld generated, string rejectedDirectory)
    {
        Directory.CreateDirectory(rejectedDirectory);
        string destination = UniquePath(Path.Combine(rejectedDirectory,
            Path.GetFileName(generated.AttemptDirectory)));
        Directory.Move(generated.AttemptDirectory, destination);
        return Path.Combine(destination, Path.GetFileName(generated.WorldPath));
    }

    private static string UniqueSessionDirectory(string root, DateTimeOffset started, RollProfile profile) =>
        UniquePath(Path.Combine(root, $"roll_{started:yyyyMMdd_HHmmss}_{Sanitize(profile.Name)}"));

    private static string Sanitize(string text)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(text) ? "custom" : text;
    }

    private static string UniquePath(string desired)
    {
        if (!File.Exists(desired) && !Directory.Exists(desired)) return desired;
        string directory = Path.GetDirectoryName(desired)!;
        string extension = Path.GetExtension(desired);
        string name = Path.GetFileNameWithoutExtension(desired);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static void TryDeleteEmptyWorkRoot(string candidateDirectory)
    {
        string work = Path.GetFullPath(Path.Combine(candidateDirectory, "_work"));
        try
        {
            if (Directory.Exists(work) && !Directory.EnumerateFileSystemEntries(work).Any())
                Directory.Delete(work);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>One candidate in the running ranking.</summary>
    private readonly record struct Candidate(
        int Seed,
        string CopiedSeed,
        double Score,
        string WorldPath,
        string? AttemptDirectory,
        TimeSpan GenerationDuration,
        TimeSpan AnalysisDuration,
        bool Restored);

    /// <summary>
    /// Writes a truthful state file when the process is torn down. Only one
    /// handler is registered at a time, and it is removed when the run settles.
    /// </summary>
    private sealed class CrashFlushScope : IDisposable
    {
        private readonly Action _flush;
        private readonly EventHandler _handler;
        private bool _disposed;

        public CrashFlushScope(Action flush)
        {
            _flush = flush;
            _handler = (_, _) => _flush();
            AppDomain.CurrentDomain.ProcessExit += _handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AppDomain.CurrentDomain.ProcessExit -= _handler;
        }
    }
}

internal static class GenerationSettingsExtensions
{
    public static RollGenerationFingerprint Generation(this GenerationSettings settings) =>
        RollGenerationFingerprint.From(settings);

    public static string MinimumMemoryFloorDescription(this GenerationSettings settings) =>
        $"{settings.MinimumFreeMemoryMb} MB";
}