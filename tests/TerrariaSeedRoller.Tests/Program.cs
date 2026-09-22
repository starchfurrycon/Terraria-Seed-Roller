using System.Security.Cryptography;
using System.Text;
using TerrariaSeedRoller.Core;

List<(string Name, Action Test)> tests =
[
    ("SeedRange ordered reaches Int32.MaxValue", TestOrderedRange),
    ("SeedRange random full Int32 range does not overflow", TestRandomRange),
    ("Copied seed encodes all generation choices", TestCopiedSeed),
    ("Metric catalogue keys are unique", TestMetricCatalogue),
    ("Profile evaluator handles hard and weighted criteria", TestEvaluator),
    ("Built-in profiles only use valid metrics", TestProfiles),
    ("Resource settings validate and reserve cores", TestResourceSettings),
    ("Resource governor reports pressure and clamps parallelism", TestResourceGovernor),
    ("Resource governor reserves memory under real pressure", TestResourceGovernorPressure),
    ("Journal round-trips pending and finished attempts", TestJournalRoundTrip),
    ("Journal drops a torn final line instead of the whole file", TestJournalTornLine),
    ("Session state survives an atomic replace", TestSessionState),
    ("Recovery finds only interrupted sessions", TestRecoveryDetection),
    ("Recovery refuses mismatched generation parameters", TestRecoveryCompatibility),
    ("Orphan cleanup never kills a recycled pid", TestOrphanCleanupSafety),
    ("Stale work directories are reclaimed without touching live ones", TestStaleWorkCleanup)
];

string? repository = FindRepository();
string? fixture = args.FirstOrDefault(a => a.EndsWith(".wld", StringComparison.OrdinalIgnoreCase));
fixture ??= Environment.GetEnvironmentVariable("TSR_TEST_WORLD");
fixture ??= repository is null ? null : Path.Combine(repository, "_research", "worldgen-test", "RollTest.wld");
if (fixture is not null && File.Exists(fixture))
    tests.Add(("Terraria 1.4.5 world read-only integration", () => TestWorld(fixture)));

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine($"PASS  {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL  {name}\n      {ex.Message}"); }
}
Console.WriteLine($"\n{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

static void TestOrderedRange()
{
    int[] values = new SeedRange(int.MaxValue - 1, int.MaxValue).Enumerate().ToArray();
    Equal(2, values.Length); Equal(int.MaxValue - 1, values[0]); Equal(int.MaxValue, values[1]);
}

static void TestRandomRange()
{
    int[] sample = new SeedRange(int.MinValue, int.MaxValue, true).Enumerate(123456).Take(100_000).ToArray();
    Equal(sample.Length, sample.Distinct().Count());
    True(sample.All(v => v >= int.MinValue && v <= int.MaxValue));
}

static void TestCopiedSeed()
{
    GenerationSettings settings = new()
    {
        TerrariaServerPath = typeof(object).Assembly.Location,
        CandidateDirectory = Path.GetTempPath(),
        Size = WorldSize.Large,
        Difficulty = WorldDifficulty.Master,
        Evil = WorldEvil.Crimson,
        SpecialSeeds = SpecialSeedFlags.ForTheWorthy | SpecialSeedFlags.NoTraps
    };
    Equal("3.3.2.48.-42", settings.BuildCopiedSeed(-42));
    Equal(ServerProcessPriority.Balanced, settings.ServerPriority);
}

static void TestMetricCatalogue()
{
    Equal(MetricCatalog.All.Count, MetricCatalog.All.Select(m => m.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    True(MetricCatalog.All.Count > 150);
}

static void TestEvaluator()
{
    Dictionary<string, double> metrics = new() { ["a"] = 5, ["b"] = 12 };
    RollProfile profile = new()
    {
        Criteria =
        [
            new CriterionDefinition { MetricKey = "a", Kind = CriterionKind.Hard, Comparison = MetricComparison.AtMost, Value = 5 },
            new CriterionDefinition { MetricKey = "b", Kind = CriterionKind.Weighted, Comparison = MetricComparison.AtLeast, Value = 10, Weight = 2 }
        ]
    };
    WorldEvaluation evaluation = ProfileEvaluator.Evaluate(metrics, profile);
    True(evaluation.PassedHardCriteria); True(evaluation.Score > 0); Equal(2, evaluation.Criteria.Count);
}

static void TestProfiles()
{
    HashSet<string> valid = MetricCatalog.All.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (RollProfile profile in BuiltInProfiles.All)
        foreach (CriterionDefinition criterion in profile.Criteria)
            True(valid.Contains(criterion.MetricKey), $"{profile.Name}: {criterion.MetricKey}");
}

static void TestWorld(string path)
{
    FileInfo beforeInfo = new(path);
    long beforeLength = beforeInfo.Length;
    DateTime beforeWrite = beforeInfo.LastWriteTimeUtc;
    byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(path));
    WorldAnalysis analysis = new WorldAnalyzer().Analyze(path, BuiltInProfiles.SafeAndTidy());
    byte[] afterHash = SHA256.HashData(File.ReadAllBytes(path));
    FileInfo afterInfo = new(path);
    Equal(beforeLength, afterInfo.Length);
    Equal(beforeWrite, afterInfo.LastWriteTimeUtc);
    True(beforeHash.SequenceEqual(afterHash), "World content changed during analysis.");
    True(analysis.Metadata.FileVersion is >= 269 and <= 326);
    True(analysis.Metadata.Width > 0 && analysis.Metadata.Height > 0);
    True(analysis.Chests.Count > 0);
    Equal(analysis.OverviewWidth * analysis.OverviewHeight, analysis.Overview.Length);
    True(analysis.Metrics.ContainsKey(MetricKeys.EvilPreHardmodeClosureLargestWidth));
    True(analysis.Metrics.ContainsKey(MetricKeys.JungleAccessCost));
    True(analysis.Metrics.ContainsKey(MetricKeys.ShimmerAccessCost));
    True(analysis.ImportantItems.All(item => double.IsFinite(item.EstimatedAccessCost)));
    True(analysis.Evaluation is not null);
}

static string? FindRepository()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "TerrariaSeedRoller.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    return null;
}

static void TestResourceSettings()
{
    GenerationSettings settings = new()
    {
        TerrariaServerPath = typeof(object).Assembly.Location,
        CandidateDirectory = Path.GetTempPath(),
        Parallelism = 8,
        ReservedLogicalProcessors = 1,
        MinimumFreeMemoryMb = 1536,
        ServerMemoryLimitMb = 3072,
        ServerStallTimeout = TimeSpan.FromMinutes(3)
    };
    settings.Validate();
    int expected = Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1));
    Equal(expected, settings.EffectiveParallelism);
    True(settings.EffectiveParallelism <= Environment.ProcessorCount - 1);

    // A reserve larger than the machine must still leave one usable slot.
    GenerationSettings extreme = settings with { ReservedLogicalProcessors = 64 };
    Equal(1, extreme.EffectiveParallelism);

    Throws<ArgumentOutOfRangeException>(() => (settings with { MinimumFreeMemoryMb = -1 }).Validate());
    Throws<ArgumentOutOfRangeException>(() => (settings with { ServerStallTimeout = TimeSpan.FromSeconds(5) }).Validate());
}

