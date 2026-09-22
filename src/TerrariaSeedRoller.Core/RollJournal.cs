using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerrariaSeedRoller.Core;

/// <summary>What happened to one generated world.</summary>
public enum RollAttemptOutcome
{
    /// <summary>Accepted and ranked.</summary>
    Accepted,

    /// <summary>Generated but rejected by a hard criterion.</summary>
    Rejected,

    /// <summary>Generation or analysis failed.</summary>
    Failed
}

/// <summary>
/// One finished attempt. <see cref="WorldPath"/> is the world's durable location,
/// never the volatile <c>_work</c> path that is deleted when the session settles.
/// </summary>
public sealed record RollAttemptRecord(
    int Seed,
    string CopiedSeed,
    RollAttemptOutcome Outcome,
    double Score,
    string? WorldPath,
    string? AttemptDirectory,
    double GenerationSeconds,
    double AnalysisSeconds,
    string? Error = null,
    string? ResourceState = null)
{
    public bool HasWorld => !string.IsNullOrWhiteSpace(WorldPath) && File.Exists(WorldPath);
}

/// <summary>An attempt that started but has no outcome recorded yet.</summary>
public sealed record RollPendingRecord(
    int Seed,
    string CopiedSeed,
    string? AttemptDirectory,
    DateTimeOffset StartedAt,
    string? ResourceState = null);

internal sealed record RollJournalEnvelope
{
    public string Type { get; init; } = "finished";
    public RollPendingRecord? Pending { get; init; }
    public RollAttemptRecord? Finished { get; init; }
}

/// <summary>Everything a journal holds, in the order it was written.</summary>
public sealed class RollJournalContents
{
    public List<RollAttemptRecord> Finished { get; } = [];

    public List<RollPendingRecord> Pending { get; } = [];

    /// <summary>Seeds that already produced a terminal record.</summary>
    public HashSet<int> FinishedSeeds { get; } = [];

    /// <summary>Attempt directories left behind by attempts that never finished.</summary>
    public IEnumerable<string> OrphanedDirectories =>
        Pending.Where(p => !string.IsNullOrWhiteSpace(p.AttemptDirectory))
               .Select(p => p.AttemptDirectory!);

    /// <summary>Finished attempts that still have a usable world on disk.</summary>
    public IEnumerable<RollAttemptRecord> Survivors =>
        Finished.Where(f => f.Outcome == RollAttemptOutcome.Accepted && f.HasWorld);
}

/// <summary>
/// The generation choices that change what a seed produces.
/// </summary>
/// <remarks>
/// A session may only be continued when this fingerprint still matches, so worlds
/// generated under different rules are never ranked together.
/// </remarks>
public sealed record RollGenerationFingerprint(
    WorldSize Size,
    WorldDifficulty Difficulty,
    WorldEvil Evil,
    SpecialSeedFlags SpecialSeeds,
    int SeedStart,
    int SeedEnd,
    bool RandomOrder)
{
    public static RollGenerationFingerprint From(GenerationSettings settings) => new(
        settings.Size, settings.Difficulty, settings.Evil, settings.SpecialSeeds,
        settings.Seeds.Start, settings.Seeds.End, settings.Seeds.RandomOrder);

    public bool Matches(RollGenerationFingerprint other) => this == other;

    public string Describe() =>
        $"{Size}/{Difficulty}/{Evil}/{(SpecialSeeds == SpecialSeedFlags.None ? "无特殊种子" : SpecialSeeds.ToString())}" +
        $"/种子 {SeedStart}..{SeedEnd}/{(RandomOrder ? "随机顺序" : "顺序")}";
}

/// <summary>The session state file. A session still marked Running was interrupted.</summary>
public sealed record RollSessionState(
    string SessionDirectory,
    string ProfileName,
    SessionOutcome Outcome,
    int Attempted,
    int Completed,
    int Failed,
    int Accepted,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    RollGenerationFingerprint Generation,
    int? EnumerationSeed)
{
    public bool IsInterrupted => Outcome == SessionOutcome.Running;
}

