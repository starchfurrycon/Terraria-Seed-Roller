using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TerrariaSeedRoller.Core;

public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Write(WorldAnalysis analysis, string outputDirectory, string? copiedSeed = null)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "analysis.json"), BuildJson(analysis, copiedSeed),
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "report.html"), BuildHtml(analysis, copiedSeed),
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "overview.svg"), BuildSvg(analysis),
            new UTF8Encoding(false));
    }

    public static void WriteSession(RollSessionResult session, RollProfile profile, string path)
    {
        object data = new
        {
            session.StartedAt,
            session.FinishedAt,
            session.Attempted,
            session.Completed,
            session.Failed,
            session.Cancelled,
            session.OutputDirectory,
            Profile = profile,
            Winners = session.Winners.Select((w, i) => new
            {
                Rank = i + 1,
                w.Seed,
                w.CopiedSeed,
                w.WorldPath,
                Score = Finite(w.Analysis.Evaluation?.Score ?? 0),
                HardCriteriaPassed = w.Analysis.Evaluation?.PassedHardCriteria ?? true,
                w.GenerationDuration,
                w.AnalysisDuration
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions), new UTF8Encoding(false));
    }

    public static string BuildJson(WorldAnalysis analysis, string? copiedSeed = null)
    {
        object data = new
        {
            SchemaVersion = 1,
            GeneratedAt = DateTimeOffset.Now,
            CopiedSeed = copiedSeed,
            analysis.WorldPath,
            analysis.Metadata,
            Metrics = analysis.Metrics.ToDictionary(p => p.Key, p => Finite(p.Value)),
            analysis.Regions,
            analysis.Chests,
            analysis.ImportantItems,
            Evaluation = analysis.Evaluation is null ? null : new
            {
                analysis.Evaluation.PassedHardCriteria,
                Score = Finite(analysis.Evaluation.Score),
                Criteria = analysis.Evaluation.Criteria.Select(c => new
                {
                    c.Criterion,
                    c.Passed,
                    Actual = Finite(c.Actual),
                    ScoreContribution = Finite(c.ScoreContribution),
                    c.Message
                })
            },
            Overview = new
            {
                analysis.OverviewWidth,
                analysis.OverviewHeight,
                ScaleTilesPerPixel = 8,
                Encoding = "x-major palette indices",
                DataBase64 = Convert.ToBase64String(analysis.Overview)
            }
        };
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    public static string BuildHtml(WorldAnalysis a, string? copiedSeed = null)
    {
        string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        StringBuilder body = new();
        body.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Terraria Seed Roller 报告</title><style>")
            .Append("body{margin:0;background:#101720;color:#dce7f2;font:15px system-ui;line-height:1.55}")
            .Append("main{max-width:1280px;margin:auto;padding:24px}h1,h2{color:#fff}.card{background:#172331;border:1px solid #2c4358;border-radius:12px;padding:18px;margin:16px 0}")
            .Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px}.stat{background:#0e1924;padding:10px;border-radius:8px}")
            .Append("table{border-collapse:collapse;width:100%}th,td{padding:7px 9px;border-bottom:1px solid #2b4053;text-align:left}th{position:sticky;top:0;background:#172331}")
            .Append(".map{background:#071019;overflow:auto;border-radius:8px}.ok{color:#7ee787}.bad{color:#ff7b72}.muted{color:#9eb1c4}code{color:#b7d8ff}.scroll{max-height:520px;overflow:auto}")
            .Append("</style></head><body><main>");
        body.Append("<h1>Terraria Seed Roller 世界报告</h1><div class=\"card grid\">")
            .Append(Stat("世界", E(a.Metadata.Title)))
            .Append(Stat("复制种子", E(copiedSeed ?? a.Metadata.SeedText)))
            .Append(Stat("格式", $"{a.Metadata.FileVersion}"))
            .Append(Stat("尺寸", $"{a.Metadata.Width} × {a.Metadata.Height}"))
            .Append(Stat("邪恶", a.Metadata.IsCrimson ? "猩红" : "腐化"))
            .Append(Stat("总分", a.Evaluation?.Score.ToString("0.##", CultureInfo.InvariantCulture) ?? "未评分"))
            .Append("</div>");

        body.Append("<section class=\"card\"><h2>目标专用地图</h2><p class=\"muted\">每像素为 8×8 格；只显示筛选相关生态、结构、液体与轨道，不会写入 .map 或解锁游戏内视野。</p><div class=\"map\">")
            .Append(BuildSvg(a)).Append("</div><p>颜色：紫=腐化，红=猩红，绿=丛林，白蓝=雪原，黄=沙漠，蓝=地牢，橙=神庙，粉=微光，红橙=岩浆。</p></section>");

        if (a.Evaluation is not null)
        {
            body.Append("<section class=\"card\"><h2>筛选结果</h2><p class=\"")
                .Append(a.Evaluation.PassedHardCriteria ? "ok\">硬条件通过" : "bad\">硬条件未通过")
                .Append("</p><table><thead><tr><th>条件</th><th>实际值</th><th>结果</th><th>分数贡献</th></tr></thead><tbody>");
            foreach (CriterionResult c in a.Evaluation.Criteria)
                body.Append("<tr><td>").Append(E(c.Criterion.Label ?? c.Criterion.MetricKey))
                    .Append("</td><td>").Append(Format(c.Actual)).Append("</td><td class=\"")
                    .Append(c.Passed ? "ok\">符合" : "bad\">不符合").Append("</td><td>")
                    .Append(c.ScoreContribution.ToString("0.##", CultureInfo.InvariantCulture)).Append("</td></tr>");
            body.Append("</tbody></table></section>");
        }

        body.Append("<section class=\"card\"><h2>重要物品</h2><div class=\"scroll\"><table><thead><tr><th>物品</th><th>坐标（格）</th><th>离出生点</th><th>获取成本</th><th>容器</th><th>阶段锁</th></tr></thead><tbody>");
        foreach (ItemFinding item in a.ImportantItems.OrderBy(i => i.EstimatedAccessCost))
            body.Append("<tr><td>").Append(E(item.Name)).Append(" × ").Append(item.Stack)
                .Append("</td><td>").Append(item.Position.X).Append(", ").Append(item.Position.Y)
                .Append("</td><td>").Append(item.DistanceFromSpawnTiles.ToString("0", CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(Format(item.EstimatedAccessCost)).Append("</td><td>")
                .Append(E(item.ChestKind)).Append("</td><td>").Append(item.ProgressionLocked ? "是" : "否").Append("</td></tr>");
        body.Append("</tbody></table></div></section>");

        body.Append("<section class=\"card\"><h2>全部指标</h2><div class=\"scroll\"><table><thead><tr><th>指标</th><th>键</th><th>值</th></tr></thead><tbody>");
        foreach ((string key, double value) in a.Metrics.OrderBy(p => p.Key))
        {
            string name = MetricCatalog.ByKey.TryGetValue(key, out MetricDefinition? definition)
                ? definition.ChineseName : key;
            body.Append("<tr><td>").Append(E(name)).Append("</td><td><code>").Append(E(key))
                .Append("</code></td><td>").Append(Format(value)).Append("</td></tr>");
        }
        body.Append("</tbody></table></div></section><section class=\"card\"><h2>解释边界</h2>")
            .Append("<p>本报告读取最终世界文件，因此宝箱、微光与结构结果来自真实生成结果。肉前传播闭包是依据可传播地表材料、三格跳跃与向日葵阻断作出的保守模型；获取成本是 8×8 降采样的相对排序，不是逐帧角色模拟。真剑冢可确定，但泰拉魔刃在击碎时才进行 1/30 随机，不能由种子筛选器承诺。</p>")
            .Append("</section></main></body></html>");
        return body.ToString();
    }

    public static string BuildSvg(WorldAnalysis a)
    {
        string[] colors =
        [
            "transparent", "#8754d6", "#d94b63", "#3f9d55", "#b9e6f2", "#d9b24c",
            "#3b70c8", "#d47a24", "#d5a03d", "#a8a8b8", "#d8d4d0", "#607080",
            "#42b89c", "#e7efff", "#8b5a2b", "#e993d7", "#e34d2f", "#bd9652"
        ];
        StringBuilder svg = new();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
            .Append(a.OverviewWidth).Append(' ').Append(a.OverviewHeight)
            .Append("\" width=\"100%\" role=\"img\" aria-label=\"World overview\"><rect width=\"100%\" height=\"100%\" fill=\"#071019\"/>");
        for (int y = 0; y < a.OverviewHeight; y++)
        {
            int x = 0;
            while (x < a.OverviewWidth)
            {
                byte color = a.Overview[x * a.OverviewHeight + y];
                int start = x++;
                while (x < a.OverviewWidth && a.Overview[x * a.OverviewHeight + y] == color) x++;
                if (color == 0 || color >= colors.Length) continue;
                svg.Append("<rect x=\"").Append(start).Append("\" y=\"").Append(y)
                    .Append("\" width=\"").Append(x - start).Append("\" height=\"1\" fill=\"")
                    .Append(colors[color]).Append("\"/>");
            }
        }
        double spawnX = (double)a.Metadata.Spawn.X / 8;
        double spawnY = (double)a.Metadata.Spawn.Y / 8;
        svg.Append("<circle cx=\"").Append(spawnX.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("\" cy=\"").Append(spawnY.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("\" r=\"3\" fill=\"#fff\" stroke=\"#000\" stroke-width=\"1\"/>")
            .Append("</svg>");
        return svg.ToString();
    }

    private static string Stat(string name, string value) =>
        $"<div class=\"stat\"><span class=\"muted\">{name}</span><br><strong>{value}</strong></div>";

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private static string Format(double value) => double.IsPositiveInfinity(value) ? "不存在" :
        double.IsNegativeInfinity(value) ? "−∞" : double.IsNaN(value) ? "无效" :
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
