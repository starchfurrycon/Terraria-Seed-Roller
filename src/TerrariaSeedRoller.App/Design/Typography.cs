using System.Drawing.Text;

namespace TerrariaSeedRoller.App.Design;

/// <summary>
/// One typographic scale for the whole application. Fonts are created once and
/// live for the lifetime of the process.
/// </summary>
internal static class Typography
{
    private static readonly string Family = ResolveFamily();

    public static readonly Font Display = Create(16f, FontStyle.Bold);
    public static readonly Font Title = Create(12f, FontStyle.Bold);
    public static readonly Font Section = Create(10f, FontStyle.Bold);
    public static readonly Font Body = Create(9.5f, FontStyle.Regular);
    public static readonly Font BodyStrong = Create(9.5f, FontStyle.Bold);
    public static readonly Font Small = Create(8.5f, FontStyle.Regular);
    public static readonly Font SmallStrong = Create(8.5f, FontStyle.Bold);
    public static readonly Font Mono = CreateMono(9f);
    public static readonly Font MonoSmall = CreateMono(8.5f);

    public static string FamilyName => Family;

    private static Font Create(float size, FontStyle style) => new(Family, size, style, GraphicsUnit.Point);

    private static Font CreateMono(float size)
    {
        string[] candidates = ["Cascadia Mono", "Consolas", "Lucida Console", "Courier New"];
        foreach (string name in candidates)
        {
            if (IsInstalled(name)) return new Font(name, size, FontStyle.Regular, GraphicsUnit.Point);
        }
        return new Font(FontFamily.GenericMonospace, size, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static string ResolveFamily()
    {
        string[] candidates = ["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma"];
        foreach (string name in candidates)
        {
            if (IsInstalled(name)) return name;
        }
        return FontFamily.GenericSansSerif.Name;
    }

    private static bool IsInstalled(string name)
    {
        try
        {
            using InstalledFontCollection installed = new();
            foreach (FontFamily family in installed.Families)
            {
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception)
        {
            // Font enumeration can fail in restricted environments; fall through to the next candidate.
        }
        return false;
    }
}
