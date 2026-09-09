namespace TerrariaSeedRoller.App;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            using MainForm form = new();
            form.CreateControl();
            return 0;
        }
        Application.Run(new MainForm());
        return 0;
    }
}
