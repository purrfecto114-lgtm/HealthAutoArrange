# Build / Test Report — 1.1.3

## Completed here

- Static source-contract suite: **56/56 PASS**.
- Coarse brace-balance check across all C# files: PASS.
- Version contract: plugin `1.1.3`, assembly `1.1.3.0`.
- Source review includes localization switching, reminder lifecycle, sorting/reminder separation, manager/refresh capture scoping, semantic numeric IDs, safe `#` matching, non-finite numeric rejection, and narrow English layout.

## Not possible in this container

No `dotnet`, `msbuild`, `csc`, or `mono` executable is installed. Direct DNS resolution for GitHub, NuGet and the .NET build host failed, so the toolchain could not be installed from inside the execution container. The plugin also requires the user's current `Assembly-CSharp.dll`, Unity Managed assemblies and BepInEx DLLs.

Therefore the following are **not claimed** as passed here:

```powershell
dotnet test HealthAutoArrange.Tests/HealthAutoArrange.Tests.csproj --configuration Release
dotnet build HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj --configuration Release -p:GameDir="<game folder>"
```

## Required release gate

Run the commands above on the current Steam build, then execute the in-game sequence in `SMOKE_TEST.md`, especially bilingual switching, sorting-disabled/reminder-enabled behavior, side Moodle hover/health-panel lifecycle, QoL + CUCoreLib coexistence, and a semantic-numeric third-party Moodle if available.
