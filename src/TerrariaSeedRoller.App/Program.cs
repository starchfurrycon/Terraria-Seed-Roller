using TerrariaSeedRoller.App.Design;
using TerrariaSeedRoller.App.Diagnostics;
using TerrariaSeedRoller.App.Dialogs;
using TerrariaSeedRoller.Core;

namespace TerrariaSeedRoller.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Must happen before the first window so the platform picks the dark theme.
        DarkMode.PrepareProcess();
        // The window, the analyser and the save path must stay responsive while
        // the servers this tool starts are competing for the same machine.
        ResourceGovernor.ProtectCurrentProcess();
        ApplicationConfiguration.Initialize();

        if (HasSwitch(args, "--make-icon"))
        {
            string output = ValueOf(args, "--make-icon") ?? Path.Combine(AppContext.BaseDirectory, "app.ico");
            return AppIconBuilder.Run(output);
        }

        if (HasSwitch(args, "--ui-shot"))
        {
            string directory = ValueOf(args, "--ui-shot") ?? Path.Combine(AppContext.BaseDirectory, "ui-shots");
            using MainForm shot = new();
            return UiShot.Run(shot, directory);
        }

        if (HasSwitch(args, "--ui-audit"))
        {
            string output = ValueOf(args, "--ui-audit") ?? Path.Combine(AppContext.BaseDirectory, "ui-audit.txt");
            if (File.Exists(output)) File.Delete(output);

            // Every built-in profile changes the criteria page (row count, badges,
            // hint text), so each one is laid out and audited, plus the empty case
            // and the add/edit dialog.
            using MainForm audited = new();
            int issues = 0;
            for (int i = 0; i < MainForm.PresetCount; i++)
            {
                audited.LoadPreset(i);
                issues += UiAudit.Run(audited, output);
            }
            audited.LoadEmptyCriteria();
            issues += UiAudit.Run(audited, output);

            using CriterionDialog dialog = new(null, "诊断");
            issues += UiAudit.Run(dialog, output);

            Console.WriteLine($"total issues across all windows: {issues}");
            return issues == 0 ? 0 : 2;
        }

        if (HasSwitch(args, "--smoke-test"))
        {
            using MainForm form = new();
            form.CreateControl();
            for (int i = 0; i < MainForm.PresetCount; i++)
            {
                form.LoadPreset(i);
                form.PerformLayout();
            }
            form.LoadEmptyCriteria();
            form.PerformLayout();
            Console.WriteLine($"smoke test ok ({MainForm.PresetCount} profiles)");
            return 0;
        }

        Application.Run(new MainForm());
        return 0;
    }

    private static bool HasSwitch(string[] args, string name)
        => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));

    private static string? ValueOf(string[] args, string name)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return arg[(name.Length + 1)..];
        }
        return null;
    }
}
