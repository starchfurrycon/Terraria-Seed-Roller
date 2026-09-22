using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TerrariaSeedRoller.Core;

return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Help();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "analyze" => Analyze(args[1..]),
                "roll" => await RollAsync(args[1..]),
                "init" => Init(args[1..]),
                "sessions" => Sessions(args[1..]),
                "recover" => await RecoverAsync(args[1..]),
                "presets" => Presets(),
                "metrics" => Metrics(),
                _ => Unknown(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("已取消。");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    private static int Analyze(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("analyze 需要 .wld 路径。");
        string world = Path.GetFullPath(args[0]);
        string profileName = Option(args, "--profile") ?? "安全整洁";
        string output = Path.GetFullPath(Option(args, "--output") ??
            Path.Combine(Environment.CurrentDirectory, "analysis_" + Path.GetFileNameWithoutExtension(world)));
        RollProfile profile = BuiltInProfiles.Get(profileName);
        Console.WriteLine($"只读分析: {world}");
        WorldAnalysis analysis = new WorldAnalyzer().Analyze(world, profile);
        ReportWriter.Write(analysis, output);
        Console.WriteLine($"世界: {analysis.Metadata.Title}  格式: {analysis.Metadata.FileVersion}");
        Console.WriteLine($"得分: {analysis.Evaluation!.Score:0.##}  硬条件: {(analysis.Evaluation.PassedHardCriteria ? "通过" : "未通过")}");
        Console.WriteLine($"肉前邪恶自由蔓延最大宽度: {analysis.Metrics[MetricKeys.EvilPreHardmodeClosureLargestWidth]:0} 格");
        Console.WriteLine($"报告: {Path.Combine(output, "report.html")}");
        return analysis.Evaluation.PassedHardCriteria ? 0 : 2;
    }

    private static async Task<int> RollAsync(string[] args)
    {
        string configPath = Path.GetFullPath(Option(args, "--config") ??
            (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "roller.json"));
        RollConfiguration configuration = JsonSerializer.Deserialize<RollConfiguration>(
            await File.ReadAllTextAsync(configPath), JsonOptions) ??
            throw new InvalidDataException("配置文件为空或无效。");
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Progress<RollProgress> progress = new(p =>
            Console.WriteLine($"[{p.Completed}/{p.MaximumAttempts}] seed={p.CurrentSeed} {p.Stage}" +
                (p.LatestScore.HasValue ? $" score={p.LatestScore:0.##}" : string.Empty) +
                (string.IsNullOrWhiteSpace(p.Message) ? string.Empty : $" - {p.Message}")));
        bool resume = args.Any(a => string.Equals(a, "--resume", StringComparison.OrdinalIgnoreCase));
        bool fresh = args.Any(a => string.Equals(a, "--fresh", StringComparison.OrdinalIgnoreCase));
        if (resume && fresh) throw new ArgumentException("--resume 与 --fresh 不能同时使用。");
        Console.WriteLine($"预设: {configuration.Profile.Name}");
        Console.WriteLine("按 Ctrl+C 可安全取消；只会终止本工具启动的 TerrariaServer。");
        Console.WriteLine("每个世界分析完成后立即落盘，中断的会话可用 roll --resume 继续。");
        RollSessionResult session = await new SeedRollerEngine().RunAsync(configuration.Generation,
            configuration.Profile, pause: null, progress: progress, log: Console.WriteLine,
            cancellationToken: cancellation.Token,
            options: new RollRunOptions { Resume = resume, Fresh = fresh });
        if (session.Resumed)
            Console.WriteLine("本次运行继续了上一次被中断的会话。");
        Console.WriteLine($"完成: 尝试 {session.Attempted}，成功分析 {session.Completed - session.Failed}，失败 {session.Failed}。");
        if (session.ResourceSummary is not null) Console.WriteLine(session.ResourceSummary);
        Console.WriteLine($"保留 {session.Winners.Count} 个入选世界: {session.OutputDirectory}");
        foreach ((RollResult winner, int index) in session.Winners.Select((value, index) => (value, index)))
            Console.WriteLine($"#{index + 1} {winner.CopiedSeed}  score={winner.Analysis.Evaluation!.Score:0.##}  " +
                $"生成={winner.GenerationDuration.TotalSeconds:0.00}s  分析={winner.AnalysisDuration.TotalSeconds:0.00}s");
        return session.Cancelled ? 130 : session.Winners.Count == 0 ? 2 : 0;
    }

    private static int Init(string[] args)
    {
        string path = Path.GetFullPath(args.Length > 0 ? args[0] : "roller.json");
        if (File.Exists(path) && !args.Contains("--force"))
            throw new IOException($"文件已存在: {path}（使用 --force 明确覆盖）");
        string defaultServer = @"D:\Program Files (x86)\Steam\steamapps\common\Terraria\TerrariaServer.exe";
        RollConfiguration config = new()
        {
            Generation = new GenerationSettings
            {
                TerrariaServerPath = defaultServer,
                CandidateDirectory = Path.Combine(Path.GetDirectoryName(path)!, "roll-output"),
                Size = WorldSize.Small,
                Difficulty = WorldDifficulty.Classic,
                Evil = WorldEvil.Random,
                Seeds = new SeedRange(0, int.MaxValue, true),
                MaximumAttempts = 20,
                WinnersToKeep = 3,
                Parallelism = 1,
                PerWorldTimeout = TimeSpan.FromMinutes(10),
                MinimumFreeMemoryMb = 1536,
                ReservedLogicalProcessors = 1,
                ServerMemoryLimitMb = 3072,
                ServerStallTimeout = TimeSpan.FromMinutes(3),
                ProtectProcessPriority = true
            },
            Profile = BuiltInProfiles.SafeAndTidy()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        Console.WriteLine($"已创建: {path}");
        return 0;
    }

    private static int Presets()
    {
        foreach (RollProfile profile in BuiltInProfiles.All)
            Console.WriteLine($"{profile.Name}\t{profile.Description}");
        return 0;
    }

    private static int Metrics()
    {
        foreach (IGrouping<string, MetricDefinition> category in MetricCatalog.All.GroupBy(m => m.Category))
        {
            Console.WriteLine($"\n[{category.Key}]");
            foreach (MetricDefinition metric in category)
                Console.WriteLine($"{metric.Key}\t{metric.ChineseName} ({metric.Unit})");
        }
        Console.WriteLine("\n通用扩展: tiles.<ID>.count / walls.<ID>.count / loot.item.<ID>.*");
        return 0;
    }

    private static string ResolveOutput(string[] args)
    {
        string? configured = Option(args, "--output");
        if (configured is not null) return Path.GetFullPath(configured);
        string configPath = Option(args, "--config") ?? "roller.json";
        if (File.Exists(configPath))
        {
            try
            {
                RollConfiguration configuration = JsonSerializer.Deserialize<RollConfiguration>(
                    File.ReadAllText(configPath), JsonOptions)!;
                return Path.GetFullPath(configuration.Generation.CandidateDirectory);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    private static int Sessions(string[] args)
    {
        string output = ResolveOutput(args);
        IReadOnlyList<InterruptedRoll> rolls = RollRecovery.Find(output);
        if (rolls.Count == 0)
        {
            Console.WriteLine($"{output} 下没有被中断的 Roll 种会话。");
            return 0;
        }
        Console.WriteLine($"{output} 下有 {rolls.Count} 个可继续的会话：");
        foreach (InterruptedRoll roll in rolls) Console.WriteLine($"  {roll.Describe()}");
        Console.WriteLine("使用 roll --resume 继续最近一个，或 recover 直接导出已完成的世界。");
        return 0;
    }

    private static async Task<int> RecoverAsync(string[] args)
    {
        string configPath = Path.GetFullPath(Option(args, "--config") ??
            (args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "roller.json"));
        RollConfiguration configuration = JsonSerializer.Deserialize<RollConfiguration>(
            await File.ReadAllTextAsync(configPath), JsonOptions) ??
            throw new InvalidDataException("配置文件为空或无效。");
        string output = ResolveOutput(args);
        InterruptedRoll? roll = RollRecovery.FindLatest(output);
        if (roll is null)
        {
            Console.WriteLine($"{output} 下没有被中断的 Roll 种会话。");
            return 0;
        }
        RollRecovery.EnsureCompatible(roll, configuration.Generation);
        Console.WriteLine($"恢复会话: {roll.Describe()}");
        IReadOnlyList<RollResult> recovered = RollRecovery.ExportSurvivors(roll,
            configuration.Profile, log: Console.WriteLine);
        Console.WriteLine($"已导出 {recovered.Count} 个已完成的世界。");
        foreach ((RollResult result, int index) in recovered.Select((value, index) => (value, index)))
            Console.WriteLine($"#{index + 1} {result.CopiedSeed}  score={result.Analysis.Evaluation?.Score ?? 0:0.##}");
        Console.WriteLine($"目录: {Path.Combine(roll.SessionDirectory, "recovered")}");
        return recovered.Count == 0 ? 2 : 0;
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException($"{name} 缺少值。");
        return args[index + 1];
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令: {command}");
        Help();
        return 1;
    }

    private static void Help()
    {
        Console.WriteLine("""
Terraria Seed Roller CLI

  analyze <world.wld> [--profile 安全整洁] [--output folder]
  init [roller.json] [--force]
  roll --config roller.json [--resume | --fresh]
  sessions [--output folder]
  recover --config roller.json [--output folder]
  presets
  metrics

被中断的 Roll 种会在输出目录留下可继续的会话：
  roll --config roller.json --resume   继续最近一个被中断的会话
  roll --config roller.json --fresh    忽略中断会话，开始新会话
  recover --config roller.json         不生成新世界，直接导出中断会话里已完成的世界

退出码: 0 成功/通过，2 分析成功但未通过硬条件或无入选，130 已取消。
""");
    }
}