static void TestResourceGovernor()
{
    int floor = 64;
    using ResourceGovernor governor = new(requestedParallelism: 4, memoryFloorMb: floor,
        protectProcessPriority: false);
    governor.Configure(requestedParallelism: 4, memoryFloorMb: floor, reservedLogicalProcessors: 1);
    ResourceSnapshot snapshot = governor.Poll(force: true);
    True(snapshot.AvailableMemoryMb > 0, "Available memory must be measurable.");
    True(snapshot.MemoryFloorMb <= Math.Max(1, ResourceGovernor.TotalMemoryMb() / 4));
    True(snapshot.ParallelismCap >= 1 && snapshot.ParallelismCap <= 4);
    True(snapshot.State is ResourceGuardState.Healthy or ResourceGuardState.Reduced
        or ResourceGuardState.Reserved);
    True(governor.BuildSummary().Contains("资源保护统计", StringComparison.Ordinal));

    // Pause semantics are checked without a reserve floor, so the outcome does
    // not depend on how much memory the test machine happens to have free.
    using ResourceGovernor pausable = new(requestedParallelism: 2, memoryFloorMb: 0,
        protectProcessPriority: false);
    pausable.Configure(requestedParallelism: 2, memoryFloorMb: 0, reservedLogicalProcessors: 0);
    pausable.ForcePause("test");
    True(pausable.IsThrottled);
    Task waiting = pausable.WaitForCapacityAsync(CancellationToken.None);
    True(waiting.Wait(TimeSpan.FromMilliseconds(400)) == false,
        "A paused governor must not hand out capacity.");
    pausable.ClearForcePause();
    True(waiting.Wait(TimeSpan.FromSeconds(10)), "Clearing the pause must release the wait.");
    True(pausable.ThrottleCount >= 1);
}

