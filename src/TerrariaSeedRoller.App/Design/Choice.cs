namespace TerrariaSeedRoller.App.Design;

/// <summary>A display label paired with a strongly typed value for drop-down lists.</summary>
internal sealed record Choice<T>(string Name, T Value)
{
    public override string ToString() => Name;
}
