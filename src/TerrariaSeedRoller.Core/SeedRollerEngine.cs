using System.Collections.Concurrent;
using System.Diagnostics;

namespace TerrariaSeedRoller.Core;

public sealed class SeedRollerEngine
{
    private readonly TerrariaServerGenerator _generator;
    private readonly WorldAnalyzer _analyzer;

    public SeedRollerEngine(TerrariaServerGenerator? generator = null, WorldAnalyzer? analyzer = null)
    {
        _generator = generator ?? new TerrariaServerGenerator();
        _analyzer = analyzer ?? new WorldAnalyzer();
    }

    public async Task<RollSessionResult> RunAsync(GenerationSettings settings, RollProfile profile,
        PauseController? pause = null, IProgress<RollProgress>? progress = null,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        ArgumentNullException.ThrowIfNull(profile);
        pause ??= new PauseController();
        DateTimeOffset started = DateTimeOffset.Now;
        Stopwatch elapsed = Stopwatch.StartNew();
        string root = Path.GetFullPath(settings.CandidateDirectory);
        Directory.CreateDirectory(root);
        string sessionDirectory = Path.Combine(root,
            $"roll_{started:yyyyMMdd_HHmmss}_{Sanitize(profile.Name)}");
        Directory.CreateDirectory(sessionDirectory);
        string rejectedDirectory = Path.Combine(sessionDirectory, "rejected");

        object winnersGate = new();
        List<(RollResult Result, string AttemptDirectory)> top = [];
        ConcurrentBag<string> failures = [];
        int attempted = 0, completed = 0, accepted = 0, failed = 0;
        bool cancelled = false;
        IEnumerable<int> seeds = settings.Seeds.Enumerate().Take(settings.MaximumAttempts);

        try
        {
            await Parallel.ForEachAsync(seeds, new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.Parallelism,
                CancellationToken = cancellationToken
            }, async (seed, token) =>
            {
                await pause.WaitIfPausedAsync(token).ConfigureAwait(false);
                int currentAttempt = Interlocked.Increment(ref attempted);
                Report(seed, "生成", null, $"开始第 {currentAttempt} 个种子");
                GeneratedWorld? generated = null;
                try
                {
                    generated = await _generator.GenerateAsync(settings, seed, log, token).ConfigureAwait(false);
                    await pause.WaitIfPausedAsync(token).ConfigureAwait(false);
                    Report(seed, "分析", null, "只读解析世界并计算指标");
                    Stopwatch analysisWatch = Stopwatch.StartNew();
                    WorldAnalysis analysis = await Task.Run(
                        () => _analyzer.Analyze(generated.WorldPath, profile, token), token).ConfigureAwait(false);
                    analysisWatch.Stop();
                    WorldEvaluation evaluation = analysis.Evaluation ??
                        throw new InvalidOperationException("Profile evaluation was not produced.");
                    RollResult rollResult = new(seed, generated.CopiedSeed, generated.WorldPath,
                        generated.Duration, analysisWatch.Elapsed, analysis);

                    if (!evaluation.PassedHardCriteria)
                    {
                        if (settings.KeepRejectedWorlds)
                            MoveRejected(generated, rejectedDirectory);
                        else
                            TerrariaServerGenerator.DeleteAttemptDirectory(generated.AttemptDirectory,
                                settings.CandidateDirectory);
                        Report(seed, "淘汰", evaluation.Score, "未通过硬条件");
                    }
                    else
                    {
                        Interlocked.Increment(ref accepted);
                        (RollResult Result, string AttemptDirectory)? dropped = null;
                        lock (winnersGate)
                        {
                            top.Add((rollResult, generated.AttemptDirectory));
                            top.Sort((a, b) => b.Result.Analysis.Evaluation!.Score
                                .CompareTo(a.Result.Analysis.Evaluation!.Score));
                            if (top.Count > settings.WinnersToKeep)
                            {
                                dropped = top[^1];
                                top.RemoveAt(top.Count - 1);
                            }
                        }
                        if (dropped.HasValue)
                            TerrariaServerGenerator.DeleteAttemptDirectory(dropped.Value.AttemptDirectory,
                                settings.CandidateDirectory);
                        Report(seed, "入围", evaluation.Score, "通过硬条件并进入当前排名");
                    }
                    Interlocked.Increment(ref completed);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    if (generated is not null && Directory.Exists(generated.AttemptDirectory))
                        TerrariaServerGenerator.DeleteAttemptDirectory(generated.AttemptDirectory,
                            settings.CandidateDirectory);
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Interlocked.Increment(ref completed);
                    failures.Add($"Seed {seed}: {ex.Message}");
                    log?.Invoke($"种子 {seed} 失败: {ex}");
                    if (generated is not null && Directory.Exists(generated.AttemptDirectory))
                        TerrariaServerGenerator.DeleteAttemptDirectory(generated.AttemptDirectory,
                            settings.CandidateDirectory);
                    Report(seed, "失败", null, ex.Message);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            log?.Invoke("任务已取消，正在保存已经完成的入围结果。");
        }

        List<RollResult> winners = [];
        string winnersDirectory = Path.Combine(sessionDirectory, "winners");
        Directory.CreateDirectory(winnersDirectory);
        List<(RollResult Result, string AttemptDirectory)> finalTop;
        lock (winnersGate) finalTop = top.ToList();
        int rank = 0;
        foreach ((RollResult result, string attemptDirectory) in finalTop)
        {
            rank++;
            string baseName = $"{rank:00}_seed_{result.Seed}_score_{result.Analysis.Evaluation!.Score:0.##}";
            string finalWorld = UniquePath(Path.Combine(winnersDirectory, baseName + ".wld"));
            File.Move(result.WorldPath, finalWorld);
            WorldAnalysis finalAnalysis = result.Analysis with { WorldPath = finalWorld };
            RollResult finalResult = result with { WorldPath = finalWorld, Analysis = finalAnalysis };
            string reportDirectory = Path.Combine(winnersDirectory, Path.GetFileNameWithoutExtension(finalWorld));
            ReportWriter.Write(finalAnalysis, reportDirectory, finalResult.CopiedSeed);
            winners.Add(finalResult);
            TerrariaServerGenerator.DeleteAttemptDirectory(attemptDirectory, settings.CandidateDirectory);
        }

        if (failures.Count > 0)
            await File.WriteAllLinesAsync(Path.Combine(sessionDirectory, "failures.log"), failures,
                CancellationToken.None).ConfigureAwait(false);
        elapsed.Stop();
        RollSessionResult session = new(started, DateTimeOffset.Now, attempted, completed, failed,
            cancelled, winners.AsReadOnly(), sessionDirectory);
        ReportWriter.WriteSession(session, profile, Path.Combine(sessionDirectory, "session.json"));
        TryDeleteEmptyWorkRoot(settings.CandidateDirectory);
        return session;

        void Report(int seed, string stage, double? score, string message)
        {
            progress?.Report(new RollProgress(attempted, completed, accepted, failed,
                settings.MaximumAttempts, seed, stage, elapsed.Elapsed, score, message));
        }
    }

    private static void MoveRejected(GeneratedWorld generated, string rejectedDirectory)
    {
        Directory.CreateDirectory(rejectedDirectory);
        string destination = UniquePath(Path.Combine(rejectedDirectory,
            Path.GetFileName(generated.AttemptDirectory)));
        Directory.Move(generated.AttemptDirectory, destination);
    }

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
}
