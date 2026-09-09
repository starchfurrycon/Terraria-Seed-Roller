using System.Security.Cryptography;
using TerrariaSeedRoller.Core;

List<(string Name, Action Test)> tests =
[
    ("SeedRange ordered reaches Int32.MaxValue", TestOrderedRange),
    ("SeedRange random full Int32 range does not overflow", TestRandomRange),
    ("Copied seed encodes all generation choices", TestCopiedSeed),
    ("Metric catalogue keys are unique", TestMetricCatalogue),
    ("Profile evaluator handles hard and weighted criteria", TestEvaluator),
    ("Built-in profiles only use valid metrics", TestProfiles)
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

static void True(bool condition, string? message = null)
{
    if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
