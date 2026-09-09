# Contributing

Keep the core dependency-free and preserve the read-only world guarantee.

Before a pull request:

```powershell
dotnet build TerrariaSeedRoller.sln -c Release
dotnet run --project tests/TerrariaSeedRoller.Tests -c Release --no-build
```

For parser changes, test a copied or generated fixture from every claimed world format. Never commit `.wld`, `.plr`, `.map`, Terraria binaries, decompiled game source, or game assets. New heuristics must be described as estimates in the UI/report when they are not exact vanilla simulation.
