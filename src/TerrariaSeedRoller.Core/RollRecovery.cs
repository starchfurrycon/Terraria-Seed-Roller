using System.Text;

namespace TerrariaSeedRoller.Core;

/// <summary>A roll session that stopped without finishing.</summary>
public sealed record InterruptedRoll(
    string SessionDirectory,
    string ProfileName,
    RollGenerationFingerprint Generation,
    int Attempted,
    int Completed,
    int Failed,
    int Accepted,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    int SurvivorCount,
    bool HasState)
{
    public string Name => Path.GetFileName(SessionDirectory.TrimEnd(Path.DirectorySeparatorChar));

    public string Describe() =>
        $"{Name}  预设「{ProfileName}」  已尝试 {Attempted}，可恢复 {SurvivorCount} 个入围世界  " +
        $"（最后更新 {UpdatedAt:yyyy-MM-dd HH:mm}）";
}

/// <summary>Finds and recovers roll sessions that were interrupted.</summary>
public static class RollRecovery
{
    private const string SessionPrefix = "roll_";

    /// <summary>Every interrupted session under an output directory, newest first.</summary>
    public static IReadOnlyList<InterruptedRoll> Find(string outputDirectory)
    {
        string root = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(root)) return [];
        List<InterruptedRoll> found = [];
        foreach (string directory in Directory.EnumerateDirectories(root, SessionPrefix + "*"))
        {
            InterruptedRoll? roll = Load(directory);
            if (roll is not null) found.Add(roll);
        }
        found.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        return found;
    }

    /// <summary>The most recent interrupted session, or null when there is none.</summary>
    public static InterruptedRoll? FindLatest(string outputDirectory)
    {
        IReadOnlyList<InterruptedRoll> all = Find(outputDirectory);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>
    /// Reads one session directory. Returns null when the session finished
    /// normally, was already recovered, or never started a roll at all.
    /// </summary>
    public static InterruptedRoll? Load(string sessionDirectory)
    {
        string directory = Path.GetFullPath(sessionDirectory);
        if (!Directory.Exists(directory)) return null;
        // A directory with neither artefact is not one of ours.
        string journalPath = Path.Combine(directory, RollJournal.JournalFileName);
        string sessionPath = Path.Combine(directory, "session.json");
        if (!File.Exists(journalPath) && !File.Exists(sessionPath)) return null;

        RollSessionState? state = RollJournal.ReadState(directory);
        if (state is not null && state.Outcome != SessionOutcome.Running) return null;

        RollJournalContents contents = RollJournal.Read(directory);
        DateTimeOffset updated = state?.UpdatedAt ?? DateTimeOffset.MinValue;
        if (state is null)
        {
            // No state file: an older session, or one killed before the state was
            // ever written. A finished session.json means it did settle.
            if (File.Exists(sessionPath)) return null;
            updated = LastWrite(journalPath);
        }

        // A killed run leaves the counters from when it started, so the journal is
        // the authority for how far it actually got.
        int completed = contents.Finished.Count;
        int accepted = contents.Finished.Count(f => f.Outcome == RollAttemptOutcome.Accepted);
        int failed = contents.Finished.Count(f => f.Outcome == RollAttemptOutcome.Failed);

        return new InterruptedRoll(
            directory,
            state?.ProfileName ?? "未知",
            state?.Generation ?? new RollGenerationFingerprint(
                WorldSize.Small, WorldDifficulty.Classic, WorldEvil.Random,
                SpecialSeedFlags.None, 0, int.MaxValue, true),
            Math.Max(completed, state?.Attempted ?? 0),
            Math.Max(completed, state?.Completed ?? 0),
            Math.Max(failed, state?.Failed ?? 0),
            Math.Max(accepted, state?.Accepted ?? 0),
            state?.StartedAt ?? updated,
            updated,
            contents.Survivors.Count(),
            state is not null);
    }

    /// <summary>
    /// Refuses to continue a session whose world parameters no longer match, so
    /// worlds generated under different rules are never ranked together.
    /// </summary>
    public static void EnsureCompatible(InterruptedRoll roll, GenerationSettings settings)
    {
        RollGenerationFingerprint current = RollGenerationFingerprint.From(settings);
        if (roll.Generation.Matches(current)) return;
        throw new InvalidOperationException(
            $"无法继续会话 {roll.Name}：世界参数已改变。" +
            $"会话为 {roll.Generation.Describe()}，当前为 {current.Describe()}。" +
            "请使用 --fresh 开始一个新会话。");
    }

    /// <summary>Marks a session as abandoned so it stops being offered for recovery.</summary>
    public static void MarkAbandoned(InterruptedRoll roll, string reason)
    {
        // A run killed mid-flight leaves the state file holding the counters from
        // when it started, so the journal is the authority for what actually
        // finished.
        RollJournalContents contents = RollJournal.Read(roll.SessionDirectory);
        int completed = contents.Finished.Count;
        int accepted = contents.Finished.Count(f => f.Outcome == RollAttemptOutcome.Accepted);
        int failed = contents.Finished.Count(f => f.Outcome == RollAttemptOutcome.Failed);

        RollSessionState? existing = RollJournal.ReadState(roll.SessionDirectory);
        RollSessionState state = (existing ?? new RollSessionState(
            roll.SessionDirectory, roll.ProfileName, SessionOutcome.Running,
            completed, completed, failed, accepted,
            roll.StartedAt, DateTimeOffset.Now, roll.Generation, null))
            with
        {
            Attempted = Math.Max(completed, existing?.Attempted ?? 0),
            Completed = Math.Max(completed, existing?.Completed ?? 0),
            Accepted = Math.Max(accepted, existing?.Accepted ?? 0),
            Failed = Math.Max(failed, existing?.Failed ?? 0),
            Outcome = SessionOutcome.Failed,
            UpdatedAt = DateTimeOffset.Now
        };
        new RollJournal(roll.SessionDirectory).WriteState(state);
        try
        {
            File.AppendAllText(Path.Combine(roll.SessionDirectory, RollJournal.CrashFileName),
                $"{DateTimeOffset.Now:O} 会话已放弃：{reason}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Exports the worlds an interrupted session had already finished, without
    /// generating anything new, and writes the reports they never got.
    /// </summary>
    public static IReadOnlyList<RollResult> ExportSurvivors(InterruptedRoll roll, RollProfile profile,
        string? destinationDirectory = null, Action<string>? log = null)
    {
        string destination = Path.GetFullPath(destinationDirectory ??
            Path.Combine(roll.SessionDirectory, "recovered"));
        Directory.CreateDirectory(destination);

        RollJournalContents contents = RollJournal.Read(roll.SessionDirectory);
        WorldAnalyzer analyzer = new();
        List<RollResult> results = [];
        int rank = 0;
        foreach (RollAttemptRecord record in contents.Survivors
            .OrderByDescending(r => r.Score))
        {
            rank++;
            string baseName = $"{rank:00}_seed_{record.Seed}_score_{record.Score:0.##}";
            string world = UniquePath(Path.Combine(destination, baseName + ".wld"));
            try
            {
                File.Copy(record.WorldPath!, world, overwrite: false);
            }
            catch (IOException ex)
            {
                log?.Invoke($"恢复：无法复制种子 {record.Seed} 的世界：{ex.Message}");
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                log?.Invoke($"恢复：无法复制种子 {record.Seed} 的世界：{ex.Message}");
                continue;
            }

            // Re-analyse so the exported report matches the profile being used now.
            WorldAnalysis analysis = analyzer.Analyze(world, profile);
            WorldAnalysis finalAnalysis = analysis with { WorldPath = world };
            string reportDirectory = Path.Combine(destination, Path.GetFileNameWithoutExtension(world));
            ReportWriter.Write(finalAnalysis, reportDirectory, record.CopiedSeed);
            results.Add(new RollResult(record.Seed, record.CopiedSeed, world,
                TimeSpan.FromSeconds(record.GenerationSeconds),
                TimeSpan.FromSeconds(record.AnalysisSeconds), finalAnalysis));
            log?.Invoke($"恢复：导出种子 {record.Seed}（score={record.Score:0.##}）");
        }

        File.WriteAllText(Path.Combine(destination, "RECOVERED.txt"),
            BuildRecoveryReport(roll, results), new UTF8Encoding(false));

        // Recovering settles the session, so the half-written work it left behind
        // can be reclaimed now instead of waiting for the age-based sweep.
        string root = Path.GetDirectoryName(roll.SessionDirectory) ?? roll.SessionDirectory;
        int reclaimed = 0;
        foreach (string orphan in contents.OrphanedDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (TerrariaServerGenerator.DeleteAttemptDirectory(orphan, root)) reclaimed++;
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        reclaimed += TerrariaServerGenerator.CleanOrphanedWorkDirectories(root, TimeSpan.Zero);
        if (reclaimed > 0) log?.Invoke($"恢复：已清理 {reclaimed} 个未完成的临时生成目录。");

        MarkAbandoned(roll, $"已由 recover 导出 {results.Count} 个世界");
        return results;
    }

    private static string BuildRecoveryReport(InterruptedRoll roll, IReadOnlyList<RollResult> results)
    {
        StringBuilder text = new();
        text.AppendLine("Terraria Seed Roller 恢复报告");
        text.AppendLine($"会话: {roll.Name}");
        text.AppendLine($"会话目录: {roll.SessionDirectory}");
        text.AppendLine($"预设: {roll.ProfileName}");
        text.AppendLine($"世界参数: {roll.Generation.Describe()}");
        text.AppendLine($"开始时间: {roll.StartedAt:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"恢复时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"中断前已尝试: {roll.Attempted}，成功分析 {roll.Completed - roll.Failed}，失败 {roll.Failed}");
        text.AppendLine($"本次导出世界: {results.Count}");
        text.AppendLine();
        int rank = 0;
        foreach (RollResult result in results)
        {
            rank++;
            text.AppendLine($"#{rank} {result.CopiedSeed}  score={result.Analysis.Evaluation?.Score ?? 0:0.##}  " +
                $"{Path.GetFileName(result.WorldPath)}");
        }
        text.AppendLine();
        text.AppendLine("这些世界是上一次中断前已经生成并分析完成的候选结果。");
        return text.ToString();
    }

    private static DateTimeOffset LastWrite(string path)
    {
        try { return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToLocalTime(); }
        catch (IOException) { return DateTimeOffset.MinValue; }
        catch (UnauthorizedAccessException) { return DateTimeOffset.MinValue; }
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
}