/// <summary>
/// The durable record of one roll session.
/// </summary>
/// <remarks>
/// Every attempt is journalled and flushed to disk before the next step, so a
/// process that dies without warning still leaves behind a truthful account of
/// what finished. The journal is append-only: a torn final line is discarded
/// rather than failing the whole file.
/// </remarks>
public sealed class RollJournal
{
    public const string JournalFileName = "session.journal.jsonl";
    public const string StateFileName = "session.state.json";
    public const string CrashFileName = "crash.log";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions StateOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    public RollJournal(string sessionDirectory)
    {
        SessionDirectory = Path.GetFullPath(sessionDirectory);
        JournalPath = Path.Combine(SessionDirectory, JournalFileName);
        StatePath = Path.Combine(SessionDirectory, StateFileName);
        CrashPath = Path.Combine(SessionDirectory, CrashFileName);
    }

    public string SessionDirectory { get; }
    public string JournalPath { get; }
    public string StatePath { get; }
    public string CrashPath { get; }

    /// <summary>Records that an attempt is about to start generating.</summary>
    public void AppendPending(RollPendingRecord pending)
    {
        Write(new RollJournalEnvelope { Type = "pending", Pending = pending });
    }

    /// <summary>Records the terminal outcome of an attempt and flushes it to disk.</summary>
    public void Append(RollAttemptRecord record)
    {
        Write(new RollJournalEnvelope { Type = "finished", Finished = record });
    }

    private void Write(RollJournalEnvelope envelope)
    {
        string line = JsonSerializer.Serialize(envelope, JsonOptions);
        lock (_gate)
        {
            Directory.CreateDirectory(SessionDirectory);
            using FileStream stream = new(JournalPath, FileMode.Append, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.WriteThrough);
            using StreamWriter writer = new(stream, new UTF8Encoding(false));
            writer.WriteLine(line);
            writer.Flush();
            // Force the bytes out before the caller moves on: the whole point is
            // to survive a kill that gives no chance to flush later.
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>Reads a journal, discarding only a torn final line.</summary>
    public static RollJournalContents Read(string sessionDirectory)
    {
        string path = Path.Combine(Path.GetFullPath(sessionDirectory), JournalFileName);
        RollJournalContents contents = new();
        if (!File.Exists(path)) return contents;
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (IOException) { return contents; }
        catch (UnauthorizedAccessException) { return contents; }
        Parse(lines, contents);
        return contents;
    }

    internal static void Parse(IEnumerable<string> lines, RollJournalContents contents)
    {
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            RollJournalEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<RollJournalEnvelope>(line, JsonOptions);
            }
            catch (JsonException)
            {
                // A process killed mid-write leaves a partial final line. The
                // records before it are still valid and must not be thrown away.
                continue;
            }
            if (envelope is null) continue;
            if (envelope.Finished is not null)
            {
                contents.Finished.Add(envelope.Finished);
                contents.FinishedSeeds.Add(envelope.Finished.Seed);
            }
            else if (envelope.Pending is not null)
            {
                contents.Pending.Add(envelope.Pending);
            }
        }
    }

    /// <summary>Reads the state file, or null when it is missing or unreadable.</summary>
    public static RollSessionState? ReadState(string sessionDirectory)
    {
        string path = Path.Combine(Path.GetFullPath(sessionDirectory), StateFileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<RollSessionState>(File.ReadAllText(path, Encoding.UTF8),
                StateOptions);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void WriteState(RollSessionState state)
    {
        Directory.CreateDirectory(SessionDirectory);
        WriteAtomic(StatePath, JsonSerializer.Serialize(state, StateOptions));
    }

    public void WriteCrash(string message)
    {
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            File.WriteAllText(CrashPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Replaces a file atomically, so a crash can never leave a half-written state
    /// file that looks like a different outcome.
    /// </summary>
    public static void WriteAtomic(string path, string content)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory,
            $"{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        try
        {
            if (File.Exists(full))
            {
                try
                {
                    File.Replace(temporary, full, destinationBackupFileName: null,
                        ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}