static void TestResourceGovernorPressure()
{
    long floor = Math.Max(256, ResourceGovernor.AvailableMemoryMb() / 2);
    using ResourceGovernor governor = new(requestedParallelism: 4, memoryFloorMb: floor,
        protectProcessPriority: false);
    governor.Configure(requestedParallelism: 4, memoryFloorMb: floor, reservedLogicalProcessors: 0);
    True(governor.Snapshot.MemoryFloorMb <= Math.Max(1, ResourceGovernor.TotalMemoryMb() / 4));
    // The guard clamps a huge floor to a quarter of physical memory, so the real
    // reserve being enforced is the clamped value.
    long enforced = governor.Snapshot.MemoryFloorMb;
    long baselineMb = governor.Poll(force: true).AvailableMemoryMb;

    // Hold real memory so the machine genuinely runs low, then check the guard
    // refuses to start new work instead of letting the process walk into paging.
    List<byte[]> ballast = [];
    bool pressuredEnough = false;
    try
    {
        long freeMb = governor.Poll(force: true).AvailableMemoryMb;
        // Push past the reserve line and past the recovery hysteresis band above
        // it, so the guard must actually stop starting new work.
        long wantMb = freeMb - enforced + 512;
        if (wantMb > 0 && wantMb <= 4096)
        {
            // One array cannot exceed 2 GB on .NET, so the pressure is built from
            // chunks that are each touched to make the pages resident.
            long remaining = wantMb;
            while (remaining > 0)
            {
                int chunkMb = (int)Math.Min(remaining, 1024);
                byte[] chunk = GC.AllocateUninitializedArray<byte>(chunkMb * 1024 * 1024);
                for (long offset = 0; offset < chunk.LongLength; offset += 4096) chunk[offset] = 1;
                ballast.Add(chunk);
                remaining -= chunkMb;
            }

            ResourceSnapshot pressured = governor.Poll(force: true);
            if (pressured.AvailableMemoryMb < enforced)
            {
                pressuredEnough = true;
                Equal(ResourceGuardState.Reserved, pressured.State);
                Equal(1, pressured.ParallelismCap);
                True(governor.IsThrottled, "A breached reserve must stop new worlds from starting.");
                Task blocked = governor.WaitForCapacityAsync(CancellationToken.None);
                True(blocked.Wait(TimeSpan.FromMilliseconds(300)) == false,
                    "A breached reserve must not hand out capacity.");
                governor.ClearForcePause();
            }
        }
    }
    finally
    {
        ballast.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // Once the pressure is gone the guard must recover on its own. Windows hands
    // the pages back lazily and other processes keep allocating, so this waits
    // for the machine to return to where it started.
    ResourceSnapshot recovered = governor.Poll(force: true);
    for (int attempt = 0; attempt < 60 && recovered.State == ResourceGuardState.Reserved; attempt++)
    {
        Thread.Sleep(250);
        recovered = governor.Poll(force: true);
        if (recovered.AvailableMemoryMb >= baselineMb) break;
    }
    if (recovered.State == ResourceGuardState.Reserved)
    {
        // Only a machine that is still genuinely low may stay reserved; the guard
        // must not latch. This is reported rather than failed because the test
        // machine may be running the game itself.
        Console.WriteLine($"      [note] still reserved after pressure: available {recovered.AvailableMemoryMb} MB, " +
            $"floor {recovered.MemoryFloorMb} MB, baseline {baselineMb} MB, pressured={pressuredEnough}");
        True(recovered.AvailableMemoryMb < recovered.MemoryFloorMb,
            "The guard latched into Reserved even though memory recovered.");
    }
    True(recovered.ParallelismCap >= 1);
}

static void TestJournalRoundTrip()
{
    string directory = NewTempDirectory();
    try
    {
        RollJournal journal = new(directory);
        journal.AppendPending(new RollPendingRecord(7, "1.1.1.7", null, DateTimeOffset.Now));
        journal.Append(new RollAttemptRecord(7, "1.1.1.7", RollAttemptOutcome.Failed,
            0, null, null, 1, 2, "boom"));
        string staged = Path.Combine(directory, "staged.wld");
        File.WriteAllBytes(staged, [1, 2, 3]);
        journal.Append(new RollAttemptRecord(9, "1.1.1.9", RollAttemptOutcome.Accepted,
            12.5, staged, null, 3, 4));

        RollJournalContents contents = RollJournal.Read(directory);
        Equal(2, contents.Finished.Count);
        Equal(1, contents.Pending.Count);
        True(contents.FinishedSeeds.Contains(7));
        True(contents.FinishedSeeds.Contains(9));
        Equal(1, contents.Survivors.Count());
        Equal(9, contents.Survivors.First().Seed);
        True(contents.Finished.First(f => f.Seed == 7).Error == "boom");
    }
    finally { TryDelete(directory); }
}

static void TestJournalTornLine()
{
    string directory = NewTempDirectory();
    try
    {
        RollJournal journal = new(directory);
        journal.Append(new RollAttemptRecord(1, "a", RollAttemptOutcome.Rejected, 1, null, null, 0, 0));
        journal.Append(new RollAttemptRecord(2, "b", RollAttemptOutcome.Rejected, 2, null, null, 0, 0));
        // Simulate a process killed halfway through writing the next record.
        File.AppendAllText(journal.JournalPath, "{\"Type\":\"finished\",\"Finished\":{\"Seed\":3,");

        RollJournalContents contents = RollJournal.Read(directory);
        Equal(2, contents.Finished.Count);
        True(!contents.FinishedSeeds.Contains(3));
    }
    finally { TryDelete(directory); }
}

static void TestSessionState()
{
    string directory = NewTempDirectory();
    try
    {
        RollJournal journal = new(directory);
        RollGenerationFingerprint generation = new(WorldSize.Small, WorldDifficulty.Classic,
            WorldEvil.Random, SpecialSeedFlags.None, 0, int.MaxValue, true);
        RollSessionState running = new(directory, "安全整洁", SessionOutcome.Running, 3, 3, 0, 2,
            DateTimeOffset.Now, DateTimeOffset.Now, generation, 12345);
        journal.WriteState(running);
        Equal(SessionOutcome.Running, RollJournal.ReadState(directory)!.Outcome);
        True(RollJournal.ReadState(directory)!.IsInterrupted);

        // Replacing the state must never leave a partially written file behind.
        journal.WriteState(running with { Outcome = SessionOutcome.Completed });
        Equal(SessionOutcome.Completed, RollJournal.ReadState(directory)!.Outcome);
        Equal(12345, RollJournal.ReadState(directory)!.EnumerationSeed);
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
    }
    finally { TryDelete(directory); }
}

static void TestRecoveryDetection()
{
    string root = NewTempDirectory();
    try
    {
        string interrupted = Path.Combine(root, "roll_20260101_000000_测试");
        Directory.CreateDirectory(interrupted);
        string staged = Path.Combine(interrupted, "staged.wld");
        File.WriteAllBytes(staged, [1]);
        RollJournal journal = new(interrupted);
        RollGenerationFingerprint generation = new(WorldSize.Small, WorldDifficulty.Classic,
            WorldEvil.Random, SpecialSeedFlags.None, 0, int.MaxValue, true);
        // The state file holds the counters from when the run started, exactly as
        // a killed run leaves them: the journal is the authority for what finished.
        journal.WriteState(new RollSessionState(interrupted, "测试", SessionOutcome.Running,
            0, 0, 0, 0, DateTimeOffset.Now, DateTimeOffset.Now, generation, 5));
        journal.Append(new RollAttemptRecord(11, "1.1.1.11", RollAttemptOutcome.Accepted,
            5, staged, null, 1, 1));
        journal.Append(new RollAttemptRecord(12, "1.1.1.12", RollAttemptOutcome.Rejected,
            1, null, null, 1, 1));

        // A session that settled is not offered for recovery.
        string finished = Path.Combine(root, "roll_20260102_000000_完成");
        Directory.CreateDirectory(finished);
        new RollJournal(finished).WriteState(new RollSessionState(finished, "完成",
            SessionOutcome.Completed, 1, 1, 0, 1, DateTimeOffset.Now, DateTimeOffset.Now,
            generation, 6));

        IReadOnlyList<InterruptedRoll> found = RollRecovery.Find(root);
        Equal(1, found.Count);
        Equal("roll_20260101_000000_测试", found[0].Name);
        Equal(1, found[0].SurvivorCount);
        Equal(2, found[0].Attempted);
        True(found[0].Describe().Contains("可恢复 1 个", StringComparison.Ordinal));
        Equal("roll_20260101_000000_测试", RollRecovery.FindLatest(root)!.Name);

        RollRecovery.MarkAbandoned(found[0], "测试");
        Equal(0, RollRecovery.Find(root).Count);
        // Abandoning must record what really happened, not the stale start counters.
        RollSessionState settled = RollJournal.ReadState(interrupted)!;
        Equal(SessionOutcome.Failed, settled.Outcome);
        Equal(2, settled.Attempted);
        Equal(2, settled.Completed);
        Equal(1, settled.Accepted);
        Equal(5, settled.EnumerationSeed);
    }
    finally { TryDelete(root); }
}

static void TestRecoveryCompatibility()
{
    string directory = NewTempDirectory();
    try
    {
        RollGenerationFingerprint generation = new(WorldSize.Small, WorldDifficulty.Classic,
            WorldEvil.Random, SpecialSeedFlags.None, 0, int.MaxValue, true);
        InterruptedRoll roll = new(directory, "测试", generation, 1, 1, 0, 1,
            DateTimeOffset.Now, DateTimeOffset.Now, 0, true);
        GenerationSettings matching = new()
        {
            TerrariaServerPath = typeof(object).Assembly.Location,
            CandidateDirectory = directory
        };
        RollRecovery.EnsureCompatible(roll, matching);

        GenerationSettings changed = matching with { Size = WorldSize.Large };
        Throws<InvalidOperationException>(() => RollRecovery.EnsureCompatible(roll, changed));
        GenerationSettings otherEvil = matching with { Evil = WorldEvil.Crimson };
        Throws<InvalidOperationException>(() => RollRecovery.EnsureCompatible(roll, otherEvil));
    }
    finally { TryDelete(directory); }
}

static void TestOrphanCleanupSafety()
{
    string directory = NewTempDirectory();
    try
    {
        // A pid file is plain text on disk, so it can name any process. A
        // recycled pid must never be terminated: this process is alive and is
        // not a Terraria server.
        File.WriteAllText(Path.Combine(directory, ResourceGovernor.ServerPidFileName),
            $"{Environment.ProcessId} deadbeef{Environment.NewLine}", new UTF8Encoding(false));

        int killed = ResourceGovernor.KillOrphanedServers(directory, timeoutMilliseconds: 200);
        Equal(0, killed);
        True(!File.Exists(Path.Combine(directory, ResourceGovernor.ServerPidFileName)),
            "The consumed pid file must be removed.");
    }
    finally { TryDelete(directory); }
}

static void TestStaleWorkCleanup()
{
    string root = NewTempDirectory();
    try
    {
        string work = Path.Combine(root, "_work");
        // A killed run leaves exactly this behind: a non-empty directory, so the
        // cleanup cannot simply look for empty ones.
        string stale = Path.Combine(work, "seed_1_deadbeef");
        Directory.CreateDirectory(Path.Combine(stale, "save"));
        File.WriteAllText(Path.Combine(stale, "serverconfig.txt"), "port=0");
        File.WriteAllText(Path.Combine(stale, "save", "favorites.json"), "{}");

        // A directory that is still being written to must survive. Timestamps are
        // set explicitly so the outcome does not depend on how fast the test runs.
        string live = Path.Combine(work, "seed_2_live");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "serverconfig.txt"), "port=0");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(2));
        Directory.SetLastWriteTimeUtc(live, DateTime.UtcNow);

        // A three-hour cutoff must leave a two-hour-old directory alone: it is old
        // but not older than the window a live attempt could still be inside.
        Equal(0, TerrariaServerGenerator.CleanOrphanedWorkDirectories(root,
            TimeSpan.FromHours(3)));
        True(Directory.Exists(stale), "A directory newer than the cutoff must survive.");
        True(Directory.Exists(live));

        // A thirty-minute cutoff must reclaim it, even though it is not empty.
        Equal(1, TerrariaServerGenerator.CleanOrphanedWorkDirectories(root,
            TimeSpan.FromMinutes(30)));
        True(!Directory.Exists(stale), "A stale non-empty directory must be reclaimed.");
        True(Directory.Exists(live), "A directory newer than the cutoff must survive.");
    }
    finally { TryDelete(root); }
}

static string NewTempDirectory()
{
    string directory = Path.Combine(Path.GetTempPath(), "tsr-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void TryDelete(string directory)
{
    try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

static void True(bool condition, string? message = null)
{
    if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}, but nothing was thrown.");
}
